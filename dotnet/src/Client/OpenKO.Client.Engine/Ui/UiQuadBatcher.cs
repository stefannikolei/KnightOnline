using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Interop;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// The RHW-quad replacement: screen-space textured quads through a
/// BasicEffect with an off-center orthographic projection (top-left origin,
/// like D3D screen coordinates). Batches consecutive quads per texture; the
/// D3D9 -0.5px offset is dropped (GL rasterization rules) — documented
/// deviation. Depth is off (UI draws in order).
/// </summary>
public sealed class UiQuadBatcher(GraphicsDevice device) : IDisposable
{
    private readonly BasicEffect _effect = new(device)
    {
        VertexColorEnabled = true,
        LightingEnabled = false,
        FogEnabled = false,
        World = Matrix.Identity,
        View = Matrix.Identity,
    };

    private readonly List<VertexPositionColorTexture> _vertices = [];
    private readonly List<short> _indices = [];
    private Texture2D? _currentTexture;
    private bool _open;

    public int BatchBreaks { get; private set; }

    public void Begin()
    {
        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            0f, device.Viewport.Width, device.Viewport.Height, 0f, 0f, 1f);
        device.BlendState = BlendState.NonPremultiplied; // SRCALPHA/INVSRCALPHA
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = SamplerState.LinearClamp;
        _open = true;
        BatchBreaks = 0;
    }

    /// <summary>Adds one screen quad; flushes when the texture changes.</summary>
    public void Draw(Texture2D? texture, float left, float top, float right, float bottom,
        float u0, float v0, float u1, float v1, Color color)
    {
        if (!_open)
            throw new InvalidOperationException("Begin() first");

        if (!ReferenceEquals(texture, _currentTexture) && _vertices.Count > 0)
        {
            Flush();
            BatchBreaks++;
        }

        _currentTexture = texture;

        int baseVertex = _vertices.Count;
        _vertices.Add(new VertexPositionColorTexture(new Vector3(left, top, 0f), color, new Vector2(u0, v0)));
        _vertices.Add(new VertexPositionColorTexture(new Vector3(right, top, 0f), color, new Vector2(u1, v0)));
        _vertices.Add(new VertexPositionColorTexture(new Vector3(right, bottom, 0f), color, new Vector2(u1, v1)));
        _vertices.Add(new VertexPositionColorTexture(new Vector3(left, bottom, 0f), color, new Vector2(u0, v1)));
        FanIndexer.Append(_indices, baseVertex, 4);
    }

    public void End()
    {
        Flush();
        _open = false;
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private void Flush()
    {
        if (_vertices.Count == 0)
            return;

        _effect.Texture = _currentTexture;
        _effect.TextureEnabled = _currentTexture != null;

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _vertices.ToArray(), 0, _vertices.Count,
                _indices.ToArray(), 0, _indices.Count / 3);
        }

        _vertices.Clear();
        _indices.Clear();
    }

    public void Dispose() => _effect.Dispose();
}
