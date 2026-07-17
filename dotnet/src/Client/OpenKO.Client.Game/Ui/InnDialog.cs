using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the inn-keeper NPC menu — port of <c>CUIInn</c> (Client/WarFare/UIInn.cpp).
/// The shipped <c>*_inn_*.uif</c> carries <c>btn_warehouse</c>, <c>btn_makeclan</c> and
/// <c>btn_sale</c>. The window is pushed open by the server's WIZ_WAREHOUSE / N3_SP_WARE_INN
/// (0x10) sub-command (routed through <see cref="InGameState.WarehouseReceived"/>).
///
/// <list type="bullet">
/// <item><c>btn_warehouse</c> → open the warehouse (CUIInn::MsgSend_OpenWareHouse →
/// <see cref="WarehouseProtocol.BuildOpen"/>).</item>
/// <item><c>btn_makeclan</c> → raise <see cref="FoundClanRequested"/> (the level/gold/already-joined
/// gates and the clan-name popup live in the executable glue, matching the original).</item>
/// <item><c>btn_sale</c> → the personal trade-sell BBS (CUITradeBBSSelector) is <b>deferred</b> to a
/// later slice; the button raises <see cref="SellBoardRequested"/> for the glue to log.</item>
/// </list>
/// Every button hides the window afterwards, as in the original.
/// </summary>
public sealed class InnDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;

    public InnDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>Raised when btn_makeclan is pressed (open the found-clan flow).</summary>
    public event Action? FoundClanRequested;

    /// <summary>Raised when btn_sale is pressed (the trade-sell BBS is deferred).</summary>
    public event Action? SellBoardRequested;

    /// <summary>Wire the WIZ_WAREHOUSE / N3_SP_WARE_INN push that opens the inn menu.</summary>
    public void Bind(InGameState inGame) => inGame.WarehouseReceived += OnWarehouse;

    private void OnWarehouse(byte sub, byte[] payload)
    {
        if (sub == WarehouseProtocol.Inn)
            _root.SetVisible(true);
    }

    /// <summary>CUIInn::MsgSend_OpenWareHouse — request the warehouse and hide.</summary>
    public void OpenWarehouse()
    {
        _context.Client.Send(WarehouseProtocol.BuildOpen());
        _root.SetVisible(false);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) == 0)
            return;

        switch (sender.Id.ToLowerInvariant())
        {
            case "btn_warehouse":
                OpenWarehouse();
                break;

            case "btn_makeclan":
                FoundClanRequested?.Invoke();
                _root.SetVisible(false);
                break;

            case "btn_sale":
                SellBoardRequested?.Invoke();
                _root.SetVisible(false);
                break;
        }
    }
}
