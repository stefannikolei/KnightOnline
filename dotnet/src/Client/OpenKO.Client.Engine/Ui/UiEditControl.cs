using OpenKO.Client.Assets;
using OpenKO.Core.Text;

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

    /// <summary>
    /// Maximum length in CP949 bytes (0 = unlimited) — matches CN3UIEdit::SetMaxString,
    /// which measures the encoded byte length (a Hangul glyph = 2 bytes). Insertion adds
    /// whole characters, so a 2-byte glyph is never split.
    /// </summary>
    public int MaxLength { get; set; }

    public int CaretPos { get; private set; }

    public bool Focused => State == UiState.EditActive;

    public string Text
    {
        get => _text;
        set
        {
            _text = TruncateToMaxBytes(value ?? string.Empty);
            CaretPos = _text.Length;
        }
    }

    private static int Cp949Bytes(string s) => KoEncoding.Cp949.GetByteCount(s);

    private static int Cp949Bytes(char c) => KoEncoding.Cp949.GetByteCount([c]);

    /// <summary>Drop trailing whole characters until the CP949 byte length fits MaxLength.</summary>
    private string TruncateToMaxBytes(string s)
    {
        if (MaxLength <= 0 || Cp949Bytes(s) <= MaxLength)
            return s;

        int bytes = 0;
        int i = 0;
        for (; i < s.Length; i++)
        {
            int cb = Cp949Bytes(s[i]);
            if (bytes + cb > MaxLength)
                break;
            bytes += cb;
        }

        return s[..i];
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
        if (MaxLength > 0 && Cp949Bytes(_text) + Cp949Bytes(c) > MaxLength)
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
