using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>UISTYLE_* bits the renderer needs (N3UIDef.h).</summary>
public static class UiStyles
{
    public const uint ImageAnimate = 0x00010000; // UISTYLE_IMAGE_ANIMATE
}

/// <summary>One textured screen quad in draw order.</summary>
public readonly record struct UiQuadPlan(
    string TexFileName, N3UiRect Screen, N3UiRectF Uv, uint ColorArgb);

/// <summary>One text run in draw order.</summary>
public readonly record struct UiTextPlan(
    string Text, N3UiRect Region, string FontName, uint FontHeight, uint FontFlags, uint ColorArgb);

/// <summary>
/// Pure UI traversal: turns a .uif widget tree into draw-ordered quad/text
/// plans, reproducing the C++ render order — a widget draws itself, then its
/// children in REVERSE list order (tail first, head topmost). Buttons draw
/// only their BS_NORMAL state image (child Reserved == 0); animated images
/// draw only the current frame child (frame 0 here — the browser is static).
/// </summary>
public static class UiRenderer
{
    public const uint OpaqueWhite = 0xFFFFFFFF;

    public static (List<UiQuadPlan> Quads, List<UiTextPlan> Texts) BuildPlans(N3UiBase root)
    {
        var quads = new List<UiQuadPlan>();
        var texts = new List<UiTextPlan>();
        Visit(root, quads, texts);
        return (quads, texts);
    }

    private static void Visit(N3UiBase widget, List<UiQuadPlan> quads, List<UiTextPlan> texts)
    {
        switch (widget)
        {
            case N3UiImage image when (image.Style & UiStyles.ImageAnimate) != 0:
            {
                // Animated: only the current frame child renders (frame 0).
                N3UiBase? frame = image.Children.FirstOrDefault(c => c is N3UiImage);
                if (frame != null)
                    Visit(frame, quads, texts);
                return; // no self-quad, no other children
            }

            case N3UiImage image:
                if (image.TexFileName.Length > 0)
                    quads.Add(new UiQuadPlan(image.TexFileName, image.Region, image.UvRect, OpaqueWhite));
                break;

            case N3UiString str:
                if (str.Text.Length > 0)
                    texts.Add(new UiTextPlan(str.Text, str.Region, str.FontName, str.FontHeight, str.FontFlags, str.Color));
                break;

            case N3UiButton button:
            {
                // CN3UIButton::Render: only the image matching the state
                // (BS_NORMAL = child image with Reserved 0); other child
                // types (strings etc.) render normally.
                N3UiImage? normal = button.Children.OfType<N3UiImage>().FirstOrDefault(i => i.Reserved == 0);
                if (normal != null)
                    Visit(normal, quads, texts);
                foreach (N3UiBase child in Enumerable.Reverse(button.Children))
                {
                    if (child is not N3UiImage)
                        Visit(child, quads, texts);
                }

                return;
            }

            case N3UiList list:
                // Lists draw their rows at runtime; only chrome children here.
                break;
        }

        // Children tail-first (CN3UIBase::Render iterates rbegin→rend).
        for (int i = widget.Children.Count - 1; i >= 0; i--)
            Visit(widget.Children[i], quads, texts);
    }

    /// <summary>
    /// Topmost widget containing the point — the hit-test walks head-first
    /// (the head child draws last, i.e. on top).
    /// </summary>
    public static N3UiBase? HitTest(N3UiBase root, int x, int y)
    {
        foreach (N3UiBase child in root.Children)
        {
            N3UiBase? hit = HitTest(child, x, y);
            if (hit != null)
                return hit;
        }

        return IsIn(root.Region, x, y) ? root : null;
    }

    /// <summary>CN3UIBase::IsIn — the Win32 PtInRect convention (right/bottom exclusive).</summary>
    public static bool IsIn(in N3UiRect rect, int x, int y)
        => x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
}
