using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>The NPC event kind (e_NpcEvent, GameDef.h) that <see cref="NpcEventDialog.Open"/> takes.</summary>
public enum NpcEventKind
{
    /// <summary>NPC_EVENT_ITEM_TRADE — a plain vendor (buy/sell); the repair button is hidden.</summary>
    ItemTrade = 0,

    /// <summary>NPC_EVENT_TRADE_REPAIR — a blacksmith (vendor + repair); the repair button shows.</summary>
    TradeRepair = 1,
}

/// <summary>
/// Controller for the NPC event/vendor entry menu — port of <c>CUINPCEvent</c>
/// (Client/WarFare/UINPCEvent.cpp). The shipped <c>*_npcevent_*.uif</c> carries <c>Text_Title</c>,
/// <c>Btn_Sale</c> (open the vendor transaction), <c>Btn_Repair</c> + <c>Text_Repair</c> (enter
/// inventory-repair mode) and <c>btn_close</c>. <see cref="Open"/> sets the title and shows/hides the
/// repair row per <see cref="NpcEventKind"/>.
///
/// The vendor transaction window (<c>CUITransactionDlg</c>) and the inventory repair mode
/// (<c>CUIInventory::Open(INV_STATE_REPAIR)</c>) are <b>deferred</b> to a later slice, so the buttons
/// raise <see cref="SaleRequested"/> / <see cref="RepairRequested"/> and hide; the host logs the
/// deferral. Pure/headless.
/// </summary>
public sealed class NpcEventDialog
{
    private readonly UiControl _root;
    private readonly UiButton? _btnRepair;
    private readonly UiStringControl? _textRepair;
    private readonly UiStringControl? _textTitle;
    private readonly UiButton? _btnClose;

    public NpcEventDialog(GameContext context, UiControl root)
    {
        _ = context; // no reply is sent from this window (kept for pattern symmetry)
        _root = root;
        _btnRepair = root.GetChildById<UiButton>("Btn_Repair");
        _textRepair = root.GetChildById<UiStringControl>("Text_Repair");
        _textTitle = root.GetChildById<UiStringControl>("Text_Title");
        _btnClose = root.GetChildById<UiButton>("btn_close");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The trade id from the last <see cref="Open"/> (echoed into <see cref="SaleRequested"/>).</summary>
    public int TradeId { get; private set; }

    /// <summary>The target NPC id from the last <see cref="Open"/>.</summary>
    public int TargetId { get; private set; }

    /// <summary>Whether the repair button/label is currently shown (TradeRepair kind).</summary>
    public bool RepairVisible => _btnRepair?.Visible ?? false;

    /// <summary>Resolves an event kind to its window title (the text-resource lookup). Null → empty.</summary>
    public Func<NpcEventKind, string>? TitleResolver { get; set; }

    /// <summary>Raised when <c>Btn_Sale</c> is pressed (open the vendor transaction — deferred).</summary>
    public event Action<int>? SaleRequested;

    /// <summary>Raised when <c>Btn_Repair</c> is pressed (enter inventory repair — deferred).</summary>
    public event Action? RepairRequested;

    /// <summary>
    /// <c>CUINPCEvent::Open</c> — show the menu, remember the trade/target ids, set the title and
    /// toggle the repair row per <paramref name="kind"/>.
    /// </summary>
    public void Open(NpcEventKind kind, int tradeId, int targetId)
    {
        TradeId = tradeId;
        TargetId = targetId;

        if (_textTitle != null)
            _textTitle.Text = TitleResolver?.Invoke(kind) ?? string.Empty;

        bool showRepair = kind == NpcEventKind.TradeRepair;
        _btnRepair?.SetVisible(showRepair);
        _textRepair?.SetVisible(showRepair);

        _root.SetVisible(true);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) == 0)
            return;

        switch (sender.Id.ToLowerInvariant())
        {
            case "btn_sale":
                _root.SetVisible(false);
                SaleRequested?.Invoke(TradeId);
                break;

            case "btn_repair":
                _root.SetVisible(false);
                RepairRequested?.Invoke();
                break;

            case "btn_close":
                _root.SetVisible(false);
                break;
        }
    }
}
