using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Device layer for the global weather field (the <c>Render</c> side of
/// <c>CN3GERain</c> / <c>CN3GESnow</c>): rain draws as a coloured line list of
/// tail→head streaks (no texture, SRCALPHA/INVSRCALPHA, Z-read no-write), snow as
/// a textured triangle list of drifting flakes (double-sided, same blend). The
/// world matrix places the field box at the camera XZ, mirroring the C++
/// <c>m_Matrix</c> follow. All the field maths lives in the pure
/// <see cref="WeatherSimulator"/>; this only uploads + draws, so it is not
/// unit-tested (no GPU in CI).
/// </summary>
public sealed class WeatherRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private VertexPositionColor[] _rainVerts = new VertexPositionColor[256];
    private VertexPositionColorTexture[] _snowVerts = new VertexPositionColorTexture[256];

    public WeatherRenderer(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
        };
    }

    /// <summary>The flake texture (misc\Snow.DXT). Null draws nothing for snow.</summary>
    public Texture2D? SnowTexture { get; set; }

    public void Render(WeatherSimulator weather, N3EngineCamera camera)
    {
        if (!weather.Active)
            return;

        // The field box follows the camera in XZ (Y is already camera-relative in the sim).
        System.Numerics.Vector3 eye = camera.Eye;
        _effect.World = Matrix.CreateTranslation(eye.X, 0f, eye.Z);
        _effect.View = camera.View.ToXna();
        _effect.Projection = camera.Projection.ToXna();

        _device.BlendState = BlendState.NonPremultiplied; // SRCALPHA / INVSRCALPHA
        _device.DepthStencilState = DepthStencilState.DepthRead; // weather does not write Z
        _device.RasterizerState = RasterizerState.CullNone;

        if (weather.Type == WeatherType.Rain)
            RenderRain(weather);
        else if (weather.Type == WeatherType.Snow)
            RenderSnow(weather);

        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private void RenderRain(WeatherSimulator weather)
    {
        IReadOnlyList<WeatherRainParticle> rain = weather.RainParticles;
        if (rain.Count == 0)
            return;

        int needed = rain.Count * 2;
        EnsureRain(needed);

        Color tail = ColorInterop.FromArgb(WeatherSimulator.RainTailColor);
        Color head = ColorInterop.FromArgb(WeatherSimulator.RainHeadColor);
        for (int i = 0; i < rain.Count; i++)
        {
            _rainVerts[(i * 2) + 0] = new VertexPositionColor(rain[i].Tail.ToXna(), tail);
            _rainVerts[(i * 2) + 1] = new VertexPositionColor(rain[i].Head.ToXna(), head);
        }

        _effect.TextureEnabled = false;
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.LineList, _rainVerts, 0, rain.Count);
        }
    }

    private void RenderSnow(WeatherSimulator weather)
    {
        IReadOnlyList<WeatherSnowParticle> snow = weather.SnowParticles;
        if (snow.Count == 0 || SnowTexture == null)
            return;

        int needed = snow.Count * 3;
        EnsureSnow(needed);

        var white = Color.White;
        for (int i = 0; i < snow.Count; i++)
        {
            WeatherSnowParticle p = snow[i];
            _snowVerts[(i * 3) + 0] = new VertexPositionColorTexture(p.V1.ToXna(), white, new Vector2(0.5f, 0f));
            _snowVerts[(i * 3) + 1] = new VertexPositionColorTexture(p.V2.ToXna(), white, new Vector2(1f, 1f));
            _snowVerts[(i * 3) + 2] = new VertexPositionColorTexture(p.V3.ToXna(), white, new Vector2(0f, 1f));
        }

        _effect.TextureEnabled = true;
        _effect.Texture = SnowTexture;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, _snowVerts, 0, snow.Count);
        }
    }

    private void EnsureRain(int count)
    {
        if (_rainVerts.Length < count)
            _rainVerts = new VertexPositionColor[Math.Max(count, _rainVerts.Length * 2)];
    }

    private void EnsureSnow(int count)
    {
        if (_snowVerts.Length < count)
            _snowVerts = new VertexPositionColorTexture[Math.Max(count, _snowVerts.Length * 2)];
    }

    public void Dispose() => _effect.Dispose();
}
