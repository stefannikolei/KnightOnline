namespace OpenKO.Client.Engine.Rendering;

/// <summary>
/// MonoGame's DualTextureEffect pixel shader (DualTextureEffect.fx, v3.8.4)
/// computes `color.rgb *= 2; color *= overlay * diffuse` — i.e. RGB is
/// Modulate2X (tex0·tex1·diffuse·2) while alpha stays 1× (a0·a1·aDiffuse).
/// The KO fixed-function pipeline uses plain MODULATE on both stages, so the
/// engine emulates it by halving the effect's DiffuseColor RGB (alpha
/// untouched). Verified against the 3.8.4 shader source.
/// </summary>
public static class DualTextureCompensation
{
    /// <summary>Multiply DiffuseColor RGB by this to turn Modulate2X into MODULATE.</summary>
    public const float DiffuseRgbScale = 0.5f;

    /// <summary>
    /// The resulting RGB for one channel — used by tests to pin that the
    /// compensated pipeline equals plain double modulation.
    /// </summary>
    public static float CompensatedRgb(float tex0, float tex1, float diffuse)
        => tex0 * 2f * tex1 * (diffuse * DiffuseRgbScale);
}
