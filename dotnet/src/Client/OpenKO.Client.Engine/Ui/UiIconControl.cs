using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime item/skill icon — faithful port of <c>CN3UIIcon::MouseProc</c>
/// (Client/WarFare/N3UIIcon.cpp). The icon hit-tests the cursor against its move rect
/// (<c>GetMoveRect</c> = <see cref="UiControl.Movable"/>) and posts the <c>UIMSG_ICON_*</c>
/// messages to its <see cref="UiControl.Parent"/> (the icon-manager window), which bubbles
/// them to the dialog controller. The parent's <see cref="UiState.IconMoving"/> latch is the
/// press/release handshake; while the shared <see cref="IconDragState.WaitFromServer"/> lock
/// is set the icon freezes. Hover toggles a runtime <see cref="Highlight"/> (UISTYLE_ICON_HIGHLIGHT)
/// whenever the parent window is idle.
///
/// Pure (no GraphicsDevice) so it is headless-testable; the device renderer reads
/// <see cref="Highlight"/> / <see cref="IconTexture"/> to draw.
/// </summary>
public sealed class UiIconControl : UiControl
{
    public UiIconControl(N3UiIcon node) : base(node)
    {
        IconTexture = node.TexFileName;
    }

    /// <summary>
    /// Shared drag-state consulted for the <see cref="IconDragState.WaitFromServer"/> guard.
    /// <see cref="UiManager"/> assigns its single instance to every icon it builds.
    /// </summary>
    public IconDragState? DragState { get; set; }

    /// <summary>Opaque item/skill payload the dialog attaches (the __IconItemSkill analog).</summary>
    public object? Payload { get; set; }

    /// <summary>Item or skill id this icon represents (0 = none).</summary>
    public int ItemSkillId { get; set; }

    /// <summary>Icon texture name (seeded from the node; the dialog may retarget it).</summary>
    public string IconTexture { get; set; }

    /// <summary>Runtime UISTYLE_ICON_HIGHLIGHT bit — hover highlight while the window is idle.</summary>
    public bool Highlight { get; private set; }

    /// <summary>CN3UIIcon::GetMoveRect — the icon's clickable/draggable rect.</summary>
    public N3UiRect MoveRect => Movable;

    /// <summary>Place the icon at an absolute rect (slot placement); sets region and move rect.</summary>
    public void SetIconRegion(in N3UiRect rect)
    {
        Region = rect;
        Movable = rect;
    }

    /// <summary>Snap the icon so it is centred on the cursor (drag-follow; moves children too).</summary>
    public void MoveToCursor(UiPoint cur) => SetPos(cur.X - Width / 2, cur.Y - Height / 2);

    public override UiMouseProc MouseProc(UiMouse flags, UiPoint cur, UiPoint old, UiTooltipControl? tooltip = null)
    {
        var ret = UiMouseProc.None;
        if (!Visible)
            return ret;

        UiState parentState = Parent?.State ?? UiState.CommonNone;

        // Clear the highlight while the window is idle or a drag is in progress.
        if (parentState is UiState.CommonNone or UiState.IconMoving)
            Highlight = false;

        // Global input lock while a move is pending on the server.
        if (DragState is { WaitFromServer: true })
            return ret;

        // Hover highlight (tested against the full region, only while the window is idle).
        if (UiRectMath.PtInRect(Region, cur.X, cur.Y) && parentState == UiState.CommonNone)
            Highlight = true;

        // Interaction is hit-tested against the move rect.
        if (!UiRectMath.PtInRect(Movable, cur.X, cur.Y))
        {
            ret |= base.MouseProc(flags, cur, old, tooltip);
            return ret;
        }

        // Left press (not while right held) — pick the icon up.
        if ((flags & UiMouse.LbClick) != 0 && (flags & UiMouse.RbDown) == 0)
        {
            if (Parent != null)
            {
                Parent.State = UiState.IconMoving;
                Parent.ReceiveMessage(this, UiMsg.IconDownFirst);
            }

            return ret | UiMouseProc.DoneSomething;
        }

        // Left release (not while right held) — drop, if we were the one moving.
        if ((flags & UiMouse.LbClicked) != 0 && (flags & UiMouse.RbDown) == 0)
        {
            if (Parent != null && Parent.State == UiState.IconMoving)
            {
                Parent.State = UiState.CommonNone;
                Parent.ReceiveMessage(this, UiMsg.IconUp);
                return ret | UiMouseProc.DoneSomething;
            }
        }

        // Right press (not while left held).
        if ((flags & UiMouse.RbClick) != 0 && (flags & UiMouse.LbDown) == 0)
        {
            Parent?.ReceiveMessage(this, UiMsg.IconRDownFirst);
            return ret | UiMouseProc.DoneSomething;
        }

        // Right release (not while left held).
        if ((flags & UiMouse.RbClicked) != 0 && (flags & UiMouse.LbDown) == 0)
        {
            Parent?.ReceiveMessage(this, UiMsg.IconRUp);
            return ret | UiMouseProc.DoneSomething;
        }

        // Left held.
        if ((flags & UiMouse.LbDown) != 0)
        {
            Parent?.ReceiveMessage(this, UiMsg.IconDown);
            return ret | UiMouseProc.DoneSomething;
        }

        // Left double-click.
        if ((flags & UiMouse.LbDblClk) != 0)
        {
            Parent?.ReceiveMessage(this, UiMsg.IconDblClk);
            return ret | UiMouseProc.DoneSomething;
        }

        // Right double-click.
        if ((flags & UiMouse.RbDblClk) != 0)
        {
            Parent?.ReceiveMessage(this, UiMsg.IconRDblClk);
            return ret | UiMouseProc.DoneSomething;
        }

        ret |= base.MouseProc(flags, cur, old, tooltip);
        return ret;
    }
}
