using System.Numerics;
using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-5.4 pins: keyframe channels, transforms/joints, clips and skins.</summary>
public class N3AnimTests
{
    // ---- N3AnimKey ----

    [Fact]
    public void AnimKey_Load_DuplicatesLastKey()
    {
        var original = new N3AnimKey();
        original.InitializeVector3([new Vector3(0, 0, 0), new Vector3(10, 0, 0)]);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        // [count][type][rate][2 * 12 bytes] — the +1 slot is memory-only.
        Assert.Equal(4 + 4 + 4 + 24, stream.Length);

        stream.Position = 0;
        var loaded = new N3AnimKey();
        loaded.Load(new BinaryReader(stream));

        Assert.Equal(2, loaded.Count);
        Assert.Equal(3, loaded.Vector3Keys.Length);
        Assert.Equal(loaded.Vector3Keys[1], loaded.Vector3Keys[2]); // duplicated
    }

    [Fact]
    public void AnimKey_TryGetVector3_InterpolatesAt30Fps()
    {
        var key = new N3AnimKey();
        key.InitializeVector3([new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 20, 0)]);

        var v = Vector3.Zero;
        Assert.True(key.TryGetVector3(0.5f, ref v));
        Assert.Equal(new Vector3(5, 0, 0), v);

        Assert.True(key.TryGetVector3(1.5f, ref v));
        Assert.Equal(new Vector3(10, 10, 0), v);

        // frame == count clamps to the last key (delta 0).
        Assert.True(key.TryGetVector3(3f, ref v));
        Assert.Equal(new Vector3(10, 20, 0), v);

        // Past the C++ bound (index > count) the sample fails.
        Assert.False(key.TryGetVector3(4.1f, ref v));
        Assert.False(key.TryGetVector3(-1f, ref v));
    }

    [Fact]
    public void AnimKey_TryGetVector3_HonorsSamplingRate()
    {
        // 15 samples/sec: key i sits at 30fps frame i*2.
        var key = new N3AnimKey();
        key.InitializeVector3([new Vector3(0, 0, 0), new Vector3(10, 0, 0)], samplingRate: 15f);

        var v = Vector3.Zero;
        Assert.True(key.TryGetVector3(1f, ref v)); // halfway between the keys
        Assert.Equal(new Vector3(5, 0, 0), v);
    }

    [Fact]
    public void AnimKey_TryGetQuaternion_SlerpsBetweenKeys()
    {
        var q0 = Quaternion.Identity;
        var q1 = new Quaternion(0f, MathF.Sin(MathF.PI / 4f), 0f, MathF.Cos(MathF.PI / 4f)); // 90° yaw
        var key = new N3AnimKey();
        key.InitializeQuaternion([q0, q1]);

        var q = Quaternion.Identity;
        Assert.True(key.TryGetQuaternion(0.5f, ref q));
        Assert.Equal(MathF.Sin(MathF.PI / 8f), q.Y, 5); // 45°
        Assert.Equal(MathF.Cos(MathF.PI / 8f), q.W, 5);
    }

    // ---- N3Transform / N3Joint ----

    [Fact]
    public void Transform_RoundTrips_AndComposesMatrixLikeD3D()
    {
        var original = new N3Transform
        {
            Name = "xform",
            Position = new Vector3(1, 2, 3),
            Rotation = new Quaternion(0f, MathF.Sin(MathF.PI / 4f), 0f, MathF.Cos(MathF.PI / 4f)),
            Scale = new Vector3(2, 2, 2),
        };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        // [4+5 name][12 pos][16 rot][12 scale][3 * 4 empty keys]
        Assert.Equal(9 + 40 + 12, stream.Length);

        stream.Position = 0;
        var loaded = new N3Transform();
        loaded.Load(new BinaryReader(stream));

        Assert.Equal(original.Position, loaded.Position);
        Assert.Equal(original.Scale, loaded.Scale);

        // (1,0,0) -> scale 2 -> yaw 90° maps +x to -z (D3D row-vector) -> +pos.
        Vector3 p = Vector3.Transform(new Vector3(1, 0, 0), loaded.Matrix);
        Assert.Equal(1f, p.X, 4);
        Assert.Equal(2f, p.Y, 4);
        Assert.Equal(1f, p.Z, 4); // 3 + (-2)
    }

    [Fact]
    public void Joint_RoundTrips_Hierarchy_AndTickComposesParentMatrices()
    {
        var root = new N3Joint { Name = "root", Position = new Vector3(0, 1, 0) };
        var child = new N3Joint { Name = "child", Position = new Vector3(0, 1, 0) };
        var grandChild = new N3Joint { Name = "grandchild", Position = new Vector3(2, 0, 0) };
        root.ChildAdd(child);
        child.ChildAdd(grandChild);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            root.Save(writer);
        }

        stream.Position = 0;
        var loaded = new N3Joint();
        loaded.Load(new BinaryReader(stream));
        Assert.Equal(stream.Length, stream.Position);

        Assert.Equal(3, loaded.NodeCount());
        Assert.Equal("child", loaded.Children[0].Name);
        Assert.Same(loaded, loaded.Children[0].Parent);
        Assert.Equal("grandchild", loaded.FindById(2)!.Name);
        Assert.Equal("root", loaded.FindById(0)!.Name);

        loaded.Tick(0f);
        Assert.Equal(new Vector3(0, 2, 0), loaded.Children[0].Matrix.Translation);
        Assert.Equal(new Vector3(2, 2, 0), loaded.Children[0].Children[0].Matrix.Translation);

        var palette = new Matrix4x4[3];
        int index = 0;
        loaded.MatricesGet(palette, ref index);
        Assert.Equal(3, index);
        Assert.Equal(new Vector3(0, 1, 0), palette[0].Translation);
        Assert.Equal(new Vector3(2, 2, 0), palette[2].Translation);
    }

    [Fact]
    public void Joint_AnimatedTick_SamplesChannels()
    {
        var root = new N3Joint { Name = "animated" };
        root.KeyPos.InitializeVector3([new Vector3(0, 0, 0), new Vector3(0, 10, 0)]);

        root.Tick(0.5f);
        Assert.Equal(new Vector3(0, 5, 0), root.Matrix.Translation);
    }

    // ---- N3AnimControl ----

    [Fact]
    public void AnimControl_RoundTrips_WithoutNameHeader()
    {
        var original = new N3AnimControl { Name = "ignored - Load skips the name header" };
        original.Clips.Add(new N3AnimData
        {
            Name = "Walk",
            FrmStart = 10f,
            FrmEnd = 40f,
            FrmPerSec = 30f,
            TimeBlend = 0.2f,
            BlendFlags = 1,
            FrmStrike0 = 25f,
        });
        original.Clips.Add(new N3AnimData { Name = "Attack", FrmStart = 50f, FrmEnd = 80f });

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        // [4 count] + per clip: [4 legacy][11*4 fields][4 + nameLen].
        Assert.Equal(4 + (4 + 44 + 4) * 2 + 4 + 6, stream.Length);

        stream.Position = 0;
        var loaded = new N3AnimControl();
        loaded.Load(new BinaryReader(stream));

        Assert.Equal(2, loaded.Clips.Count);
        Assert.Equal("Walk", loaded.Clips[0].Name);
        Assert.Equal(40f, loaded.Clips[0].FrmEnd);
        Assert.Equal(1, loaded.Clips[0].BlendFlags);
        Assert.Equal(25f, loaded.Clips[0].FrmStrike0);
        Assert.Equal("Attack", loaded.Clips[1].Name);
        Assert.Equal(string.Empty, loaded.Name); // no name header in the format
    }

    // ---- N3Skin ----

    [Fact]
    public void Skin_RoundTrips_AllAffectVariants()
    {
        var original = new N3Skin { Name = "skin" };
        original.Initialize(
            faceCount: 1,
            vertices:
            [
                new N3VertexXyzNormal { Position = new Vector3(0, 0, 0), Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = new Vector3(1, 0, 0), Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = new Vector3(0, 0, 1), Normal = Vector3.UnitY },
            ],
            vertexIndices: [0, 1, 2],
            uvs: [],
            uvIndices: []);
        original.InitializeSkin(
        [
            new N3SkinVertex { Origin = new Vector3(0, 0, 0) },                    // nAffect 0
            new N3SkinVertex { Origin = new Vector3(1, 0, 0), Joints = [4] },      // nAffect 1: no weights
            new N3SkinVertex                                                       // nAffect 2
            {
                Origin = new Vector3(0, 0, 1),
                Joints = [4, 7],
                Weights = [0.75f, 0.25f],
            },
        ]);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        stream.Position = 0;
        var loaded = new N3Skin();
        loaded.Load(new BinaryReader(stream));
        Assert.Equal(stream.Length, stream.Position);

        Assert.Equal(3, loaded.SkinVertices.Length);
        Assert.Empty(loaded.SkinVertices[0].Joints);
        Assert.Equal([4], loaded.SkinVertices[1].Joints);
        Assert.Empty(loaded.SkinVertices[1].Weights); // single joint stores no weight
        Assert.Equal([4, 7], loaded.SkinVertices[2].Joints);
        Assert.Equal([0.75f, 0.25f], loaded.SkinVertices[2].Weights);
        Assert.Equal(new Vector3(0, 0, 1), loaded.SkinVertices[2].Origin);
    }
}
