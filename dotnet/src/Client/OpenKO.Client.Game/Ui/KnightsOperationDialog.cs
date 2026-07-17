using System.Globalization;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the clan browse/create/join window — port of <c>CUIKnightsOperation</c>
/// (Client/WarFare/UIKnightsOperation.cpp). <c>List_Knights</c> shows the clan roster paged
/// with <c>btn_up</c>/<c>btn_down</c>; <c>Btn_Create</c> opens the name popup, <c>Btn_Join</c>
/// joins the selected clan, <c>Btn_Withdraw</c>/<c>Btn_Destroy</c> confirm (MB_YESNO) then
/// leave/disband. The list is populated from the AllListReq broadcast routed through
/// <see cref="InGameState.KnightsReceived"/>.
/// </summary>
public sealed class KnightsOperationDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly MessageBoxDialog? _messageBox;
    private readonly UiListControl? _list;
    private short _page;

    private IReadOnlyList<KnightsProtocol.ClanListRow> _rows = [];

    public KnightsOperationDialog(GameContext context, UiControl root, MessageBoxDialog? messageBox = null)
    {
        _context = context;
        _root = root;
        _messageBox = messageBox;
        _list = root.GetChildById<UiListControl>("List_Knights");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The clan rows currently shown in the list.</summary>
    public IReadOnlyList<KnightsProtocol.ClanListRow> Rows => _rows;

    /// <summary>Raised when Btn_Create is pressed (open the clan-name popup).</summary>
    public event Action? CreateRequested;

    /// <summary>Wire the clan broadcasts.</summary>
    public void Bind(InGameState inGame) => inGame.KnightsReceived += OnKnights;

    /// <summary>CUIKnightsOperation::Open — reset the page + list and show.</summary>
    public void Open()
    {
        _page = 0;
        _rows = [];
        _list?.ResetContent();
        RequestList(_page);
        _root.SetVisible(true);
    }

    public void Close()
    {
        _rows = [];
        _list?.ResetContent();
        _root.SetVisible(false);
    }

    public void Toggle()
    {
        if (_root.Visible)
            Close();
        else
            Open();
    }

    /// <summary>CGameProcMain::MsgRecv_Knights — populate the list from an AllListReq broadcast.</summary>
    public void OnKnights(byte sub, byte[] payload)
    {
        if (sub != KnightsProtocol.AllListReq)
            return;

        KnightsProtocol.ClanList list = KnightsProtocol.ParseClanList(payload);
        _page = list.Page;
        _rows = list.Rows;
        PopulateList();
    }

    /// <summary>CUIKnightsOperation::MsgSend_KnightsJoin — join the selected clan.</summary>
    public byte[]? Join()
    {
        int sel = _list?.CurSel ?? -1;
        if (sel < 0 || sel >= _rows.Count)
            return null;
        byte[] packet = KnightsProtocol.BuildJoin(_rows[sel].Id);
        _context.Client.Send(packet);
        return packet;
    }

    /// <summary>CUIKnightsOperation::MsgSend_KnightsWithdraw.</summary>
    public byte[] SendWithdraw()
    {
        byte[] packet = KnightsProtocol.BuildWithdraw();
        _context.Client.Send(packet);
        return packet;
    }

    /// <summary>CUIKnightsOperation::MsgSend_KnightsDestroy.</summary>
    public byte[] SendDestroy()
    {
        byte[] packet = KnightsProtocol.BuildDestroy();
        _context.Client.Send(packet);
        return packet;
    }

    private void RequestList(short page) => _context.Client.Send(KnightsProtocol.BuildAllListRequest(page));

    private void PopulateList()
    {
        if (_list == null)
            return;

        _list.ResetContent();
        foreach (KnightsProtocol.ClanListRow row in _rows)
        {
            string text = string.Format(
                CultureInfo.InvariantCulture, "{0,-16} {1,-12} {2,4} {3,8}",
                row.Name, row.ChiefName, row.MemberCount, row.Point);
            _list.AddString(text);
        }
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        // Match case-insensitively (the .uif ids are inconsistently cased; the original
        // compares resolved control pointers).
        switch (sender.Id.ToLowerInvariant())
        {
            case "btn_up":
                _page = (short)Math.Max(0, _page - 1);
                RequestList(_page);
                break;

            case "btn_down":
                _page++;
                RequestList(_page);
                break;

            case "btn_close":
                Close();
                break;

            case "btn_create":
                CreateRequested?.Invoke();
                break;

            case "btn_join":
                Join();
                break;

            case "btn_withdraw":
                Confirm("Leave your clan?", () => SendWithdraw());
                break;

            case "btn_destroy":
                Confirm("Disband your clan?", () => SendDestroy());
                break;
        }
    }

    private void Confirm(string message, Action onYes)
    {
        if (_messageBox != null)
        {
            _messageBox.Show(message, string.Empty, MessageBoxStyle.YesNo, r =>
            {
                if (r == MessageBoxResult.Yes)
                    onYes();
            });
        }
        else
        {
            onYes();
        }
    }
}
