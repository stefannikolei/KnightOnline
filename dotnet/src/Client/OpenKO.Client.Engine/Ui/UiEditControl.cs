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

    /// <summary>
    /// The composition-aware draw layout for the focused edit — the in-progress IME
    /// string overlaid (underlined) at the caret before it is committed. The C++ host
    /// uses a native EDIT + IMM32 (Client/N3Base/N3UIEdit.cpp), which draws the standard
    /// underlined composition; this reproduces that look for the pure model so the
    /// device renderer can draw it. When there is no live composition the display is the
    /// plain <see cref="DisplayText"/> and the underline span is empty.
    /// </summary>
    /// <param name="text">The display string to draw (committed text with the composition spliced in at the caret).</param>
    /// <param name="caretIndex">The caret glyph index within <paramref name="text"/> (intra-composition while composing).</param>
    /// <param name="underlineStart">The glyph index where the composition underline begins.</param>
    /// <param name="underlineLength">The composition length in glyphs (0 = no underline).</param>
    public readonly record struct CompositionLayout(
        string Text,
        int CaretIndex,
        int UnderlineStart,
        int UnderlineLength)
    {
        /// <summary>True when there is a live composition to underline.</summary>
        public bool HasComposition => UnderlineLength > 0;
    }

    /// <summary>
    /// Splices a live IME composition string into the display at the caret and reports
    /// where to draw the underline + intra-composition caret. The composition is <b>not</b>
    /// committed to <see cref="Text"/> — it is an overlay at <see cref="CaretPos"/>.
    /// <para>
    /// Clause-segment styling (thick vs. thin underlines per IMM clause) needs
    /// <c>SDL_TEXTEDITING_EXT</c> attribute data, which SDL2 does not expose, so this
    /// approximates the whole composition with a single flat underline.
    /// </para>
    /// </summary>
    /// <param name="compositionText">The IME composition string ("" or null when idle).</param>
    /// <param name="compositionCursor">The IME's caret offset within the composition string.</param>
    public CompositionLayout GetCompositionLayout(string? compositionText, int compositionCursor)
    {
        string display = DisplayText;
        int caret = Math.Clamp(CaretPos, 0, display.Length);

        if (string.IsNullOrEmpty(compositionText))
            return new CompositionLayout(display, caret, caret, 0);

        // Overlay the (unmasked) composition at the caret; committed text stays as-is.
        string spliced = display[..caret] + compositionText + display[caret..];
        int cursor = Math.Clamp(compositionCursor, 0, compositionText.Length);
        return new CompositionLayout(spliced, caret + cursor, caret, compositionText.Length);
    }

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

    // KoEncoding.Cp949 already registered the code-pages provider; derive a strict
    // (throwing) clone so unrepresentable input is rejected rather than silently
    // turned into '?'. The original client is CP949/ANSI-only.
    private static readonly System.Text.Encoding Cp949Strict =
        System.Text.Encoding.GetEncoding(KoEncoding.Cp949.CodePage,
            System.Text.EncoderFallback.ExceptionFallback,
            System.Text.DecoderFallback.ExceptionFallback);

    /// <summary>The original client is CP949/ANSI-only — reject what the wire can't carry.</summary>
    private static bool IsCp949Encodable(char c)
    {
        try
        {
            Cp949Strict.GetByteCount([c]);
            return true;
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Insert a typed character at the caret (respecting number-only / max-length).</summary>
    public bool InsertChar(char c)
    {
        if (char.IsControl(c))
            return false;
        if (IsNumberOnly && !char.IsDigit(c))
            return false;
        if (!IsCp949Encodable(c))
            return false;
        if (MaxLength > 0 && Cp949Bytes(_text) + Cp949Bytes(c) > MaxLength)
            return false;

        _text = _text.Insert(CaretPos, c.ToString());
        CaretPos++;
        return true;
    }

    /// <summary>
    /// CN3UIEdit routes its buffer through the child string (m_pBuffOutRef) —
    /// keep the display child in sync every tick.
    /// </summary>
    public override void Tick()
    {
        foreach (UiControl child in Children)
        {
            if (child is UiStringControl str)
            {
                str.Text = DisplayText;
                break;
            }
        }

        base.Tick();
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
