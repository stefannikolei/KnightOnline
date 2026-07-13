using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.1 pins: interop, state mapping, camera/fog math, timing.</summary>
public class EngineFoundationTests
{
    [Fact]
    public void ColorInterop_UnpacksArgbChannels()
    {
        Microsoft.Xna.Framework.Color c = ColorInterop.FromArgb(0x80FF4020);
        Assert.Equal(0xFF, c.R);
        Assert.Equal(0x40, c.G);
        Assert.Equal(0x20, c.B);
        Assert.Equal(0x80, c.A);
        Assert.Equal(0x80FF4020u, ColorInterop.ToArgb(c));

        // 0xffffffff (the pervasive KO default) is opaque white.
        Assert.Equal(Microsoft.Xna.Framework.Color.White, ColorInterop.FromArgb(0xFFFFFFFF));
    }

    [Fact]
    public void XnaInterop_MatrixRoundTrips_ElementWise()
    {
        Matrix4x4 m = Matrix4x4.CreateTranslation(1, 2, 3) * Matrix4x4.CreateScale(2);
        Microsoft.Xna.Framework.Matrix x = m.ToXna();
        Assert.Equal(m.M41, x.M41);
        Assert.Equal(m.M11, x.M11);
        Assert.Equal(m, x.ToNumerics());
    }

    [Fact]
    public void FanIndexer_QuadBecomesTwoTriangles()
    {
        Assert.Equal(new short[] { 0, 1, 2, 0, 2, 3 }, FanIndexer.Build(4));
        Assert.Empty(FanIndexer.Build(2));

        var list = new List<short>();
        FanIndexer.Append(list, baseVertex: 4, vertexCount: 4);
        Assert.Equal(new short[] { 4, 5, 6, 4, 6, 7 }, list.ToArray());
    }

    [Theory]
    [InlineData(1u, Blend.Zero)]
    [InlineData(2u, Blend.One)]
    [InlineData(5u, Blend.SourceAlpha)]
    [InlineData(6u, Blend.InverseSourceAlpha)]
    [InlineData(9u, Blend.DestinationColor)]
    public void RenderStateMapper_MapsD3dBlendValues(uint d3d, Blend expected)
    {
        Assert.Equal(expected, RenderStateMapper.ToBlend(d3d));
    }

    [Fact]
    public void RenderStateMapper_MapsD3dCompareAndCachesBlendStates()
    {
        Assert.Equal(CompareFunction.Greater, RenderStateMapper.ToCompareFunction(5)); // D3DCMP_GREATER (alpha test)
        Assert.Equal(CompareFunction.LessEqual, RenderStateMapper.ToCompareFunction(4));

        BlendState a = RenderStateMapper.GetBlendState(5, 6); // SRCALPHA/INVSRCALPHA
        BlendState b = RenderStateMapper.GetBlendState(5, 6);
        Assert.Same(a, b);
        Assert.Equal(Blend.SourceAlpha, a.ColorSourceBlend);
        Assert.Equal(Blend.InverseSourceAlpha, a.ColorDestinationBlend);

        BlendState additive = RenderStateMapper.GetBlendState(2, 2); // ONE/ONE
        Assert.Equal(Blend.One, additive.ColorSourceBlend);
        Assert.Equal(Blend.One, additive.ColorDestinationBlend);
    }

    [Fact]
    public void Camera_LeftHandedMatrices_MatchD3dConventions()
    {
        var cam = new N3EngineCamera
        {
            Eye = new Vector3(0, 0, -10),
            At = Vector3.Zero,
            Up = Vector3.UnitY,
            Fov = MathF.PI / 2f,
            Aspect = 1f,
            NearPlane = 1f,
            FarPlane = 100f,
        };
        cam.Update();

        // LH view from -z looking at origin: +z world stays +z view (depth grows away).
        Vector3 p = Vector3.Transform(new Vector3(0, 0, 5), cam.View);
        Assert.Equal(15f, p.Z, 4);

        // Projection: near plane maps to z'=0, far to z'=1 after w-divide (D3D depth).
        Vector4 nearClip = Vector4.Transform(new Vector4(0, 0, -10f + 1f, 1), cam.ViewProjection);
        Assert.Equal(0f, nearClip.Z / nearClip.W, 4);
        Vector4 farClip = Vector4.Transform(new Vector4(0, 0, -10f + 100f, 1), cam.ViewProjection);
        Assert.Equal(1f, farClip.Z / farClip.W, 4);

        // FOV 90°, aspect 1: at distance d the frustum half-width is d.
        Vector4 edge = Vector4.Transform(new Vector4(10, 0, 0, 1), cam.ViewProjection);
        Assert.Equal(1f, edge.X / edge.W, 4);

        Assert.Equal(70f * MathF.PI / 180f, N3EngineCamera.GameFov, 5);
        Assert.Equal(0.96f, N3EngineCamera.CharSelectFov);
    }

    [Fact]
    public void Frustum_SphereTests_MatchCameraSetup()
    {
        var cam = new N3EngineCamera
        {
            Eye = new Vector3(0, 0, -10),
            At = Vector3.Zero,
            Fov = MathF.PI / 2f,
            Aspect = 1f,
            NearPlane = 1f,
            FarPlane = 100f,
        };
        cam.Update();

        Assert.False(cam.Frustum.IsOutOfFrustum(Vector3.Zero, 1f));            // dead center
        Assert.True(cam.Frustum.IsOutOfFrustum(new Vector3(0, 0, -20), 1f));   // behind the camera
        Assert.True(cam.Frustum.IsOutOfFrustum(new Vector3(0, 0, 200), 1f));   // past far plane
        Assert.True(cam.Frustum.IsOutOfFrustum(new Vector3(50, 0, 0), 1f));    // far off to the side
        Assert.False(cam.Frustum.IsOutOfFrustum(new Vector3(0, 0, 95), 10f));  // overlaps far plane
        Assert.False(cam.Frustum.IsOutOfFrustum(new Vector3(11, 0, 0), 2f));   // just outside, radius reaches in
    }

    [Fact]
    public void FogMapper_FitBracketsTheExp2Curve()
    {
        const float farPlane = 512f;
        float density = FogMapper.DensityFromFarPlane(farPlane);
        Assert.Equal(1f / (0.37f * farPlane), density, 6);

        (float start, float end) = FogMapper.FromFarPlane(farPlane);

        // The linear window must sit inside the visible range and be ordered.
        Assert.True(0f < start && start < end);

        // At the fit points the linear factor equals the EXP2 factor.
        float dHigh = MathF.Sqrt(-MathF.Log(0.99f)) / density;
        float dLow = MathF.Sqrt(-MathF.Log(0.02f)) / density;
        float LinearFactor(float d) => Math.Clamp((end - d) / (end - start), 0f, 1f);
        Assert.Equal(0.99f, LinearFactor(dHigh), 3);
        Assert.Equal(0.02f, LinearFactor(dLow), 3);
        Assert.Equal(0.99f, FogMapper.Exp2Factor(dHigh, density), 3);
        Assert.Equal(0.02f, FogMapper.Exp2Factor(dLow, density), 3);

        // EXP2 with the C++ density is fully fogged well before the far plane.
        Assert.True(end < farPlane);
    }

    [Fact]
    public void DualTextureCompensation_TurnsModulate2XIntoPlainModulate()
    {
        // D3D9 two-stage MODULATE: tex0 · tex1 · diffuse.
        const float tex0 = 0.6f, tex1 = 0.8f, diffuse = 0.9f;
        float expected = tex0 * tex1 * diffuse;
        Assert.Equal(expected, DualTextureCompensation.CompensatedRgb(tex0, tex1, diffuse), 6);
    }

    [Fact]
    public void FrameTimer_SnapsImplausibleDeltasTo30Fps()
    {
        var timer = new FrameTimer();
        timer.Tick(0.016);
        Assert.Equal(0.016f, timer.SecPerFrame, 5);

        timer.Tick(2.5); // hitch >= 1s -> snap
        Assert.Equal(FrameTimer.FallbackSecPerFrame, timer.SecPerFrame);

        timer.Tick(0.0);  // <= 1ms -> snap
        Assert.Equal(FrameTimer.FallbackSecPerFrame, timer.SecPerFrame);

        Assert.Equal(2.516, timer.TotalSeconds, 3);
    }
}
