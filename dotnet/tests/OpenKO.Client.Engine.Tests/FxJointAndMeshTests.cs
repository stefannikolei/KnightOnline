using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Fx;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Slice-10.4 pins: the FX joint-anchor maths (CPlayerBase::JointPosGet) and the
/// mesh part's parent transform (CN3FXPartMesh::Tick, Move mode).
/// </summary>
public class FxJointAndMeshTests
{
    [Fact]
    public void WorldPos_TransformsJointTranslationByCharacterWorld()
    {
        // Joint local matrix: translation (1, 2, 3), plus an orientation that
        // JointPosGet ignores (position only).
        Matrix4x4 joint = Matrix4x4.CreateRotationZ(0.7f);
        joint.Translation = new Vector3(1f, 2f, 3f);

        // Character world: scale 2 then translate to (10, 0, -5).
        Matrix4x4 chrWorld = Matrix4x4.CreateScale(2f);
        chrWorld.Translation = new Vector3(10f, 0f, -5f);

        Vector3 world = FxJointMath.WorldPos(joint, chrWorld);

        Assert.Equal(Vector3.Transform(new Vector3(1f, 2f, 3f), chrWorld), world);
        // Explicit: (1,2,3)*2 + (10,0,-5) = (12, 4, 1).
        Assert.Equal(new Vector3(12f, 4f, 1f), world);
    }

    [Fact]
    public void WorldPos_IsPositionOnly_IgnoresJointOrientation()
    {
        Matrix4x4 spun = Matrix4x4.CreateRotationX(1.3f) * Matrix4x4.CreateRotationY(-0.4f);
        spun.Translation = new Vector3(4f, 5f, 6f);
        Matrix4x4 unspun = Matrix4x4.Identity;
        unspun.Translation = new Vector3(4f, 5f, 6f);

        Assert.Equal(
            FxJointMath.WorldPos(unspun, Matrix4x4.Identity),
            FxJointMath.WorldPos(spun, Matrix4x4.Identity));
    }

    [Fact]
    public void MeshParentMatrix_ScalesByUnitSizeAndTranslatesToBundlePlusPartPos()
    {
        var desc = new N3FXPartMesh
        {
            Life = 0f,
            FadeIn = 0f,
            UnitScale = new Vector3(2f, 3f, 4f),
            ScaleVelocity = Vector3.Zero,
            Pos = new Vector3(0f, 1f, 0f),
        };
        var sim = new FxMeshSimulator(desc);
        sim.Start();

        var context = new FxBundleContext
        {
            Pos = new Vector3(10f, 0f, 5f),
            Dir = new Vector3(0f, 0f, 1f),
            DependScale = false,
            TargetScale = 1f,
        };

        sim.Advance(0f, context, null);

        // Scale from the diagonal, translation from bundlePos + partPos.
        Assert.Equal(2f, sim.ParentMatrix.M11, 4);
        Assert.Equal(3f, sim.ParentMatrix.M22, 4);
        Assert.Equal(4f, sim.ParentMatrix.M33, 4);
        Assert.Equal(new Vector3(10f, 1f, 5f), sim.ParentMatrix.Translation);
    }

    [Fact]
    public void MeshParentMatrix_DependScaleMultipliesByTargetScale()
    {
        var desc = new N3FXPartMesh { Life = 0f, FadeIn = 0f, UnitScale = Vector3.One };
        var sim = new FxMeshSimulator(desc);
        sim.Start();

        var context = new FxBundleContext
        {
            Pos = Vector3.Zero,
            Dir = new Vector3(0f, 0f, 1f),
            DependScale = true,
            TargetScale = 2.5f,
        };

        sim.Advance(0f, context, null);

        Assert.Equal(2.5f, sim.ParentMatrix.M11, 4);
        Assert.Equal(2.5f, sim.ParentMatrix.M22, 4);
        Assert.Equal(2.5f, sim.ParentMatrix.M33, 4);
    }
}
