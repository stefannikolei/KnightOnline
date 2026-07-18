using System.Numerics;
using OpenKO.Client.Engine.Ui;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Stage-10.6 pins: the pure minimap layout math (port of CUIStateBar::TickMiniMap /
/// Render). A square 1000×1000 world onto a 100×100 px map rect at the top-left origin.
/// </summary>
public class MinimapLayoutTests
{
    private const float MapSize = 1000f;
    private const int L = 0, T = 0, R = 100, B = 100;

    private static void Approx(float expected, float actual, float eps = 1e-3f) =>
        Assert.True(MathF.Abs(expected - actual) <= eps, $"expected {expected}, got {actual}");

    [Fact]
    public void Zoom_ClampsToOneToSix()
    {
        Assert.Equal(6f, MinimapLayout.ZoomIn(6f));      // 6*1.1 -> clamp 6
        Assert.Equal(1f, MinimapLayout.ZoomOut(1f));     // 1*0.9 -> clamp 1
        Approx(5.4f, MinimapLayout.ZoomOut(6f));         // 6*0.9
        Approx(5.5f, MinimapLayout.ZoomIn(5f));          // 5*1.1
    }

    [Fact]
    public void CentredPlayer_SymmetricVFlippedUv()
    {
        // Player at the map centre with no clamping: the view stays at (500,500).
        Vector2 view = MinimapLayout.ClampView(500f, 500f, MapSize, MapSize, 6f, L, T, R, B);
        Approx(500f, view.X);
        Approx(500f, view.Y);

        MinimapUv uv = MinimapLayout.ComputeUv(view, MapSize, MapSize, 6f);
        // fOffset = 0.5/6 = 0.08333, centred at 0.5 → [0.41667, 0.58333]; V flipped.
        Approx(0.41667f, uv.U0);
        Approx(0.58333f, uv.U1);
        Approx(0.41667f, uv.V0); // 1 - 0.58333
        Approx(0.58333f, uv.V1); // 1 - 0.41667
    }

    [Fact]
    public void EdgeClamp_KeepsWindowInsideTexture()
    {
        // Top-left corner: view is pushed inward to half a window (50 / factorX, factorX=0.6).
        Vector2 lo = MinimapLayout.ClampView(0f, 0f, MapSize, MapSize, 6f, L, T, R, B);
        Approx(83.333f, lo.X);
        Approx(83.333f, lo.Y);

        // Bottom-right corner: view pulled back to (zoom*w - w2)/factorX = 550/0.6.
        Vector2 hi = MinimapLayout.ClampView(1000f, 1000f, MapSize, MapSize, 6f, L, T, R, B);
        Approx(916.667f, hi.X);
        Approx(916.667f, hi.Y);
        Assert.True(hi.X < 1000f && lo.X > 0f);
    }

    [Fact]
    public void DotScreen_CentreVisible_FarCulled()
    {
        Vector2 view = MinimapLayout.ClampView(500f, 500f, MapSize, MapSize, 6f, L, T, R, B);

        // A dot at the player position lands on the map centre (50,50).
        bool onCentre = MinimapLayout.TryDotScreen(
            view, MapSize, MapSize, 6f, L, T, R, B, new Vector3(500f, 0f, 500f), out Vector2 c);
        Assert.True(onCentre);
        Approx(50f, c.X);
        Approx(50f, c.Y);

        // A slightly offset dot is still inside (62,62).
        bool inside = MinimapLayout.TryDotScreen(
            view, MapSize, MapSize, 6f, L, T, R, B, new Vector3(520f, 0f, 480f), out Vector2 p);
        Assert.True(inside);
        Approx(62f, p.X);
        Approx(62f, p.Y);

        // A far dot projects off the rect and is culled.
        bool far = MinimapLayout.TryDotScreen(
            view, MapSize, MapSize, 6f, L, T, R, B, new Vector3(500f, 0f, 900f), out _);
        Assert.False(far);
    }

    [Fact]
    public void Arrow_RotatesWithYaw()
    {
        Vector2 view = MinimapLayout.ClampView(500f, 500f, MapSize, MapSize, 6f, L, T, R, B);
        const float fH = 100f / 30f; // (bottom-top)/30

        // Yaw 0: the tip (local (0,-fH)) points up (smaller screen y) at the anchor (50,50).
        Vector2[] a0 = MinimapLayout.ArrowTriangles(view, 500f, 500f, 0f, MapSize, MapSize, 6f, L, T, R, B);
        Assert.Equal(6, a0.Length);
        Approx(50f, a0[0].X);
        Approx(50f - fH, a0[0].Y);

        // Yaw +π/2 rotates the tip to +x (points right): (50+fH, 50).
        Vector2[] a90 = MinimapLayout.ArrowTriangles(view, 500f, 500f, MathF.PI / 2f, MapSize, MapSize, 6f, L, T, R, B);
        Approx(50f + fH, a90[0].X);
        Approx(50f, a90[0].Y);
    }
}
