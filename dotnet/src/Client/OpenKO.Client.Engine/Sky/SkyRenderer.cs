using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Sky;

/// <summary>
/// Device layer for the sky (CN3SkyMng::Render + CN3Sky/CN3Cloud::Render): the
/// horizon-glow colour fans and the scrolling cloud dome, drawn camera-centred
/// (view translation zeroed) with the depth buffer and fog disabled and
/// SRCALPHA/INVSRCALPHA blending.
///
/// Slice 9.11a adds the sun/moon billboards and the star field on top, driven
/// by the pure <see cref="DayNightCycle"/> simulation: call
/// <see cref="SetTimeOfDay(float)"/> each frame with the game day fraction to
/// tint the fans (simulated fog colour) and place the sun, moon and stars on
/// their arcs. Draw order matches the C++: sky, stars, moon, sun, clouds.
/// </summary>
public sealed class SkyRenderer : IDisposable
{
    // Placement radius and billboard half-sizes (relative to the camera-centred
    // sky). The sun/moon sit inside the cloud dome; stars are small points.
    private const float BodyRadius = 2.5f;
    private const float SunHalfSize = 0.18f;
    private const float MoonHalfSize = 0.22f;
    private const float StarHalfSize = 0.012f;

    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _frontFan = new VertexPositionColor[4];
    private readonly VertexPositionColor[] _bottomFan = new VertexPositionColor[4];
    private readonly short[] _fanIndices = [0, 1, 2, 0, 2, 3];

    private readonly VertexPositionColorTexture[] _cloud = new VertexPositionColorTexture[SkyGeometry.CloudVertexCount];
    private readonly Texture2D? _cloudTexture;
    private readonly Texture2D? _sunDisk;
    private readonly Texture2D? _sunGlow;
    private readonly Texture2D? _sunFlare;
    private readonly Texture2D? _moonTexture;
    private readonly SunPartLayout _sunLayout = SkyBodies.SunLayout(SunHalfSize);

    private readonly VertexPositionColorTexture[] _bodyQuad = new VertexPositionColorTexture[4];
    private readonly StarPoint[] _starField;
    private readonly VertexPositionColor[] _starQuads;
    private readonly short[] _starIndices;

    private uint _fogColor = SkyGeometry.DefaultFogColor;
    private Vector2 _cloudScroll;

    // Day-night state (from the pure layer).
    private Vector3 _sunDir = Vector3.Up;
    private Vector3 _moonDir = Vector3.Down;
    private float _starAlpha;
    private UvRect _moonUv = SkyBodies.MoonPhaseUv(0);

    public SkyRenderer(
        GraphicsDevice device,
        Texture2D? cloudTexture = null,
        Texture2D? sunDisk = null,
        Texture2D? sunGlow = null,
        Texture2D? sunFlare = null,
        Texture2D? moonTexture = null,
        int starSeed = 0x5EED)
    {
        _effect = new BasicEffect(device) { VertexColorEnabled = true, LightingEnabled = false };
        _cloudTexture = cloudTexture;
        _sunDisk = sunDisk;
        _sunGlow = sunGlow;
        _sunFlare = sunFlare;
        _moonTexture = moonTexture;

        _starField = DayNightCycle.GenerateStarField(starSeed);
        _starQuads = new VertexPositionColor[_starField.Length * 4];
        var starIdx = new List<short>(_starField.Length * 6);
        for (int i = 0; i < _starField.Length; i++)
            FanIndexer.Append(starIdx, i * 4, 4);
        _starIndices = [.. starIdx];

        RebuildFans();
        RebuildClouds();
    }

    /// <summary>The current fog/sky colour (drives the fan tint). Default is the day colour.</summary>
    public uint FogColor
    {
        get => _fogColor;
        set
        {
            _fogColor = value;
            RebuildFans();
        }
    }

    /// <summary>
    /// Feed the day-night simulation for a game day fraction (0..1): tint the
    /// fans with the simulated fog colour and place the sun, moon and stars.
    /// </summary>
    public void SetTimeOfDay(float dayFraction)
    {
        FogColor = DayNightCycle.FogColor(dayFraction);
        _sunDir = DayNightCycle.SunDirection(dayFraction).ToXna();
        _moonDir = DayNightCycle.MoonDirection(dayFraction).ToXna();
        _starAlpha = DayNightCycle.StarAlpha(dayFraction);
    }

    /// <summary>
    /// Select the moon phase sub-image from the phase strip (CN3Moon::SetMoonPhase):
    /// <paramref name="phaseIndex"/> is <c>month*30 + day</c>, taken mod 24.
    /// </summary>
    public void SetMoonPhase(int phaseIndex) => _moonUv = SkyBodies.MoonPhaseUv(phaseIndex);

    /// <summary>CN3Cloud::Tick — scroll the cloud UVs (the two layers drift apart).</summary>
    public void Tick(float secPerFrame)
    {
        _cloudScroll.X += 0.005f * secPerFrame;
        _cloudScroll.Y += 0.015f * secPerFrame;
        if (_cloudScroll.X > 10f)
            _cloudScroll.X -= 10f;
        if (_cloudScroll.Y > 10f)
            _cloudScroll.Y -= 10f;
        RebuildClouds();
    }

    public void Render(GraphicsDevice device, N3EngineCamera camera)
    {
        // Camera-centred: zero the view translation, keep the projection.
        Matrix view = camera.View.ToXna();
        view.M41 = view.M42 = view.M43 = 0f;
        Matrix projection = camera.Projection.ToXna();
        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = SkyGeometry.CameraYaw(camera.Eye, camera.At);

        DepthStencilState prevDepth = device.DepthStencilState;
        BlendState prevBlend = device.BlendState;
        device.DepthStencilState = DepthStencilState.None; // Z off
        device.BlendState = BlendState.AlphaBlend;         // SRCALPHA / INVSRCALPHA
        device.RasterizerState = RasterizerState.CullNone;

        // Colour fans (untextured).
        _effect.TextureEnabled = false;
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _bottomFan, 0, 4, _fanIndices, 0, 2);
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _frontFan, 0, 4, _fanIndices, 0, 2);
        }

        // Stars, moon, sun sit in world space (not yawed with the fans) and face
        // the camera; the billboard basis comes from the zeroed-translation view.
        Vector3 right = new(view.M11, view.M21, view.M31);
        Vector3 up = new(view.M12, view.M22, view.M32);
        _effect.World = Matrix.Identity;

        RenderStars(device, right, up);
        RenderBody(device, _moonDir, MoonHalfSize, _moonTexture, Color.White, BlendState.AlphaBlend, right, up, _moonUv);
        RenderSun(device, right, up);

        // Cloud dome (textured, modulated by vertex colour).
        _effect.World = SkyGeometry.CameraYaw(camera.Eye, camera.At);
        device.BlendState = BlendState.AlphaBlend;
        if (_cloudTexture != null)
        {
            device.SamplerStates[0] = SamplerState.LinearWrap;
            _effect.TextureEnabled = true;
            _effect.Texture = _cloudTexture;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, _cloud, 0, _cloud.Length,
                    SkyGeometry.CloudIndices, 0, SkyGeometry.CloudIndices.Length / 3);
            }
        }

        device.DepthStencilState = prevDepth;
        device.BlendState = prevBlend;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private void RenderStars(GraphicsDevice device, Vector3 right, Vector3 up)
    {
        if (_starAlpha <= 0f)
            return;

        // Star count is animated by the fade fraction: draw the first N stars
        // (their base alphas already descend, matching CN3Star::Init).
        int visible = (int)(_starField.Length * _starAlpha);
        if (visible <= 0)
            return;

        Vector3 offR = right * StarHalfSize;
        Vector3 offU = up * StarHalfSize;
        for (int i = 0; i < visible; i++)
        {
            Vector3 c = _starField[i].Position.ToXna();
            var color = new Color((byte)255, (byte)255, (byte)255, _starField[i].BaseAlpha);
            int v = i * 4;
            _starQuads[v + 0] = new VertexPositionColor(c - offR + offU, color);
            _starQuads[v + 1] = new VertexPositionColor(c + offR + offU, color);
            _starQuads[v + 2] = new VertexPositionColor(c + offR - offU, color);
            _starQuads[v + 3] = new VertexPositionColor(c - offR - offU, color);
        }

        device.BlendState = BlendState.AlphaBlend;
        _effect.TextureEnabled = false;
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _starQuads, 0, visible * 4,
                _starIndices, 0, visible * 2);
        }
    }

    // CN3Sun::Render — the sun is three concentric additive billboards
    // (disk + glow + flare, ONE/ONE blend). Their half-sizes keep the C++
    // 0.1/0.25/0.13 delta proportions (SkyBodies.SunLayout). Each part is
    // null-safe: an absent texture falls back to the flat additive quad so the
    // disk is still visible.
    private void RenderSun(GraphicsDevice device, Vector3 right, Vector3 up)
    {
        if (_sunDir.Y <= 0f)
            return;

        RenderBody(device, _sunDir, _sunLayout.DiskHalfSize, _sunDisk, Color.White, BlendState.Additive, right, up, FullUv);
        RenderBody(device, _sunDir, _sunLayout.GlowHalfSize, _sunGlow, Color.White, BlendState.Additive, right, up, FullUv);
        RenderBody(device, _sunDir, _sunLayout.FlareHalfSize, _sunFlare, Color.White, BlendState.Additive, right, up, FullUv);
    }

    private static readonly UvRect FullUv = new(0f, 0f, 1f, 1f);

    private void RenderBody(
        GraphicsDevice device, Vector3 direction, float halfSize, Texture2D? texture,
        Color color, BlendState blend, Vector3 right, Vector3 up, UvRect uv)
    {
        // Only draw a body that is above the horizon (Y > 0), matching the C++
        // 2D-projection clip that discards bodies behind the camera/below.
        if (direction.Y <= 0f)
            return;

        Vector3 center = direction * BodyRadius;
        Vector3 offR = right * halfSize;
        Vector3 offU = up * halfSize;
        _bodyQuad[0] = new VertexPositionColorTexture(center - offR + offU, color, new Vector2(uv.U0, uv.V0));
        _bodyQuad[1] = new VertexPositionColorTexture(center + offR + offU, color, new Vector2(uv.U1, uv.V0));
        _bodyQuad[2] = new VertexPositionColorTexture(center + offR - offU, color, new Vector2(uv.U1, uv.V1));
        _bodyQuad[3] = new VertexPositionColorTexture(center - offR - offU, color, new Vector2(uv.U0, uv.V1));

        device.BlendState = blend;
        if (texture != null)
        {
            device.SamplerStates[0] = SamplerState.LinearClamp;
            _effect.TextureEnabled = true;
            _effect.Texture = texture;
        }
        else
        {
            _effect.TextureEnabled = false;
        }

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _bodyQuad, 0, 4, _fanIndices, 0, 2);
        }
    }

    private void RebuildFans()
    {
        SkyFanVertex[] front = SkyGeometry.BuildFrontFan(_fogColor);
        SkyFanVertex[] bottom = SkyGeometry.BuildBottomFan(_fogColor);
        for (int i = 0; i < 4; i++)
        {
            _frontFan[i] = new VertexPositionColor(front[i].Position, ColorInterop.FromArgb(front[i].Color));
            _bottomFan[i] = new VertexPositionColor(bottom[i].Position, ColorInterop.FromArgb(bottom[i].Color));
        }
    }

    private void RebuildClouds()
    {
        SkyCloudVertex[] dome = SkyGeometry.BuildCloudDome();
        for (int i = 0; i < dome.Length; i++)
        {
            _cloud[i] = new VertexPositionColorTexture(
                dome[i].Position,
                ColorInterop.FromArgb(dome[i].Color),
                dome[i].Uv + _cloudScroll);
        }
    }

    public void Dispose() => _effect.Dispose();
}
