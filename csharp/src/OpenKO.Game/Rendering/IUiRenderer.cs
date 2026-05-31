using OpenKO.N3;
using OpenKO.Numerics;

namespace OpenKO.Game.Rendering;

/// <summary>
/// The 2D, screen-space drawing surface that <see cref="GameProcedure"/>s use to render their UI.
///
/// The original client draws UI with pre-transformed (RHW) vertices straight in screen pixels; this
/// abstraction keeps that model — every <see cref="Rect"/> is in pixels with (0,0) at the top-left —
/// while hiding the concrete graphics backend behind an interface. <see cref="OpenKO.Client"/>
/// provides the OpenGL implementation; tests provide a recording fake. That keeps procedures (login,
/// character-select, …) fully unit-testable without a GPU.
/// </summary>
public interface IUiRenderer
{
    /// <summary>Logical UI surface width in pixels.</summary>
    int ScreenWidth { get; }

    /// <summary>Logical UI surface height in pixels.</summary>
    int ScreenHeight { get; }

    /// <summary>Begin a UI pass (set up the orthographic projection, enable blending, …).</summary>
    void Begin();

    /// <summary>Fill a screen-space rectangle with a solid colour.</summary>
    void DrawQuad(Rect region, UiColor color);

    /// <summary>
    /// Draw a (sub-region of a) texture into a screen-space rectangle. <paramref name="uv"/> selects
    /// the source region in 0..1 texture coordinates (left/top/right/bottom), matching
    /// <see cref="N3UIImage.UvRect"/>. <paramref name="tint"/> modulates the sampled colour.
    /// </summary>
    void DrawImage(Rect region, N3Texture texture, FloatRect uv, UiColor tint);

    /// <summary>Finish the UI pass (flush batches, restore state).</summary>
    void End();
}
