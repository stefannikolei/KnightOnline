using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the level-based quest guide — port of <c>CUILevelGuide</c>
/// (Client/WarFare/UILevelGuide.cpp). The shipped <c>*_levelguide_*.uif</c> carries
/// <c>edit_level</c> (the search-level field), <c>text_page</c> (the 1-based page label),
/// <c>btn_check</c> (search), <c>btn_up</c> (next page), <c>btn_down</c> (previous page) and
/// <c>btn_cancel</c> (close), plus <see cref="QuestsPerPage"/> rows of
/// <c>text_title{i}</c>/<c>text_guide{i}</c>.
///
/// The quest population reads <c>__TABLE_HELP</c> against the player's level in the original; that
/// table/roster lookup is the host's concern, so paging and search only track the page number and
/// raise <see cref="PageRequested"/> / <see cref="SearchRequested"/> for the host to fill the rows.
/// <c>btn_cancel</c> hides. Pure/headless.
/// </summary>
public sealed class LevelGuideDialog
{
    /// <summary>MAX_QUESTS_PER_PAGE (UILevelGuide.h) — quest rows shown per page.</summary>
    public const int QuestsPerPage = 3;

    private readonly UiControl _root;
    private readonly UiEditControl? _editLevel;
    private readonly UiStringControl? _textPage;
    private readonly UiButton? _btnCheck;
    private readonly UiButton? _btnUp;
    private readonly UiButton? _btnDown;
    private readonly UiButton? _btnCancel;

    public LevelGuideDialog(GameContext context, UiControl root)
    {
        _ = context; // no reply is sent from this window (kept for pattern symmetry)
        _root = root;
        _editLevel = root.GetChildById<UiEditControl>("edit_level");
        _textPage = root.GetChildById<UiStringControl>("text_page");
        _btnCheck = root.GetChildById<UiButton>("btn_check");
        _btnUp = root.GetChildById<UiButton>("btn_up");
        _btnDown = root.GetChildById<UiButton>("btn_down");
        _btnCancel = root.GetChildById<UiButton>("btn_cancel");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The current zero-based page number (CUILevelGuide::m_iPageNo).</summary>
    public int PageNo { get; private set; }

    /// <summary>Raised when the page changes (CUILevelGuide::SetPageNo) so the host can fill the rows.</summary>
    public event Action<int>? PageRequested;

    /// <summary>Raised on <c>btn_check</c> with the parsed search level (0 = empty/invalid).</summary>
    public event Action<int>? SearchRequested;

    /// <summary>Open the guide at page 0 (CUILevelGuide::SetVisible(true) → SetPageNo(0)).</summary>
    public void Open()
    {
        SetPageNo(0);
        _root.SetVisible(true);
    }

    /// <summary>Show/hide the guide.</summary>
    public void Toggle()
    {
        if (_root.Visible)
            _root.SetVisible(false);
        else
            Open();
    }

    /// <summary>CUILevelGuide::SetPageNo — clamp the page, update the label and raise <see cref="PageRequested"/>.</summary>
    public void SetPageNo(int pageNo)
    {
        PageNo = Math.Max(0, pageNo);
        if (_textPage != null)
            _textPage.Text = (PageNo + 1).ToString();
        PageRequested?.Invoke(PageNo);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) == 0)
            return;

        if (ReferenceEquals(sender, _btnCancel))
        {
            _root.SetVisible(false);
        }
        else if (ReferenceEquals(sender, _btnUp))
        {
            SetPageNo(PageNo + 1);
        }
        else if (ReferenceEquals(sender, _btnDown))
        {
            SetPageNo(PageNo - 1);
        }
        else if (ReferenceEquals(sender, _btnCheck))
        {
            int level = int.TryParse(_editLevel?.Text, out int v) ? v : 0;
            SearchRequested?.Invoke(level);
        }
    }
}
