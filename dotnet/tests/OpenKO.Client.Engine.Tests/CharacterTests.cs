using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Objects;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.4 pins: CPU skinning, the frame machine and blending.</summary>
public class CharacterTests
{
    private static N3Joint MakeTwoBoneSkeleton()
    {
        // Root at origin, child 1 unit up; child carries a rotation channel.
        var root = new N3Joint { Name = "root" };
        var child = new N3Joint { Name = "child", Position = new Vector3(0, 1, 0) };
        root.ChildAdd(child);
        return root;
    }

    [Fact]
    public void ComputeBindPose_CollectsPreOrderAndInverts()
    {
        N3Joint root = MakeTwoBoneSkeleton();
        (N3Joint[] joints, Matrix4x4[] inverse) = SkinDeformer.ComputeBindPose(root);

        Assert.Equal(2, joints.Length);
        Assert.Same(root, joints[0]);
        Assert.Equal("child", joints[1].Name);

        // invBind * bind == identity.
        Matrix4x4 product = inverse[1] * joints[1].Matrix;
        Assert.Equal(1f, product.M11, 4);
        Assert.Equal(0f, product.M41, 4);
        Assert.Equal(0f, product.M42, 4);
    }

    [Fact]
    public void Deform_SingleBone_FollowsTheJoint()
    {
        N3Joint root = MakeTwoBoneSkeleton();
        (N3Joint[] joints, Matrix4x4[] inverse) = SkinDeformer.ComputeBindPose(root);

        var skin = new N3Skin();
        skin.Initialize(
            faceCount: 1,
            vertices:
            [
                new N3VertexXyzNormal { Position = new Vector3(0.5f, 1f, 0f), Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = new Vector3(0f, 1f, 0f), Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = new Vector3(0f, 1f, 0.5f), Normal = Vector3.UnitY },
            ],
            vertexIndices: [0, 1, 2],
            uvs: [0f, 0f, 1f, 0f, 0f, 1f],
            uvIndices: [0, 1, 2]);
        skin.InitializeSkin(
        [
            new N3SkinVertex { Origin = new Vector3(0.5f, 1f, 0f), Joints = [1] }, // bound to the child
            new N3SkinVertex { Origin = new Vector3(0f, 1f, 0f), Joints = [1] },
            new N3SkinVertex { Origin = new Vector3(0f, 1f, 0.5f) },              // unbound: keeps bind pos
        ]);

        // Move the child joint up by 1: bound vertices must follow.
        N3Joint child = root.Children[0];
        child.Position = new Vector3(0, 2, 0);
        root.Tick(0f);
        var jointMatrices = new Matrix4x4[] { joints[0].Matrix, joints[1].Matrix };

        var dest = new Vector3[3];
        SkinDeformer.Deform(skin, jointMatrices, inverse, dest);

        Assert.Equal(new Vector3(0.5f, 2f, 0f), dest[0]);
        Assert.Equal(new Vector3(0f, 2f, 0f), dest[1]);
        Assert.Equal(new Vector3(0f, 1f, 0.5f), dest[2]); // untouched (nAffect 0)

        VertexPositionNormalTexture[] flat = SkinDeformer.Flatten(skin, dest);
        Assert.Equal(3, flat.Length);
        Assert.Equal(2f, flat[0].Position.Y);
        Assert.Equal(1f, flat[1].TextureCoordinate.X);
    }

    [Fact]
    public void Deform_WeightedTwoBones_Averages()
    {
        N3Joint root = MakeTwoBoneSkeleton();
        (N3Joint[] joints, Matrix4x4[] inverse) = SkinDeformer.ComputeBindPose(root);

        var skin = new N3Skin();
        skin.Initialize(
            faceCount: 1,
            vertices: [new N3VertexXyzNormal { Position = new Vector3(0f, 1f, 0f) },
                       new N3VertexXyzNormal(), new N3VertexXyzNormal()],
            vertexIndices: [0, 0, 0],
            uvs: [],
            uvIndices: []);
        skin.InitializeSkin(
        [
            new N3SkinVertex { Origin = new Vector3(0f, 1f, 0f), Joints = [0, 1], Weights = [0.5f, 0.5f] },
            new N3SkinVertex(),
            new N3SkinVertex(),
        ]);

        // Child moves up 1 → its half pulls the vertex up 0.5.
        root.Children[0].Position = new Vector3(0, 2, 0);
        root.Tick(0f);
        var jointMatrices = new Matrix4x4[] { joints[0].Matrix, joints[1].Matrix };

        var dest = new Vector3[3];
        SkinDeformer.Deform(skin, jointMatrices, inverse, dest);
        Assert.Equal(1.5f, dest[0].Y, 4);
    }

    [Fact]
    public void AnimPlayer_LoopsWithinTheClipWindow()
    {
        var player = new AnimPlayer();
        var clip = new N3AnimData { Name = "Walk", FrmStart = 10f, FrmEnd = 20f, FrmPerSec = 30f };
        player.SetAnim(clip);

        Assert.Equal(10f, player.FrmCur);

        player.Tick(0.1f); // +3 frames
        Assert.Equal(13f, player.FrmCur, 3);
        Assert.Equal(10f, player.FrmPrev, 3);

        // Push past the end: wraps by the window length.
        player.Tick(0.3f); // 13 + 9 = 22 -> 22 - 10 = 12
        Assert.Equal(12f, player.FrmCur, 3);
        Assert.Equal(1, player.AniLoop);
    }

    [Fact]
    public void AnimPlayer_OnceAndFreeze_StopsAtTheEnd()
    {
        var player = new AnimPlayer();
        player.SetAnim(new N3AnimData { FrmStart = 0f, FrmEnd = 5f, FrmPerSec = 30f }, onceAndFreeze: true);

        player.Tick(1f); // way past the end
        Assert.Equal(5f, player.FrmCur);
        Assert.Equal(1, player.AniLoop);

        player.Tick(0.1f);
        Assert.Equal(5f, player.FrmCur); // frozen
    }

    [Fact]
    public void AnimPlayer_LoopDelay_BlendsBeforeRestarting()
    {
        var player = new AnimPlayer();
        var clip = new N3AnimData
        {
            FrmStart = 0f,
            FrmEnd = 3f,
            FrmPerSec = 30f,
            BlendFlags = 1,     // loop delay
            TimeBlend = 0.2f,
        };
        player.SetAnim(clip);

        player.Tick(0.2f); // past end -> delay starts
        Assert.Equal(3f, player.FrmCur);
        Assert.True(player.ProcessingDelayNow);
        Assert.Equal(1, player.AniLoop);

        // Next tick arms the blend back to the start frame.
        player.Tick(0.05f);
        Assert.Equal(0f, player.FrmCur);
        Assert.Equal(3f, player.BlendFrm);
        Assert.True(player.BlendTime > 0f);
        Assert.True(player.BlendFactor is > 0f and <= 1f);

        // Blend completes after TimeBlend.
        player.Tick(0.3f);
        Assert.Equal(0f, player.BlendTime);
        Assert.False(player.ProcessingDelayNow);
    }

    [Fact]
    public void ReCalcMatrixBlended_BlendsPositionChannels()
    {
        var joint = new N3Joint();
        joint.KeyPos.InitializeVector3([new Vector3(0, 0, 0), new Vector3(0, 30, 0)]);

        // frame0 = 1 (pos 30), frame1 = 0 (pos 0), weight0 = 0.75.
        joint.ReCalcMatrixBlended(1f, 0f, 0.75f);
        Assert.Equal(22.5f, joint.Matrix.Translation.Y, 3);
    }

    [Fact]
    public void PlugMatrixChain_ComposesLocalJointParent()
    {
        // Plug local: scale 2, identity rot, pos (1,0,0) -> translation (2,0,0).
        var plugLocal = Matrix4x4.CreateScale(2f);
        plugLocal.Translation = new Vector3(1f, 0f, 0f) * 2f;

        Matrix4x4 joint = Matrix4x4.CreateTranslation(0f, 5f, 0f);
        Matrix4x4 parent = Matrix4x4.CreateTranslation(10f, 0f, 0f);

        Matrix4x4 world = plugLocal * joint * parent;
        Vector3 origin = Vector3.Transform(Vector3.Zero, world);
        Assert.Equal(new Vector3(12f, 5f, 0f), origin);
    }
}
