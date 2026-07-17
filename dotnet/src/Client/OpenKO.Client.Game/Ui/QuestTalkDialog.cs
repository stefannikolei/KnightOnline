using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the NPC TALK window — port of <c>CUIQuestTalk</c> (Client/WarFare/UIQuestTalk.cpp).
/// The shipped <c>*_questtalk_*.uif</c> carries <c>Text_Talk</c> (the page text), <c>btn_Ok_center</c>
/// (OK/advance), <c>btn_close</c> and a <c>scroll</c> bar. <see cref="Open"/> resolves each talk id to a
/// page via <see cref="TextResolver"/> and shows page 0; the OK button advances the page and, past the
/// last one, resets to 0 and hides (<c>CUIQuestTalk::ReceiveMessage</c>); <c>btn_close</c> hides. The
/// window is pushed open by the WIZ_NPC_SAY (0x56) reply
/// (<see cref="InGameState.QuestTalkReceived"/>). Pure/headless.
///
/// As in <see cref="QuestMenuDialog"/>, the <c>__TABLE_QUEST_TALK</c> lookup is injected via
/// <see cref="TextResolver"/> so the controller stays asset-free; a null resolver yields
/// <see cref="string.Empty"/>.
/// </summary>
public sealed class QuestTalkDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiStringControl? _textTalk;
    private readonly UiButton? _btnOk;
    private readonly UiButton? _btnClose;

    private readonly List<string> _pages = [];
    private int _curTalk;

    public QuestTalkDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        _textTalk = root.GetChildById<UiStringControl>("Text_Talk");
        _btnOk = root.GetChildById<UiButton>("btn_Ok_center");
        _btnClose = root.GetChildById<UiButton>("btn_close");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The zero-based page currently shown.</summary>
    public int CurrentPage => _curTalk;

    /// <summary>The number of talk pages from the last <see cref="Open"/>.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Resolves a quest-talk text id to its string (the <c>.tbl</c> lookup). Null → empty.</summary>
    public Func<uint, string>? TextResolver { get; set; }

    /// <summary>Wire the WIZ_NPC_SAY reply that pushes the talk window open.</summary>
    public void Bind(InGameState inGame) => inGame.QuestTalkReceived += Open;

    /// <summary>
    /// <c>CUIQuestTalk::Open</c> — reset to page 0, resolve every talk id to a page and show page 0.
    /// Note the C++ context field is unused here: <see cref="GameContext"/> is retained only for the
    /// symmetry of the dialog pattern (no reply is sent from this window).
    /// </summary>
    public void Open(QuestTalkData data)
    {
        _ = _context; // parity with the sibling dialogs (no reply on this window)
        _curTalk = 0;
        _pages.Clear();
        foreach (uint id in data.TalkIds)
            _pages.Add(TextResolver?.Invoke(id) ?? string.Empty);

        ShowPage(0);
        _root.SetVisible(true);
    }

    /// <summary>CUIQuestTalk OK button — advance a page; past the last, reset and hide.</summary>
    public void Advance()
    {
        _curTalk++;
        if (_curTalk >= _pages.Count)
        {
            _curTalk = 0;
            _root.SetVisible(false);
        }
        else
        {
            ShowPage(_curTalk);
        }
    }

    private void ShowPage(int page)
    {
        if (_textTalk != null)
            _textTalk.Text = page >= 0 && page < _pages.Count ? _pages[page] : string.Empty;
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) == 0)
            return;

        if (ReferenceEquals(sender, _btnOk))
            Advance();
        else if (ReferenceEquals(sender, _btnClose))
            _root.SetVisible(false);
    }
}
