using System.Globalization;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the party-recruitment board — port of <c>CUIPartyBBS</c>
/// (Client/WarFare/UIPartyBBS.cpp). The board .uif is flagged "outdated" upstream (the whole
/// <c>Load</c> component-wiring is <c>#if 0</c>'d out), so the ids here follow the names the C++
/// still references: <c>List_Infos</c> (the seeker rows), <c>string_page</c>, the pager
/// (<c>btn_page_up</c>/<c>btn_page_down</c>/<c>btn_refresh</c>), the recruit toggle
/// (<c>btn_add</c> register / <c>btn_delete</c> cancel), <c>btn_whisper</c>, <c>btn_Party</c> and
/// <c>btn_exit</c>. The list is filled from the WIZ_PARTY_BBS reply
/// (<see cref="InGameState.PartyBbsReceived"/>); paging clamps to <c>ceil(total / 23)</c>.
/// Whisper/party act on the selected row. Pure/headless.
/// </summary>
public sealed class PartyBbsDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiListControl? _list;
    private readonly UiStringControl? _page;

    private IReadOnlyList<PartyBbsEntry> _rows = [];
    private short _curPage;
    private int _maxPage;
    private bool _processing;

    public PartyBbsDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        _list = root.GetChildById<UiListControl>("List_Infos");
        _page = root.GetChildById<UiStringControl>("string_page");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The seekers currently listed (this page).</summary>
    public IReadOnlyList<PartyBbsEntry> Rows => _rows;

    /// <summary>The current page index (0-based).</summary>
    public short CurrentPage => _curPage;

    /// <summary>ceil(total / 23) — the number of pages (CUIPartyBBS::MsgRecv_RefreshData).</summary>
    public int MaxPage => _maxPage;

    /// <summary>Wire the WIZ_PARTY_BBS reply.</summary>
    public void Bind(InGameState inGame) => inGame.PartyBbsReceived += OnBbsPage;

    /// <summary>Show the board and request its first page (btn open / party-window entry).</summary>
    public void Open()
    {
        _curPage = 0;
        _root.SetVisible(true);
        RequestPage(0);
    }

    public void Close()
    {
        _curPage = 0;
        _root.SetVisible(false);
    }

    /// <summary>CUIPartyBBS::MsgSend_RefreshData — request one page (guarded by the reply latch).</summary>
    public void RequestPage(short page)
    {
        if (_processing)
            return;
        _processing = true;
        _context.Client.Send(PartyBbsProtocol.BuildRequestPage(page));
    }

    /// <summary>CUIPartyBBS::MsgSend_Register — flag myself recruiting.</summary>
    public void Register()
    {
        if (_processing)
            return;
        _processing = true;
        _context.Client.Send(PartyBbsProtocol.BuildRegister());
    }

    /// <summary>CUIPartyBBS::MsgSend_RegisterCancel — clear the recruiting flag.</summary>
    public void Cancel()
    {
        if (_processing)
            return;
        _processing = true;
        _context.Client.Send(PartyBbsProtocol.BuildCancel());
    }

    /// <summary>CUIPartyBBS::MsgRecv_RefreshData — populate the page and pager from a reply.</summary>
    public void OnBbsPage(PartyBbsPage reply)
    {
        _processing = false;
        if (!reply.Ok)
            return;

        if (!_root.Visible)
            _root.SetVisible(true);

        _rows = reply.Rows;
        _curPage = reply.Page;
        _maxPage = reply.Total / PartyBbsProtocol.RowsPerPage;
        if (reply.Total % PartyBbsProtocol.RowsPerPage > 0)
            _maxPage++;

        RefreshPage();
    }

    private void RefreshPage()
    {
        if (_page != null)
            _page.Text = (_curPage + 1).ToString(CultureInfo.InvariantCulture);

        if (_list == null)
            return;

        _list.ResetContent();
        foreach (PartyBbsEntry row in _rows)
            _list.AddString(row.Name);

        if (_rows.Count > 0)
            _list.SetCurSel(0);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        switch (sender.Id.ToLowerInvariant())
        {
            case "btn_refresh":
                RequestPage(_curPage);
                break;
            case "btn_page_up":
                if (_curPage - 1 >= 0)
                    RequestPage((short)(_curPage - 1));
                break;
            case "btn_page_down":
                if (_curPage + 1 < _maxPage)
                    RequestPage((short)(_curPage + 1));
                break;
            case "btn_add":
                Register();
                break;
            case "btn_delete":
                Cancel();
                break;
            case "btn_whisper":
                Whisper();
                break;
            case "btn_party":
                InviteParty();
                break;
            case "btn_exit":
            case "btn_close":
                Close();
                break;
        }
    }

    private PartyBbsEntry? Selected()
    {
        int sel = _list?.CurSel ?? -1;
        return sel >= 0 && sel < _rows.Count ? _rows[sel] : null;
    }

    /// <summary>CUIPartyBBS::RequestWhisper — pick the selected seeker as the 1:1 chat target.</summary>
    private void Whisper()
    {
        if (Selected() is not { Name.Length: > 0 } row)
            return;
        if (WorldProtocol.BuildChatTarget(row.Name) is { } packet)
            _context.Client.Send(packet);
    }

    /// <summary>CUIPartyBBS::RequestParty — invite the selected seeker into a party.</summary>
    private void InviteParty()
    {
        if (Selected() is not { Name.Length: > 0 } row)
            return;
        _context.Client.Send(PartyProtocol.BuildCreate(row.Name));
    }
}
