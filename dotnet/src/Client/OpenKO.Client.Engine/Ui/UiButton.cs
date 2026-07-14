using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime button — faithful port of <c>CN3UIButton::MouseProc</c>
/// (Client/N3Base/N3UIButton.cpp), including the normal and check (toggle) state
/// machines and the two-phase CHECKDOWN/CHECKUP transitions. The button posts
/// <see cref="UiMsg.ButtonClick"/> to its parent on release.
/// </summary>
public sealed class UiButton : UiControl
{
    public UiButton(N3UiButton node) : base(node)
    {
        ClickRect = node.ClickRect;
        State = UiState.ButtonNormal;
    }

    /// <summary>Click region (m_rcClick) — Win32 PtInRect semantics; shifts with MoveOffset.</summary>
    public N3UiRect ClickRect { get; private set; }

    public bool IsCheckStyle => (Style & UiStyle.BtnCheck) != 0;

    public bool IsNormalStyle => (Style & UiStyle.BtnNormal) != 0;

    /// <summary>True when a check (toggle) button is latched on — the stable DOWN state.</summary>
    public bool IsChecked => State == UiState.ButtonDown;

    public override bool MoveOffset(int dx, int dy)
    {
        if (!base.MoveOffset(dx, dy))
            return false;
        ClickRect = UiRectMath.Offset(ClickRect, dx, dy);
        return true;
    }

    public override UiMouseProc MouseProc(UiMouse flags, UiPoint cur, UiPoint old, UiTooltipControl? tooltip = null)
    {
        var ret = UiMouseProc.None;
        if (!Visible)
            return ret;

        if (!IsIn(cur.X, cur.Y))
        {
            if (!IsIn(old.X, old.Y))
                return ret;
            ret |= UiMouseProc.PrevInRegion;

            if (State == UiState.ButtonDisable)
                return ret;
            ResetToRestState();
            return ret;
        }

        ret |= UiMouseProc.InRegion;

        if (State == UiState.ButtonDisable)
            return ret;

        // Outside the click sub-rect: fall back to the rest state.
        if (!UiRectMath.PtInRect(ClickRect, cur.X, cur.Y))
        {
            ResetToRestState();
            return ret;
        }

        if (IsNormalStyle)
        {
            if ((flags & UiMouse.LbClick) != 0)
            {
                State = UiState.ButtonDown;
                return ret | UiMouseProc.DoneSomething;
            }

            if ((flags & UiMouse.LbClicked) != 0)
            {
                if (Parent != null && State == UiState.ButtonDown)
                {
                    State = UiState.ButtonOn;
                    Parent.ReceiveMessage(this, UiMsg.ButtonClick);
                }

                return ret | UiMouseProc.DoneSomething;
            }

            if (State == UiState.ButtonNormal)
            {
                // Hover highlight — deliberately NOT DoneSomething (C++ note: prevents
                // stale state when the pointer skips quickly between buttons).
                State = UiState.ButtonOn;
                return ret | base.MouseProc(flags, cur, old, tooltip);
            }
        }
        else if (IsCheckStyle)
        {
            if ((flags & UiMouse.LbClick) != 0)
            {
                if (State is UiState.ButtonNormal or UiState.ButtonOn)
                {
                    State = UiState.ButtonDown2CheckDown;
                    return ret | UiMouseProc.DoneSomething;
                }

                if (State == UiState.ButtonDown)
                {
                    State = UiState.ButtonDown2CheckUp;
                    return ret | UiMouseProc.DoneSomething;
                }
            }
            else if ((flags & UiMouse.LbClicked) != 0)
            {
                if (State == UiState.ButtonDown2CheckDown)
                {
                    State = UiState.ButtonDown;
                    Parent?.ReceiveMessage(this, UiMsg.ButtonClick);
                    return ret | UiMouseProc.DoneSomething;
                }

                if (State == UiState.ButtonDown2CheckUp)
                {
                    State = UiState.ButtonOn;
                    Parent?.ReceiveMessage(this, UiMsg.ButtonClick);
                    return ret | UiMouseProc.DoneSomething;
                }
            }
            else if (State == UiState.ButtonNormal)
            {
                State = UiState.ButtonOn;
                return ret | base.MouseProc(flags, cur, old, tooltip);
            }
        }

        return ret | base.MouseProc(flags, cur, old, tooltip);
    }

    /// <summary>Return to the rest state when the pointer leaves the region/click rect.</summary>
    private void ResetToRestState()
    {
        if (IsNormalStyle)
        {
            State = UiState.ButtonNormal;
        }
        else if (IsCheckStyle)
        {
            if (State == UiState.ButtonDown2CheckUp)
                State = UiState.ButtonDown;
            else if (State is UiState.ButtonDown2CheckDown or UiState.ButtonOn)
                State = UiState.ButtonNormal;
        }
    }
}
