using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime edit box — the pure text/caret/focus model of <c>CN3UIEdit</c>
/// (Client/N3Base/N3UIEdit.cpp). This slice (9.1) provides the headless-testable
/// editing logic; slice 9.2 feeds it from MonoGame's <c>Window.TextInput</c> and
/// refines CP949-byte max-length + Hangul lead-byte truncation. Click-to-focus is
/// handled here; the manager tracks the single focused edit.
/// </summary>
public sealed class UiEditControl : UiControl
{
    public UiEditControl(N3UiEdit node) : base(node)
    {
        State = UiState.EditUnactive;
    }

    private string _text = string.Empty;

    /// <summary>Raised on Enter (UIMSG_EDIT_RETURN posts to the parent too).</summary>
    public event Action<UiEditControl>? Returned;

    public bool IsPassword => (Style & UiStyle.EditPassword) != 0;

    public bool IsNumberOnly => (Style & UiStyle.EditNumberOnly) != 0;

    /// <summary>Character cap (0 = unlimited). Byte-based CP949 cap is refined in 9.2.</summary>
    public int MaxLength { get; set; }

    public int CaretPos { get; private set; }

    public bool Focused => State == UiState.EditActive;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            if (MaxLength > 0 && _text.Length > MaxLength)
                _text = _text[..MaxLength];
            CaretPos = _text.Length;
        }
    }

    /// <summary>What is drawn (password style masks with '*').</summary>
    public string DisplayText => IsPassword ? new string('*', _text.Length) : _text;

    public void SetFocus()
    {
        State = UiState.EditActive;
        CaretPos = _text.Length;
    }

    public void KillFocus() => State = UiState.EditUnactive;

    public void Clear()
    {
        _text = string.Empty;
        CaretPos = 0;
    }

    /// <summary>Insert a typed character at the caret (respecting number-only / max-length).</summary>
    public bool InsertChar(char c)
    {
        if (char.IsControl(c))
            return false;
        if (IsNumberOnly && !char.IsDigit(c))
            return false;
        if (MaxLength > 0 && _text.Length >= MaxLength)
            return false;

        _text = _text.Insert(CaretPos, c.ToString());
        CaretPos++;
        return true;
    }

    public bool Backspace()
    {
        if (CaretPos <= 0)
            return false;
        _text = _text.Remove(CaretPos - 1, 1);
        CaretPos--;
        return true;
    }

    public bool DeleteForward()
    {
        if (CaretPos >= _text.Length)
            return false;
        _text = _text.Remove(CaretPos, 1);
        return true;
    }

    public void MoveCaret(int delta) => CaretPos = Math.Clamp(CaretPos + delta, 0, _text.Length);

    public void CaretHome() => CaretPos = 0;

    public void CaretEnd() => CaretPos = _text.Length;

    /// <summary>Enter — post UIMSG_EDIT_RETURN to the parent and raise <see cref="Returned"/>.</summary>
    public void SubmitReturn()
    {
        Parent?.ReceiveMessage(this, UiMsg.EditReturn);
        Returned?.Invoke(this);
    }

    public override UiMouseProc MouseProc(UiMouse flags, UiPoint cur, UiPoint old, UiTooltipControl? tooltip = null)
    {
        var ret = UiMouseProc.None;
        if (!Visible)
            return ret;

        // Click-to-focus (the manager clears other edits' focus).
        if (IsIn(cur.X, cur.Y) && (flags & UiMouse.LbClick) != 0)
        {
            SetFocus();
            return ret | UiMouseProc.InRegion | UiMouseProc.DoneSomething;
        }

        return ret | base.MouseProc(flags, cur, old, tooltip);
    }
}
