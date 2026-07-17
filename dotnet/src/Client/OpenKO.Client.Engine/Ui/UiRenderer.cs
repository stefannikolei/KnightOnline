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

    /// <summary>Red tint applied to an icon whose item durability is exhausted (UISTYLE_DURABILITY_EXHAUST).</summary>
    public const uint DurabilityExhaustTint = 0xFFFF6060;

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

    // ---- State-aware traversal over the runtime UiControl tree (9.1) --------
    // Unlike the static N3UiBase overload above, this reads live control state:
    // button state image, list rows, and the live (possibly moved) Region.

    public static (List<UiQuadPlan> Quads, List<UiTextPlan> Texts) BuildPlans(UiControl root)
    {
        var quads = new List<UiQuadPlan>();
        var texts = new List<UiTextPlan>();
        VisitControl(root, quads, texts);
        return (quads, texts);
    }

    private static void VisitControl(UiControl c, List<UiQuadPlan> quads, List<UiTextPlan> texts)
    {
        if (!c.Visible)
            return;

        switch (c)
        {
            case UiButton button:
                RenderButton(button, quads, texts);
                return;

            case UiIconControl icon:
                RenderIcon(icon, quads);
                return;

            case UiListControl list:
                RenderSelf(list, quads, texts);
                RenderListRows(list, texts);
                break;

            default:
                RenderSelf(c, quads, texts);
                break;
        }

        // Children tail-first (head drawn last, i.e. on top).
        for (int i = c.Children.Count - 1; i >= 0; i--)
            VisitControl(c.Children[i], quads, texts);
    }

    /// <summary>Emit this control's own quad/text based on its node type, using the live Region.</summary>
    private static void RenderSelf(UiControl c, List<UiQuadPlan> quads, List<UiTextPlan> texts)
    {
        // Runtime string overrides the static layout text (SetString/SetColor).
        if (c is UiStringControl str2 && c.Node is N3UiString strNode)
        {
            if (str2.Text.Length > 0)
                texts.Add(new UiTextPlan(str2.Text, c.Region, strNode.FontName, strNode.FontHeight, strNode.FontFlags, str2.ColorArgb));
            return;
        }

        // Edit box: the child string is the display buffer (m_pBuffOutRef, synced in
        // Tick). Only fall back to drawing directly when the layout has no string child.
        if (c is UiEditControl edit && c.Node is N3UiEdit)
        {
            bool hasStringChild = c.Children.Any(ch => ch is UiStringControl);
            if (!hasStringChild && edit.DisplayText.Length > 0)
                texts.Add(new UiTextPlan(edit.DisplayText, c.Region, string.Empty, 12, 0, OpaqueWhite));
            return;
        }

        switch (c.Node)
        {
            case N3UiImage image when (image.Style & UiStyles.ImageAnimate) != 0:
            {
                // Animated: render only the current frame child (frame 0 for now).
                UiControl? frame = c.Children.FirstOrDefault(ch => ch.Node is N3UiImage);
                if (frame != null)
                    VisitControl(frame, quads, texts);
                break;
            }

            case N3UiImage image:
                if (image.TexFileName.Length > 0)
                    quads.Add(new UiQuadPlan(image.TexFileName, c.Region, image.UvRect, OpaqueWhite));
                break;

            case N3UiString str:
                if (str.Text.Length > 0)
                    texts.Add(new UiTextPlan(str.Text, c.Region, str.FontName, str.FontHeight, str.FontFlags, str.Color));
                break;
        }
    }

    /// <summary>
    /// Draw a runtime item/skill icon (CN3UIIcon::Render). Emits a single quad using the
    /// live <see cref="UiIconControl.IconTexture"/> (retargeted per slot, not the node's
    /// texture), the fixed 45/64 item-icon UV window and the live (possibly dragged) Region.
    /// A durability-exhausted item is drawn with a red tint. Empty (hidden) slots carry no
    /// texture and emit nothing.
    /// </summary>
    private static void RenderIcon(UiIconControl icon, List<UiQuadPlan> quads)
    {
        if (icon.IconTexture.Length == 0)
            return;

        uint color = icon.DurabilityExhausted ? DurabilityExhaustTint : OpaqueWhite;
        quads.Add(new UiQuadPlan(icon.IconTexture, icon.Region, UiIconControl.ItemIconUv, color));
    }

    /// <summary>CN3UIButton::Render — draw the state image, then non-state children tail-first.</summary>
    private static void RenderButton(UiButton button, List<UiQuadPlan> quads, List<UiTextPlan> texts)
    {
        int stateIdx = button.State switch
        {
            UiState.ButtonNormal => 0,          // BS_NORMAL
            UiState.ButtonDown or UiState.ButtonDown2CheckDown or UiState.ButtonDown2CheckUp => 1, // BS_DOWN
            UiState.ButtonOn => 2,              // BS_ON
            UiState.ButtonDisable => 3,         // BS_DISABLE
            _ => 0,
        };

        foreach (UiControl child in button.Children)
        {
            if (child.Node is N3UiImage img && img.Reserved == (uint)stateIdx)
            {
                if (img.TexFileName.Length > 0)
                    quads.Add(new UiQuadPlan(img.TexFileName, child.Region, img.UvRect, OpaqueWhite));
                break;
            }
        }

        // Non-state children (labels etc.) draw tail-first.
        for (int i = button.Children.Count - 1; i >= 0; i--)
        {
            UiControl child = button.Children[i];
            bool isStateImage = child.Node is N3UiImage img2 && img2.Reserved < 4;
            if (!isStateImage)
                VisitControl(child, quads, texts);
        }
    }

    /// <summary>Emit the list's visible rows as text runs (CN3UIList row model).</summary>
    private static void RenderListRows(UiListControl list, List<UiTextPlan> texts)
    {
        if (list.Node is not N3UiList node)
            return;

        int last = Math.Min(list.Rows.Count, list.ScrollTop + list.VisibleRowCount);
        for (int i = list.ScrollTop; i < last; i++)
        {
            int top = list.Region.Top + (i - list.ScrollTop) * list.RowHeight;
            var rowRect = new N3UiRect
            {
                Left = list.Region.Left,
                Top = top,
                Right = list.Region.Right,
                Bottom = top + list.RowHeight,
            };
            texts.Add(new UiTextPlan(list.Rows[i], rowRect, node.FontName, node.FontHeight, 0, node.FontColor));
        }
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
