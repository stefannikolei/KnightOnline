using System.Globalization;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the NPC-vendor buy/sell window — port of <c>CUITransactionDlg</c>
/// (Client/WarFare/UITransactionDlg.cpp), driving the WIZ_ITEM_TRADE (0x21) sub-protocol built by
/// <see cref="TransactionProtocol"/>.
///
/// <para>Two icon grids share the window: the vendor's paged catalogue
/// (<see cref="UiAreaType.TradeNpc"/>, <see cref="MaxItemTrade"/> = 24 slots across
/// <see cref="MaxItemTradePage"/> = 12 pages) reproduced entirely from the shipped item tables via
/// <see cref="ItemTableSet.VendorItems"/> (the list never travels the wire), and a mirror of the
/// player's backpack (<see cref="UiAreaType.TradeMy"/>, 28 slots) borrowed while the window is open.
/// The window is pushed open through the NPC-event menu (<c>Btn_Sale</c> →
/// <c>CGameProcMain::DoCommercialTransaction</c> → <c>EnterTransactionState</c>).</para>
///
/// <para>Dragging a vendor icon onto the backpack buys it (N3_SP_TRADE_BUY →
/// <see cref="TransactionProtocol.BuildBuy"/>); dragging a backpack icon onto the vendor grid sells
/// it (N3_SP_TRADE_SELL → <see cref="TransactionProtocol.BuildSell"/>); dragging a backpack icon onto
/// another backpack slot rearranges it (N3_SP_TRADE_MOVE → <see cref="TransactionProtocol.BuildMove"/>).
/// A countable stack first opens the shared <see cref="CountableItemEditDialog"/> for the amount.
/// <c>btn_page_up</c>/<c>btn_page_down</c> page the vendor grid; <c>btn_close</c> hides. Pure/headless.</para>
///
/// <para>Scope note: the WIZ_ITEM_TRADE reply carries only the authoritative gold (buy/sell) — the
/// item-icon bookkeeping is client-side. The original creates/removes the icon optimistically on the
/// drop and rolls back on failure; this port applies the model change on the success reply instead
/// (buy adds, sell removes, move swaps optimistically then rolls back on 0x04), so the settled state
/// after a successful trade is identical. The exhaustive same-item-stack search is simplified to the
/// first matching stack / first free slot; all packets are byte-exact.</para>
/// </summary>
public sealed class TransactionDialog
{
    /// <summary>MAX_ITEM_TRADE — vendor items per page (GameDef.h).</summary>
    public const int MaxItemTrade = 24;

    /// <summary>MAX_ITEM_TRADE_PAGE — vendor catalogue pages (GameDef.h).</summary>
    public const int MaxItemTradePage = 12;

    /// <summary>MAX_ITEM_INVENTORY — the backpack mirror slot count.</summary>
    public const int MaxItemInventory = Inventory.BackpackSlotCount;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly ItemTableSet _items;
    private readonly IconDragState _drag;
    private readonly CountableItemEditDialog? _countableEdit;

    private readonly UiStringControl? _strMyGold; // string_item_name
    private readonly UiStringControl? _strPage;    // string_page
    private readonly UiControl? _imgInn;           // img_inn
    private readonly UiControl? _imgBlacksmith;    // img_blacksmith
    private readonly UiControl? _imgStore;         // img_store

    private readonly UiAreaControl?[] _vendorArea = new UiAreaControl?[MaxItemTrade];
    private readonly UiAreaControl?[] _invArea = new UiAreaControl?[MaxItemInventory];
    private readonly UiIconControl?[] _vendorIcons = new UiIconControl?[MaxItemTrade];
    private readonly UiIconControl?[] _invIcons = new UiIconControl?[MaxItemInventory];

    private readonly TransactionIconItem?[] _vendor = new TransactionIconItem?[MaxItemTradePage * MaxItemTrade];
    private readonly TransactionIconItem?[] _inv = new TransactionIconItem?[MaxItemInventory];

    private Inventory? _inventory;
    private LocalPlayer? _local;
    private int _tradeId;
    private short _npcId;
    private int _curPage;

    // The in-flight buy/sell/move awaiting the WIZ_ITEM_TRADE reply.
    private PendingOp? _pending;

    private enum OpKind { Buy, Sell, Move }

    private sealed record PendingOp(OpKind Kind, int SrcOrder, int DestOrder, TransactionIconItem Item, int Count);

    /// <summary>The opaque per-icon payload (__IconItemSkill analog). Buy items carry count 1.</summary>
    public sealed record TransactionIconItem(int ItemId, int Count, short Durability, ItemBasicRow? Basic, ItemExtRow? Ext);

    public TransactionDialog(
        GameContext context, UiControl root, ItemTableSet items, IconDragState drag, CountableItemEditDialog? countableEdit = null)
    {
        _context = context;
        _root = root;
        _items = items;
        _drag = drag;
        _countableEdit = countableEdit;

        _strMyGold = root.GetChildById<UiStringControl>("string_item_name");
        _strPage = root.GetChildById<UiStringControl>("string_page");
        _imgInn = root.GetChildById("img_inn");
        _imgBlacksmith = root.GetChildById("img_blacksmith");
        _imgStore = root.GetChildById("img_store");

        BuildIcons();

        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The live cursor position (fed each frame; tests set it directly).</summary>
    public UiPoint Cursor { get; set; }

    /// <summary>The current vendor catalogue page (0-based).</summary>
    public int CurrentPage => _curPage;

    /// <summary>The trade id (selling-group) the window was opened with.</summary>
    public int TradeId => _tradeId;

    /// <summary>The runtime target NPC id the buy packet carries.</summary>
    public short NpcId => _npcId;

    /// <summary>The 24 vendor-grid icon widgets (current page).</summary>
    public IReadOnlyList<UiIconControl?> VendorIcons => _vendorIcons;

    /// <summary>The 28 backpack-mirror icon widgets.</summary>
    public IReadOnlyList<UiIconControl?> InvIcons => _invIcons;

    /// <summary>The current vendor catalogue (flat page-major; nulls for empty slots).</summary>
    public IReadOnlyList<TransactionIconItem?> VendorItems => _vendor;

    /// <summary>Raised after a successful buy/sell settles the model, so the host repopulates the inventory dialog.</summary>
    public event Action? InventoryChanged;

    // ---- Runtime icon construction (CUITransactionDlg::InitIconUpdate) ------

    private void BuildIcons()
    {
        for (int i = 0; i < MaxItemTrade; i++)
        {
            _vendorArea[i] = _root.GetChildAreaByOrder(UiAreaType.TradeNpc, i);
            _vendorIcons[i] = MakeIcon(_vendorArea[i]);
        }

        for (int i = 0; i < MaxItemInventory; i++)
        {
            _invArea[i] = _root.GetChildAreaByOrder(UiAreaType.TradeMy, i);
            _invIcons[i] = MakeIcon(_invArea[i]);
        }
    }

    private UiIconControl MakeIcon(UiAreaControl? area)
    {
        UiIconControl icon = UiIconControl.CreateRuntime(area?.Region ?? default);
        icon.DragState = _drag;
        icon.SetVisible(false);
        _root.AddChild(icon);
        return icon;
    }

    // ---- Binding + open ----------------------------------------------------

    /// <summary>Wire the WIZ_ITEM_TRADE reply (buy/sell/move result).</summary>
    public void Bind(InGameState inGame)
    {
        _local = inGame.World.Local;
        _inventory = inGame.Inventory;
        inGame.ItemTradeReceived += OnItemTrade;
    }

    /// <summary>
    /// CGameProcMain::DoCommercialTransaction + CUITransactionDlg::EnterTransactionState — remember the
    /// trade/target ids, build the vendor catalogue from the item tables, pull the backpack into the
    /// mirror, set the title and show the window at page 0.
    /// </summary>
    public void Open(int tradeId, short npcId)
    {
        _tradeId = tradeId;
        _npcId = npcId;
        _local ??= _context.InGame.World.Local;
        _inventory ??= _context.InGame.Inventory;
        _pending = null;

        Array.Clear(_vendor);
        int flat = 0;
        foreach ((ItemBasicRow basic, ItemExtRow ext) in _items.VendorItems(tradeId))
        {
            if (flat >= _vendor.Length)
                break;
            _vendor[flat++] = new TransactionIconItem(
                (int)(basic.Id + ext.Id), 1, (short)(basic.MaxDurability + ext.MaxDurability), basic, ext);
        }

        MirrorBackpack();
        _curPage = 0;
        PopulateAll();
        ShowTitle(tradeId);
        _root.SetVisible(true);
    }

    /// <summary>ItemMoveFromInvToThis — snapshot the backpack into the mirror grid.</summary>
    private void MirrorBackpack()
    {
        Array.Clear(_inv);
        Inventory inv = _inventory ?? _context.InGame.Inventory;
        for (int i = 0; i < MaxItemInventory; i++)
        {
            InventoryItem? item = inv.BackpackItem(i);
            if (item == null || item.ItemId == 0)
                continue;
            (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find((uint)item.ItemId);
            _inv[i] = new TransactionIconItem(item.ItemId, item.Count, item.Durability, basic, ext);
        }
    }

    /// <summary>ShowTitle — blacksmith for selling groups 122/222, else the general store.</summary>
    private void ShowTitle(int tradeId)
    {
        bool blacksmith = tradeId / 1000 is 122 or 222;
        _imgInn?.SetVisible(false);
        _imgBlacksmith?.SetVisible(blacksmith);
        _imgStore?.SetVisible(!blacksmith);
    }

    // ---- Population --------------------------------------------------------

    private void PopulateAll()
    {
        PopulateVendor();
        PopulateInv();
        UpdatePageString();
        UpdateGold();
    }

    private void PopulateVendor()
    {
        for (int i = 0; i < MaxItemTrade; i++)
        {
            UiIconControl? icon = _vendorIcons[i];
            if (icon == null)
                continue;
            if (_vendorArea[i] is { } area)
                icon.SetIconRegion(area.Region);

            ApplyIcon(icon, _vendor[_curPage * MaxItemTrade + i]);
        }
    }

    private void PopulateInv()
    {
        for (int i = 0; i < MaxItemInventory; i++)
        {
            UiIconControl? icon = _invIcons[i];
            if (icon == null)
                continue;
            if (_invArea[i] is { } area)
                icon.SetIconRegion(area.Region);

            ApplyIcon(icon, _inv[i]);
        }
    }

    private static void ApplyIcon(UiIconControl icon, TransactionIconItem? payload)
    {
        if (payload == null || payload.ItemId == 0)
        {
            icon.SetVisible(false);
            icon.Payload = null;
            icon.IconTexture = string.Empty;
            icon.ItemSkillId = 0;
            return;
        }

        icon.IconTexture = ItemResourceNamer.MakeResourceFileName(payload.Basic, payload.Ext).IconFileName;
        icon.ItemSkillId = payload.ItemId;
        icon.Payload = payload;
        icon.SetVisible(true);
    }

    private void UpdatePageString()
    {
        if (_strPage != null)
            _strPage.Text = (_curPage + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateGold()
    {
        if (_strMyGold != null && _local != null)
            _strMyGold.Text = _local.Gold.ToString(CultureInfo.InvariantCulture);
    }

    // ---- Paging (btn_page_up / btn_page_down) ------------------------------

    public void PageUp()
    {
        _curPage = Math.Max(0, _curPage - 1);
        PopulateVendor();
        UpdatePageString();
    }

    public void PageDown()
    {
        _curPage = Math.Min(MaxItemTradePage - 1, _curPage + 1);
        PopulateVendor();
        UpdatePageString();
    }

    // ---- Buy (vendor → backpack) -------------------------------------------

    /// <summary>
    /// CUITransactionDlg buy path — purchase the vendor item at page-local <paramref name="vendorOrder"/>.
    /// A countable stack first opens the split popup (bounded by what the wallet affords); otherwise a
    /// single unit is bought. Returns the sent packet, or null when the popup gathers the amount / the
    /// buy is impossible (no free slot).
    /// </summary>
    public byte[]? Buy(int vendorOrder)
    {
        if (vendorOrder < 0 || vendorOrder >= MaxItemTrade)
            return null;
        if (_vendor[_curPage * MaxItemTrade + vendorOrder] is not { } item)
            return null;

        if (item.Basic?.Countable == true && _countableEdit != null)
        {
            int price = ItemTableSet.GetBuyPrice(item.Basic, item.Ext);
            int gold = _local?.Gold ?? 0;
            int max = price > 0 ? Math.Max(1, gold / price) : CountableItemEditDialog.MaxCount;
            _countableEdit.Open(max, count => SendBuy(item, count));
            return null;
        }

        return SendBuy(item, 1);
    }

    /// <summary>
    /// SendToServerBuyMsg — pick the destination backpack slot (a matching countable stack, else the
    /// first free slot), send N3_SP_TRADE_BUY and record the pending op. Returns the packet.
    /// </summary>
    public byte[]? SendBuy(TransactionIconItem item, int count)
    {
        if (count <= 0)
            return null;

        int destOrder = FindBuyDest(item);
        if (destOrder < 0)
            return null;

        byte[] packet = TransactionProtocol.BuildBuy(_tradeId, _npcId, item.ItemId, (byte)destOrder, (short)count);
        _context.Client.Send(packet);

        _drag.WaitFromServer = true;
        _pending = new PendingOp(OpKind.Buy, -1, destOrder, item, count);
        return packet;
    }

    private int FindBuyDest(TransactionIconItem item)
    {
        if (item.Basic?.Countable == true)
        {
            for (int i = 0; i < MaxItemInventory; i++)
            {
                if (_inv[i] != null && _inv[i]!.ItemId == item.ItemId)
                    return i;
            }
        }

        for (int i = 0; i < MaxItemInventory; i++)
        {
            if (_inv[i] == null)
                return i;
        }

        return -1;
    }

    // ---- Sell (backpack → vendor) ------------------------------------------

    /// <summary>
    /// CUITransactionDlg sell path — sell the backpack item at <paramref name="invOrder"/>. A countable
    /// stack first opens the split popup; otherwise the whole stack is sold. Returns the sent packet, or
    /// null when the popup gathers the amount.
    /// </summary>
    public byte[]? Sell(int invOrder)
    {
        if (invOrder < 0 || invOrder >= MaxItemInventory || _inv[invOrder] is not { } item)
            return null;

        if (item.Basic?.Countable == true && _countableEdit != null)
        {
            _countableEdit.Open(item.Count, count => SendSell(invOrder, item, count));
            return null;
        }

        return SendSell(invOrder, item, item.Count);
    }

    /// <summary>SendToServerSellMsg — send N3_SP_TRADE_SELL and record the pending op. Returns the packet.</summary>
    public byte[]? SendSell(int invOrder, TransactionIconItem item, int count)
    {
        if (count <= 0 || count > item.Count)
            return null;

        byte[] packet = TransactionProtocol.BuildSell(item.ItemId, (byte)invOrder, (short)count);
        _context.Client.Send(packet);

        _drag.WaitFromServer = true;
        _pending = new PendingOp(OpKind.Sell, invOrder, -1, item, count);
        return packet;
    }

    // ---- Move (backpack → backpack) ----------------------------------------

    /// <summary>
    /// SendToServerMoveMsg — swap the backpack items at <paramref name="startOrder"/> and
    /// <paramref name="destOrder"/> optimistically (the server confirms with 0x03 / rolls back 0x04) and
    /// send N3_SP_TRADE_MOVE. Returns the packet.
    /// </summary>
    public byte[]? Move(int startOrder, int destOrder)
    {
        if (startOrder < 0 || startOrder >= MaxItemInventory || destOrder < 0 || destOrder >= MaxItemInventory)
            return null;
        if (startOrder == destOrder || _inv[startOrder] is not { } item)
            return null;

        SwapInv(startOrder, destOrder);

        byte[] packet = TransactionProtocol.BuildMove(item.ItemId, (byte)startOrder, (byte)destOrder);
        _context.Client.Send(packet);

        _drag.WaitFromServer = true;
        _pending = new PendingOp(OpKind.Move, startOrder, destOrder, item, 0);
        PopulateAll();
        return packet;
    }

    private void SwapInv(int a, int b)
    {
        (_inv[a], _inv[b]) = (_inv[b], _inv[a]);
        WriteInvSlot(a);
        WriteInvSlot(b);
    }

    // ---- Receive (MsgRecv_ItemTradeResult) ---------------------------------

    /// <summary>
    /// CGameProcMain::MsgRecv_ItemTradeResult — settle the in-flight op. 0x01 success carries the new
    /// gold and commits the buy/sell; 0x00 failure discards it; 0x03/0x04 confirm/roll back the move.
    /// </summary>
    public void OnItemTrade(TransactionResult result)
    {
        _drag.WaitFromServer = false;
        PendingOp? op = _pending;
        _pending = null;

        if (result.MoveSuccess)
        {
            _drag.Reset();
            return;
        }

        if (result.MoveFail)
        {
            if (op is { Kind: OpKind.Move })
                SwapInv(op.DestOrder, op.SrcOrder); // undo the optimistic swap
            _drag.Reset();
            PopulateAll();
            return;
        }

        if (result.Success)
        {
            if (_local != null)
                _local.Gold = (int)result.Money;

            if (op is { Kind: OpKind.Buy })
                ApplyBuy(op);
            else if (op is { Kind: OpKind.Sell })
                ApplySell(op);

            _drag.Reset();
            PopulateAll();
            InventoryChanged?.Invoke();
            return;
        }

        // 0x00 failure — nothing was applied optimistically; just release and repaint.
        _drag.Reset();
        PopulateAll();
    }

    private void ApplyBuy(PendingOp op)
    {
        int order = op.DestOrder;
        TransactionIconItem? existing = _inv[order];
        _inv[order] = existing != null && existing.ItemId == op.Item.ItemId && op.Item.Basic?.Countable == true
            ? existing with { Count = existing.Count + op.Count }
            : op.Item with { Count = op.Count };
        WriteInvSlot(order);
    }

    private void ApplySell(PendingOp op)
    {
        int order = op.SrcOrder;
        TransactionIconItem? src = _inv[order];
        if (src == null)
            return;

        int remaining = src.Count - op.Count;
        _inv[order] = op.Item.Basic?.Countable == true && remaining > 0 ? src with { Count = remaining } : null;
        WriteInvSlot(order);
    }

    /// <summary>Write the mirror slot back over the backpack model (the authoritative post-trade layout).</summary>
    private void WriteInvSlot(int order)
    {
        Inventory inv = _inventory ?? _context.InGame.Inventory;
        int index = Inventory.BackpackIndex(order);
        if (_inv[order] is { } item)
            inv.Set(index, new InventoryItem(item.ItemId, item.Count, item.Durability));
        else
            inv.Clear(index);
    }

    // ---- Message routing (CUITransactionDlg::ReceiveMessage) ---------------

    private void OnMessage(UiControl sender, uint msg)
    {
        switch (msg)
        {
            case UiMsg.ButtonClick:
                OnButton(sender);
                break;

            case UiMsg.IconDownFirst:
                OnIconDownFirst(sender);
                break;

            case UiMsg.IconDown:
                _drag.SelectedIcon.Icon?.MoveToCursor(Cursor);
                break;

            case UiMsg.IconUp:
                OnIconUp();
                break;
        }
    }

    private void OnButton(UiControl sender)
    {
        switch (sender.Id.ToLowerInvariant())
        {
            case "btn_close":
                Close();
                break;

            case "btn_page_up":
                PageUp();
                break;

            case "btn_page_down":
                PageDown();
                break;
        }
    }

    private void OnIconDownFirst(UiControl sender)
    {
        if (sender is not UiIconControl icon || icon.Payload is not TransactionIconItem)
            return;

        (bool isVendor, int order) = FindSlot(icon);
        if (order < 0)
            return;

        _drag.SelectedIcon.Location = new UiWndIconInfo
        {
            Wnd = UiWnd.Transaction,
            District = isVendor ? UiWndDistrict.TradeNpc : UiWndDistrict.TradeMy,
            Order = order,
        };
        _drag.SelectedIcon.Item = icon.Payload;
        _drag.SelectedIcon.Icon = icon;
        icon.MoveToCursor(Cursor);
    }

    private void OnIconUp()
    {
        SelectedIconInfo sel = _drag.SelectedIcon;
        if (!sel.IsActive || sel.Icon is not { } icon)
        {
            sel.Clear();
            return;
        }

        bool srcIsVendor = sel.Location.District == UiWndDistrict.TradeNpc;
        int srcOrder = sel.Location.Order;

        if (!ResolveDrop(out bool destIsVendor, out int destOrder))
        {
            RestoreIcon(icon, srcIsVendor, srcOrder);
            sel.Clear();
            return;
        }

        if (srcIsVendor && !destIsVendor)
            Buy(srcOrder);                    // vendor → backpack
        else if (!srcIsVendor && destIsVendor)
            Sell(srcOrder);                   // backpack → vendor
        else if (!srcIsVendor && srcOrder != destOrder)
            Move(srcOrder, destOrder);        // backpack → backpack
        else
            RestoreIcon(icon, srcIsVendor, srcOrder); // vendor → vendor / same slot: no-op

        sel.Clear();
    }

    private bool ResolveDrop(out bool isVendor, out int order)
    {
        for (int i = 0; i < MaxItemTrade; i++)
        {
            if (_vendorArea[i]?.IsIn(Cursor.X, Cursor.Y) == true)
            {
                isVendor = true;
                order = i;
                return true;
            }
        }

        for (int i = 0; i < MaxItemInventory; i++)
        {
            if (_invArea[i]?.IsIn(Cursor.X, Cursor.Y) == true)
            {
                isVendor = false;
                order = i;
                return true;
            }
        }

        isVendor = false;
        order = -1;
        return false;
    }

    private (bool IsVendor, int Order) FindSlot(UiIconControl icon)
    {
        for (int i = 0; i < MaxItemTrade; i++)
        {
            if (ReferenceEquals(_vendorIcons[i], icon))
                return (true, i);
        }

        for (int i = 0; i < MaxItemInventory; i++)
        {
            if (ReferenceEquals(_invIcons[i], icon))
                return (false, i);
        }

        return (false, -1);
    }

    private void RestoreIcon(UiIconControl icon, bool isVendor, int order)
    {
        UiAreaControl? area = isVendor ? _vendorArea[order] : _invArea[order];
        if (area != null)
            icon.SetIconRegion(area.Region);
    }

    /// <summary>LeaveTransactionState — hide the window (the backpack mirror is already the live model).</summary>
    public void Close() => _root.SetVisible(false);

    public void Show() => _root.SetVisible(true);

    public void Hide() => _root.SetVisible(false);
}
