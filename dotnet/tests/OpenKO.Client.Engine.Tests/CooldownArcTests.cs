using System.Numerics;
using OpenKO.Client.Engine.Ui;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Stage-10.6 pins: the pure cooldown-pie geometry (port of
/// CUIHotKeyDlg::RenderCooldown). Centre (100,100), radius 20, 64 segments.
/// </summary>
public class CooldownArcTests
{
    private const float Cx = 100f, Cy = 100f, Radius = 20f;

    private static void Approx(float expected, float actual, float eps = 1e-3f) =>
        Assert.True(MathF.Abs(expected - actual) <= eps, $"expected {expected}, got {actual}");

    [Fact]
    public void StartsAt12OClock()
    {
        Vector2[] v = CooldownArc.BuildPie(Cx, Cy, Radius, 0.5f);
        Approx(Cx, v[0].X); // centre vertex
        Approx(Cy, v[0].Y);

        // First arc vertex at −π/2 → straight up (screen y − radius).
        Approx(Cx, v[1].X);
        Approx(Cy - Radius, v[1].Y);
    }

    [Fact]
    public void QuarterSweep_IsClockwiseByCppFormula()
    {
        // progress 0.25 → 16 segments, maxAngle π/2; the sweep runs angle = −π/2 − maxAngle·i/seg,
        // so the endpoint sits at −π (the left point) exactly as the C++ arc.
        Vector2[] v = CooldownArc.BuildPie(Cx, Cy, Radius, 0.25f);
        Assert.Equal(16 + 2, v.Length);       // centre + 17 arc points
        Vector2 last = v[^1];
        Approx(Cx - Radius, last.X);
        Approx(Cy, last.Y);
    }

    [Fact]
    public void FullCircle_ClosesBackToStart()
    {
        Vector2[] v = CooldownArc.BuildPie(Cx, Cy, Radius, 1f);
        Assert.Equal(64 + 2, v.Length);       // centre + 65 arc points
        // The last arc vertex wraps a full 2π back to the 12 o'clock start.
        Approx(v[1].X, v[^1].X, 1e-2f);
        Approx(v[1].Y, v[^1].Y, 1e-2f);
    }

    [Fact]
    public void EmptyWhenOffCooldown()
    {
        Assert.Empty(CooldownArc.BuildPie(Cx, Cy, Radius, 0f));
        Assert.Empty(CooldownArc.BuildPie(Cx, Cy, Radius, -0.5f));
    }

    [Fact]
    public void VertexCount_MatchesSegmentBudget()
    {
        Assert.Equal(32 + 2, CooldownArc.BuildPie(Cx, Cy, Radius, 0.5f).Length);
        // Clamped above 1 → full 64 segments.
        Assert.Equal(64 + 2, CooldownArc.BuildPie(Cx, Cy, Radius, 1.5f).Length);
    }

    [Fact]
    public void ArcPointsLieOnRadius()
    {
        Vector2[] v = CooldownArc.BuildPie(Cx, Cy, Radius, 0.75f);
        for (int i = 1; i < v.Length; i++)
        {
            float d = MathF.Sqrt((v[i].X - Cx) * (v[i].X - Cx) + (v[i].Y - Cy) * (v[i].Y - Cy));
            Approx(Radius, d, 1e-2f);
        }
    }
}
