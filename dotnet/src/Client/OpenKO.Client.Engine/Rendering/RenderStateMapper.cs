using Microsoft.Xna.Framework.Graphics;

namespace OpenKO.Client.Engine.Rendering;

/// <summary>
/// Maps the raw D3D9 render-state values stored in the asset files
/// (__Material.dwSrcBlend/dwDestBlend etc.) to MonoGame equivalents.
/// Value tables from d3d9types.h.
/// </summary>
public static class RenderStateMapper
{
    /// <summary>D3DBLEND (1..11) → MonoGame Blend.</summary>
    public static Blend ToBlend(uint d3dBlend) => d3dBlend switch
    {
        1 => Blend.Zero,
        2 => Blend.One,
        3 => Blend.SourceColor,
        4 => Blend.InverseSourceColor,
        5 => Blend.SourceAlpha,
        6 => Blend.InverseSourceAlpha,
        7 => Blend.DestinationAlpha,
        8 => Blend.InverseDestinationAlpha,
        9 => Blend.DestinationColor,
        10 => Blend.InverseDestinationColor,
        11 => Blend.SourceAlphaSaturation,
        _ => throw new ArgumentOutOfRangeException(nameof(d3dBlend), d3dBlend, "Unknown D3DBLEND value"),
    };

    /// <summary>D3DCMP (1..8) → MonoGame CompareFunction.</summary>
    public static CompareFunction ToCompareFunction(uint d3dCmp) => d3dCmp switch
    {
        1 => CompareFunction.Never,
        2 => CompareFunction.Less,
        3 => CompareFunction.Equal,
        4 => CompareFunction.LessEqual,
        5 => CompareFunction.Greater,
        6 => CompareFunction.NotEqual,
        7 => CompareFunction.GreaterEqual,
        8 => CompareFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(d3dCmp), d3dCmp, "Unknown D3DCMP value"),
    };

    /// <summary>
    /// Builds (and caches) a BlendState for a D3D (src, dest) pair — the only
    /// blend parameters KO materials carry (BLENDOP is always ADD).
    /// </summary>
    public static BlendState GetBlendState(uint d3dSrcBlend, uint d3dDestBlend)
    {
        var key = (d3dSrcBlend, d3dDestBlend);
        lock (BlendCache)
        {
            if (BlendCache.TryGetValue(key, out BlendState? cached))
                return cached;

            var state = new BlendState
            {
                ColorSourceBlend = ToBlend(d3dSrcBlend),
                AlphaSourceBlend = ToBlend(d3dSrcBlend),
                ColorDestinationBlend = ToBlend(d3dDestBlend),
                AlphaDestinationBlend = ToBlend(d3dDestBlend),
                Name = $"KoBlend({d3dSrcBlend},{d3dDestBlend})",
            };
            BlendCache[key] = state;
            return state;
        }
    }

    private static readonly Dictionary<(uint, uint), BlendState> BlendCache = [];
}
