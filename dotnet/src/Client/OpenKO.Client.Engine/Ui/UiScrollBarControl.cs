using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime scrollbar (port of the position model in <c>CN3UIScrollBar</c>). Tracks a
/// clamped [Min..Max] position and posts <see cref="UiMsg.ScrollBarPos"/> to its
/// parent when the position changes. The visual thumb/track children come from the
/// layout node; this slice models the value + wheel/button stepping.
/// </summary>
public sealed class UiScrollBarControl : UiControl
{
    public UiScrollBarControl(N3UiScrollBar node) : base(node)
    {
        State = UiState.ScrollBarNull;
    }

    public int Min { get; private set; }

    public int Max { get; private set; }

    public int Pos { get; private set; }

    public bool IsVertical => (Style & UiStyle.ScrollBarVertical) != 0;

    public void SetRange(int min, int max)
    {
        Min = min;
        Max = Math.Max(min, max);
        SetPos(Pos);
    }

    /// <summary>Set the position (clamped); notifies the parent on change.</summary>
    public bool SetPos(int pos)
    {
        int clamped = Math.Clamp(pos, Min, Max);
        if (clamped == Pos)
            return false;
        Pos = clamped;
        Parent?.ReceiveMessage(this, UiMsg.ScrollBarPos);
        return true;
    }

    public bool Step(int delta) => SetPos(Pos + delta);
}
