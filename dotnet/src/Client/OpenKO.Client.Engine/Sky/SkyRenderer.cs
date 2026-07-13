using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Sky;

/// <summary>
/// Device layer for the sky (CN3SkyMng::Render + CN3Sky/CN3Cloud::Render): the
/// horizon-glow colour fans and the scrolling cloud dome, drawn camera-centred
/// (view translation zeroed) with the depth buffer and fog disabled and
/// SRCALPHA/INVSRCALPHA blending. Sun, moon and stars are deferred (they need
/// the full day-change colour simulation) — documented for stage 6.7.
/// </summary>
public sealed class SkyRenderer : IDisposable
{
    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _frontFan = new VertexPositionColor[4];
    private readonly VertexPositionColor[] _bottomFan = new VertexPositionColor[4];
    private readonly short[] _fanIndices = [0, 1, 2, 0, 2, 3];

    private readonly VertexPositionColorTexture[] _cloud = new VertexPositionColorTexture[SkyGeometry.CloudVertexCount];
    private readonly Texture2D? _cloudTexture;
    private uint _fogColor = SkyGeometry.DefaultFogColor;
    private Vector2 _cloudScroll;

    public SkyRenderer(GraphicsDevice device, Texture2D? cloudTexture = null)
    {
        _effect = new BasicEffect(device) { VertexColorEnabled = true, LightingEnabled = false };
        _cloudTexture = cloudTexture;
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
        _effect.View = view;
        _effect.Projection = camera.Projection.ToXna();
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

        // Cloud dome (textured, modulated by vertex colour).
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
