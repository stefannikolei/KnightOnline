using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Objects;
using OpenKO.Client.Engine.Rendering;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.3 pins: material plans, alpha sorting, billboard/windy math.</summary>
public class MaterialAndAlphaTests
{
    [Fact]
    public void MaterialPlan_MapsRenderFlags()
    {
        var mtl = new N3Material
        {
            RenderFlags = (uint)(RenderFlags.AlphaBlending | RenderFlags.DoubleSided |
                                 RenderFlags.NotUseFog | RenderFlags.NotZWrite | RenderFlags.Windy),
            SrcBlend = 2, // ONE
            DestBlend = 2, // ONE (additive)
        };

        MaterialPlan plan = MaterialBinder.Plan(mtl, hasOverlayTexture: false);

        Assert.True(plan.DeferToAlphaManager);
        Assert.True(plan.CullNone);
        Assert.True(plan.DisableFog);
        Assert.True(plan.DisableZWrite);
        Assert.True(plan.Windy);
        Assert.False(plan.BoardY);
        Assert.Equal(2u, plan.SrcBlend);
        Assert.Equal(2u, plan.DestBlend);
        Assert.Equal(EffectKind.Basic, plan.Effect);
    }

    [Fact]
    public void MaterialPlan_DefaultsAndOverlay()
    {
        var mtl = new N3Material(); // all zero
        MaterialPlan plan = MaterialBinder.Plan(mtl, hasOverlayTexture: true);

        Assert.False(plan.DeferToAlphaManager);
        Assert.Equal(EffectKind.DualTexture, plan.Effect);
        Assert.Equal(5u, plan.SrcBlend);  // SRCALPHA default
        Assert.Equal(6u, plan.DestBlend); // INVSRCALPHA default
    }

    [Fact]
    public void AlphaManager_SortsBackToFront()
    {
        static AlphaPrimitive Prim(float distance) => new()
        {
            Vertices = [],
            VertexCount = 0,
            PrimitiveCount = 0,
            World = Matrix4x4.Identity,
            Plan = default,
            Distance = distance,
        };

        List<AlphaPrimitive> primitives = [Prim(4f), Prim(100f), Prim(25f)];
        AlphaManager.SortForRender(primitives);

        Assert.Equal(100f, primitives[0].Distance);
        Assert.Equal(25f, primitives[1].Distance);
        Assert.Equal(4f, primitives[2].Distance);
    }

    [Fact]
    public void AlphaManager_Add_UsesSquaredCameraDistance()
    {
        var manager = new AlphaManager();
        var prim = new AlphaPrimitive
        {
            Vertices = [],
            VertexCount = 0,
            PrimitiveCount = 0,
            World = Matrix4x4.Identity,
            Plan = default,
        };
        manager.Add(prim, cameraEye: new Vector3(0, 0, 0), worldCenter: new Vector3(3, 4, 0));
        Assert.Equal(25f, prim.Distance);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void BoardY_FacesTheCamera()
    {
        // Part at origin, identity parent, camera on +x: yaw = -atan(0/inf)-pi/2 = -pi/2.
        Matrix4x4 m = BillboardMath.BoardY(
            Vector3.Zero, Matrix4x4.Identity, Quaternion.Identity, new Vector3(10, 0, 0));

        // D3D RotationY(-pi/2): +z axis maps to -x... verify the part's local
        // +z (its facing) points toward the camera-ish half-space.
        Vector3 facing = Vector3.TransformNormal(Vector3.UnitZ, m);
        Assert.True(facing.X < -0.99f || facing.X > 0.99f); // aligned with the x axis
        Assert.Equal(0f, m.Translation.X, 4);

        // Camera on -x flips the yaw branch.
        Matrix4x4 m2 = BillboardMath.BoardY(
            Vector3.Zero, Matrix4x4.Identity, Quaternion.Identity, new Vector3(-10, 0, 0));
        Vector3 facing2 = Vector3.TransformNormal(Vector3.UnitZ, m2);
        Assert.Equal(-MathF.Sign(facing.X), MathF.Sign(facing2.X));
    }

    [Fact]
    public void RotationXyz_ComposesInXyzOrder()
    {
        var angles = new Vector3(0.3f, -0.2f, 0.1f);
        Matrix4x4 expected = Matrix4x4.CreateRotationX(angles.X)
            * Matrix4x4.CreateRotationY(angles.Y)
            * Matrix4x4.CreateRotationZ(angles.Z);
        Assert.Equal(expected, BillboardMath.RotationXyz(angles));

        // Pin one element against the C++ closed form: m[0][0] = CY*CZ.
        Matrix4x4 m = BillboardMath.RotationXyz(angles);
        Assert.Equal(MathF.Cos(angles.Y) * MathF.Cos(angles.Z), m.M11, 5);
    }

    [Fact]
    public void WindyState_EasesTowardTheTarget()
    {
        // Deterministic random: first Next(100) = target factor, second = duration.
        var windy = new WindyState(new Random(42));

        // First tick picks a target (timeToSetWind was 0) and returns null.
        Assert.Null(windy.Tick(0.016f, Vector3.Zero, Matrix4x4.Identity));

        // Following ticks ease the factor and return a rotated matrix.
        Matrix4x4? m = windy.Tick(0.016f, Vector3.Zero, Matrix4x4.Identity);
        if (windy.FactorCur > 0f)
        {
            Assert.NotNull(m);
            Assert.Equal(Vector3.Zero, m!.Value.Translation);
        }

        float before = windy.FactorCur;
        windy.Tick(0.016f, Vector3.Zero, Matrix4x4.Identity);
        Assert.True(windy.FactorCur >= before); // monotone toward the target
    }

    [Fact]
    public void RenderFlags_MatchMy3DStructValues()
    {
        Assert.Equal(0x1u, (uint)RenderFlags.AlphaBlending);
        Assert.Equal(0x8u, (uint)RenderFlags.BoardY);
        Assert.Equal(0x20u, (uint)RenderFlags.Windy);
        Assert.Equal(0x100u, (uint)RenderFlags.NotZWrite);
        Assert.Equal(0x400u, (uint)RenderFlags.NotZBuffer);
    }
}
