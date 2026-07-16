using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime string — port of <c>CN3UIString::MouseProc</c>: posts
/// <see cref="UiMsg.StringLClick"/> on left release and <see cref="UiMsg.StringLDClick"/>
/// on double-click when the cursor is inside (the C++ deliberately does NOT set
/// DoneSomething for these). Used for clickable text rows like the login server list.
/// The mutable <see cref="Text"/> overrides the layout node's static text.
/// </summary>
public sealed class UiStringControl : UiControl
{
    public UiStringControl(N3UiString node) : base(node)
    {
        Text = node.Text;
        ColorArgb = node.Color;
    }

    /// <summary>Runtime text (SetString equivalent); the renderer prefers this.</summary>
    public string Text { get; set; }

    /// <summary>Runtime color (SetColor equivalent).</summary>
    public uint ColorArgb { get; set; }

    public override UiMouseProc MouseProc(UiMouse flags, UiPoint cur, UiPoint old, UiTooltipControl? tooltip = null)
    {
        var ret = UiMouseProc.None;
        if (!Visible)
            return ret;

        if (IsIn(cur.X, cur.Y) && (flags & UiMouse.LbClicked) != 0)
            Parent?.ReceiveMessage(this, UiMsg.StringLClick);

        if (IsIn(cur.X, cur.Y) && (flags & UiMouse.LbDblClk) != 0)
            Parent?.ReceiveMessage(this, UiMsg.StringLDClick);

        return ret | base.MouseProc(flags, cur, old, tooltip);
    }
}
