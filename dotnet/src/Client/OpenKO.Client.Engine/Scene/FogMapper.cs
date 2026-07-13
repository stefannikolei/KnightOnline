namespace OpenKO.Client.Engine.Scene;

/// <summary>
/// The C++ camera uses D3D EXP2 table fog: f = exp(−(d·ρ)²) with
/// ρ = 1/(0.37·farPlane) (CN3Camera::Apply). BasicEffect only supports
/// linear vertex fog, so this maps the EXP2 curve to a linear (start, end)
/// pair via a two-point fit at f≈0.99 (fog onset) and f≈0.02 (fully fogged).
/// The mid-range differs slightly from D3D9 — documented deviation.
/// </summary>
public static class FogMapper
{
    /// <summary>The C++ density rule: ρ = 1/(0.37·farPlane).</summary>
    public static float DensityFromFarPlane(float farPlane) => 1f / (0.37f * farPlane);

    private const float FitHigh = 0.99f; // fog factor at the near fit point
    private const float FitLow = 0.02f;  // fog factor at the far fit point

    /// <summary>
    /// Linear (FogStart, FogEnd) approximating EXP2 fog of the given density.
    /// Linear fog factor is (end − d)/(end − start): 1 at start, 0 at end.
    /// </summary>
    public static (float Start, float End) MapExp2ToLinear(float density)
    {
        // Invert f = exp(−(d·ρ)²)  →  d(f) = sqrt(−ln f)/ρ.
        float dHigh = MathF.Sqrt(-MathF.Log(FitHigh)) / density;
        float dLow = MathF.Sqrt(-MathF.Log(FitLow)) / density;

        // Line through (dHigh, FitHigh) and (dLow, FitLow), extended to f=1/f=0.
        float slope = (FitLow - FitHigh) / (dLow - dHigh);
        float start = dHigh + (1f - FitHigh) / slope;
        float end = dHigh - FitHigh / slope;
        return (start, end);
    }

    /// <summary>Convenience: linear fog window for a camera far plane.</summary>
    public static (float Start, float End) FromFarPlane(float farPlane)
        => MapExp2ToLinear(DensityFromFarPlane(farPlane));

    /// <summary>The exact EXP2 factor (for tests / documentation of the deviation).</summary>
    public static float Exp2Factor(float distance, float density)
        => MathF.Exp(-(distance * density) * (distance * density));
}
