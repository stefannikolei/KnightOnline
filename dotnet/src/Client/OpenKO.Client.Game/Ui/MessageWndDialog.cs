using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the system message window — port of <c>CUIMessageWnd</c>
/// (Client/WarFare/UIMessageWnd.cpp). A simpler cousin of <see cref="ChatDialog"/>:
/// the output text (<c>text_message</c>) is a plain string the dialog paints line by
/// line, so this controller keeps an in-memory <see cref="Lines"/> buffer. Resolves the
/// scrollbar and the fold button (<c>btn_off</c>).
/// </summary>
public sealed class MessageWndDialog
{
    /// <summary>MAX_CHAT_LINES analog — cap the scrollback buffer.</summary>
    public const int MaxLines = 500;

    private const uint White = 0xFFFFFFFF;

    private readonly UiControl _root;
    private readonly UiButton? _btnFold;
    private readonly UiScrollBarControl? _scroll;

    private readonly List<ChatLine> _lines = [];

    /// <summary>Raised when the fold button (btn_off) is pressed.</summary>
    public event Action? FoldRequested;

    public UiControl Root => _root;

    /// <summary>The scrollback lines (the renderer paints these into text_message).</summary>
    public IReadOnlyList<ChatLine> Lines => _lines;

    public MessageWndDialog(GameContext context, UiControl root)
    {
        _root = root;
        _btnFold = root.GetChildById<UiButton>("btn_off");
        _scroll = root.GetChildById<UiScrollBarControl>("scroll");
        root.Message += OnMessage;
    }

    /// <summary>CUIMessageWnd::AddMsg — append a colored line to the scrollback.</summary>
    public void AddMsg(string text, uint color = White)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _lines.Add(new ChatLine(text, color));
        while (_lines.Count > MaxLines)
            _lines.RemoveAt(0);
        _scroll?.SetRange(0, Math.Max(0, _lines.Count - 1));
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg == UiMsg.ButtonClick && ReferenceEquals(sender, _btnFold))
            FoldRequested?.Invoke();
    }
}
