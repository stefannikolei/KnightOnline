using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NumericsVector2 = System.Numerics.Vector2;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// The colored-primitive companion to <see cref="UiQuadBatcher"/>: draws untextured
/// screen-space triangle fans/lists (VertexPositionColor) through a BasicEffect with the
/// same off-center orthographic projection (top-left origin). Used for the minimap player
/// arrow and the skill cooldown pie — the <c>DrawPrimitiveUP(D3DPT_TRIANGLEFAN, …,
/// FVF_TRANSFORMEDCOLOR)</c> replacement. Optionally clips to a scissor rect (the cooldown
/// pie's icon square). Depth off; alpha blend on (SRCALPHA/INVSRCALPHA).
/// </summary>
public sealed class UiPrimitiveBatcher(GraphicsDevice device) : IDisposable
{
    private readonly BasicEffect _effect = new(device)
    {
        VertexColorEnabled = true,
        LightingEnabled = false,
        FogEnabled = false,
        TextureEnabled = false,
        World = Matrix.Identity,
        View = Matrix.Identity,
    };

    private readonly List<VertexPositionColor> _vertices = [];
    private RasterizerState? _scissorState;
    private bool _open;
    private bool _scissor;
    private Rectangle _prevScissor;
    private RasterizerState? _prevRaster;

    public void Begin(Rectangle? scissor = null)
    {
        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            0f, device.Viewport.Width, device.Viewport.Height, 0f, 0f, 1f);
        device.BlendState = BlendState.NonPremultiplied;
        device.DepthStencilState = DepthStencilState.None;

        _scissor = scissor.HasValue;
        if (_scissor)
        {
            _scissorState ??= new RasterizerState
            {
                CullMode = CullMode.None,
                ScissorTestEnable = true,
            };
            _prevRaster = device.RasterizerState;
            _prevScissor = device.ScissorRectangle;
            device.ScissorRectangle = scissor!.Value;
            device.RasterizerState = _scissorState;
        }
        else
        {
            device.RasterizerState = RasterizerState.CullNone;
        }

        _open = true;
    }

    /// <summary>
    /// Append a triangle fan given as centre + arc points (the <see cref="CooldownArc"/> /
    /// arrow output). A fan of N verts emits N-2 triangles (0,i,i+1).
    /// </summary>
    public void FillTriangleFan(IReadOnlyList<NumericsVector2> fan, Color color)
    {
        if (!_open)
            throw new InvalidOperationException("Begin() first");
        if (fan.Count < 3)
            return;

        for (int i = 1; i < fan.Count - 1; i++)
        {
            Add(fan[0], color);
            Add(fan[i], color);
            Add(fan[i + 1], color);
        }
    }

    /// <summary>Append a flat triangle list (3 verts per triangle) — e.g. the two-triangle arrow.</summary>
    public void FillTriangleList(IReadOnlyList<NumericsVector2> tris, Color color)
    {
        if (!_open)
            throw new InvalidOperationException("Begin() first");

        int n = tris.Count - tris.Count % 3;
        for (int i = 0; i < n; i++)
            Add(tris[i], color);
    }

    private void Add(NumericsVector2 p, Color color) =>
        _vertices.Add(new VertexPositionColor(new Vector3(p.X, p.Y, 0f), color));

    public void End()
    {
        Flush();
        if (_scissor)
        {
            device.ScissorRectangle = _prevScissor;
            device.RasterizerState = _prevRaster ?? RasterizerState.CullCounterClockwise;
        }

        _open = false;
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private void Flush()
    {
        if (_vertices.Count < 3)
        {
            _vertices.Clear();
            return;
        }

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(
                PrimitiveType.TriangleList, _vertices.ToArray(), 0, _vertices.Count / 3);
        }

        _vertices.Clear();
    }

    public void Dispose()
    {
        _effect.Dispose();
        _scissorState?.Dispose();
    }
}
