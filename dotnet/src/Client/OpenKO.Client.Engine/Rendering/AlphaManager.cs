using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Interop;

namespace OpenKO.Client.Engine.Rendering;

/// <summary>One deferred alpha-blended draw (__AlphaPrimitive).</summary>
public sealed class AlphaPrimitive
{
    public required VertexPositionNormalTexture[] Vertices { get; init; }

    /// <summary>Null for non-indexed triangle lists.</summary>
    public short[]? Indices { get; init; }

    public required int VertexCount { get; init; }

    public required int PrimitiveCount { get; init; }

    public Texture2D? Texture { get; init; }

    public required System.Numerics.Matrix4x4 World { get; init; }

    public required MaterialPlan Plan { get; init; }

    /// <summary>Squared camera distance at Add time — the sort key.</summary>
    public float Distance { get; set; }
}

/// <summary>
/// Port of <c>CN3AlphaPrimitiveManager</c>: alpha-blended geometry is queued
/// during the frame and drawn after the opaque world, sorted back-to-front by
/// camera distance, with per-primitive fog/cull/zwrite/blend states.
/// </summary>
public sealed class AlphaManager
{
    private readonly List<AlphaPrimitive> _primitives = [];

    public int Count => _primitives.Count;

    public void Add(AlphaPrimitive primitive, System.Numerics.Vector3 cameraEye, System.Numerics.Vector3 worldCenter)
    {
        primitive.Distance = (worldCenter - cameraEye).LengthSquared();
        _primitives.Add(primitive);
    }

    /// <summary>Back-to-front ordering (farthest first) — pure, pinned by tests.</summary>
    /// <summary>
    /// Whether a primitive has drawable geometry. A degenerate part (no vertices, no
    /// triangles, or a VertexCount past its vertex array) is skipped instead of faulting
    /// the draw — the alpha-path counterpart of PMeshInstanceRenderer.Render's early-out.
    /// </summary>
    public static bool IsDrawable(AlphaPrimitive p)
        => p.VertexCount > 0 && p.PrimitiveCount > 0 && p.VertexCount <= p.Vertices.Length
            && (p.Indices == null || p.PrimitiveCount * 3 <= p.Indices.Length);

    public static void SortForRender(List<AlphaPrimitive> primitives)
        => primitives.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));

    public void Render(GraphicsDevice device, BasicEffect effect)
    {
        if (_primitives.Count == 0)
            return;

        SortForRender(_primitives);

        bool fogWasEnabled = effect.FogEnabled;
        bool lightingWasEnabled = effect.LightingEnabled;

        foreach (AlphaPrimitive p in _primitives)
        {
            // Skip degenerate geometry — an empty/zero-vertex part (e.g. a shape LOD
            // that collapsed to nothing) would fault DrawUserIndexedPrimitives with a
            // numVertices-out-of-range. This mirrors the opaque path's early-out
            // (PMeshInstanceRenderer.Render: NumIndices < 3 || vertices.Length == 0).
            if (!IsDrawable(p))
                continue;

            device.BlendState = RenderStateMapper.GetBlendState(p.Plan.SrcBlend, p.Plan.DestBlend);
            device.RasterizerState = p.Plan.CullNone
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;
            device.DepthStencilState = p.Plan.DisableZBuffer
                ? DepthStencilState.None
                : p.Plan.DisableZWrite ? DepthStencilState.DepthRead : DepthStencilState.Default;
            device.SamplerStates[0] = p.Plan.PointSampling
                ? (p.Plan.UvClamp ? SamplerState.PointClamp : SamplerState.PointWrap)
                : (p.Plan.UvClamp ? SamplerState.LinearClamp : SamplerState.LinearWrap);

            effect.World = p.World.ToXna();
            effect.Texture = p.Texture;
            effect.TextureEnabled = p.Texture != null;
            effect.FogEnabled = fogWasEnabled && !p.Plan.DisableFog;
            effect.LightingEnabled = lightingWasEnabled && !p.Plan.NoLighting;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                if (p.Indices != null)
                {
                    device.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList, p.Vertices, 0, p.VertexCount,
                        p.Indices, 0, p.PrimitiveCount);
                }
                else
                {
                    device.DrawUserPrimitives(PrimitiveType.TriangleList, p.Vertices, 0, p.PrimitiveCount);
                }
            }
        }

        // Restore the frame defaults, like the C++ restores its state block.
        device.BlendState = BlendState.Opaque;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.DepthStencilState = DepthStencilState.Default;
        device.SamplerStates[0] = SamplerState.LinearWrap;
        effect.FogEnabled = fogWasEnabled;
        effect.LightingEnabled = lightingWasEnabled;

        _primitives.Clear();
    }

    public void Clear() => _primitives.Clear();
}
