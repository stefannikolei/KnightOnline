using System.Globalization;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the bank/warehouse window — port of <c>CUIWareHouseDlg</c>
/// (Client/WarFare/UIWareHouseDlg.cpp). Two icon grids share the window: the paged warehouse
/// store (<see cref="UiAreaType.TradeNpc"/>, <see cref="WarehouseProtocol.ItemsPerPage"/> = 24
/// slots across <see cref="WarehouseProtocol.PageCount"/> = 8 pages) and the player's backpack
/// mirror (<see cref="UiAreaType.TradeMy"/>, 28 slots). The window is pushed open by the
/// WIZ_WAREHOUSE / N3_SP_WARE_OPEN reply (parsed by <see cref="WarehouseProtocol.ParseOpen"/>).
///
/// <para>Dragging a backpack icon onto the store deposits it (N3_SP_WARE_GET_IN →
/// <see cref="WarehouseProtocol.BuildInput"/>); dragging a store icon onto the backpack
/// withdraws it (N3_SP_WARE_GET_OUT → <see cref="WarehouseProtocol.BuildOutput"/>). A countable
/// item first opens the shared <see cref="CountableItemEditDialog"/> for the amount. The
/// <c>btn_gold</c> / <c>btn_gold_warehouse</c> buttons open the same popup and deposit/withdraw
/// gold (dwGold pseudo-item → <see cref="WarehouseProtocol.BuildGoldInput"/> /
/// <see cref="WarehouseProtocol.BuildGoldOutput"/>). <c>btn_page_up</c>/<c>btn_page_down</c> page
/// the store grid. Pure/headless.</para>
///
/// <para>Scope note: the original's exhaustive same-item-stack / free-slot search across all
/// pages (and intra-grid moves) is simplified to the drop-target slot; the deposit/withdraw and
/// gold packets are byte-exact. Full count reconciliation is server-driven (WIZ_ITEM_COUNT_CHANGE).</para>
/// </summary>
public sealed class WareHouseDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly ItemTableSet _items;
    private readonly IconDragState _drag;
    private readonly CountableItemEditDialog? _countableEdit;

    private readonly UiStringControl? _strMyGold;    // string_item_name
    private readonly UiStringControl? _strWareGold;  // string_wareitem_name
    private readonly UiStringControl? _strPage;      // string_page

    private readonly UiAreaControl?[] _wareArea = new UiAreaControl?[WarehouseProtocol.ItemsPerPage];
    private readonly UiAreaControl?[] _invArea = new UiAreaControl?[Inventory.BackpackSlotCount];
    private readonly UiIconControl?[] _wareIcons = new UiIconControl?[WarehouseProtocol.ItemsPerPage];
    private readonly UiIconControl?[] _invIcons = new UiIconControl?[Inventory.BackpackSlotCount];

    private readonly WarehouseIconItem?[] _ware = new WarehouseIconItem?[WarehouseProtocol.SlotCount];

    private Inventory? _inventory;
    private LocalPlayer? _local;
    private int _curPage;
    private int _wareGold;

    // The in-flight deposit/withdraw awaiting the server result byte.
    private PendingOp? _pending;

    private enum OpKind { Deposit, Withdraw }

    private sealed record PendingOp(OpKind Kind, int InvOrder, int WareFlat, WarehouseIconItem Item, int Count);

    /// <summary>The opaque per-icon payload for a stored item (the __IconItemSkill analog).</summary>
    public sealed record WarehouseIconItem(int ItemId, int Count, short Durability, ItemBasicRow? Basic, ItemExtRow? Ext);

    public WareHouseDialog(
        GameContext context, UiControl root, ItemTableSet items, IconDragState drag, CountableItemEditDialog? countableEdit = null)
    {
        _context = context;
        _root = root;
        _items = items;
        _drag = drag;
        _countableEdit = countableEdit;

        _strMyGold = root.GetChildById<UiStringControl>("string_item_name");
        _strWareGold = root.GetChildById<UiStringControl>("string_wareitem_name");
        _strPage = root.GetChildById<UiStringControl>("string_page");

        BuildIcons();

        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The live cursor position (fed each frame; tests set it directly).</summary>
    public UiPoint Cursor { get; set; }

    /// <summary>The current warehouse page (0-based).</summary>
    public int CurrentPage => _curPage;

    /// <summary>The stored gold last shown.</summary>
    public int WareGold => _wareGold;

    /// <summary>The 24 warehouse-grid icon widgets (current page).</summary>
    public IReadOnlyList<UiIconControl?> WareIcons => _wareIcons;

    /// <summary>The 28 backpack-grid icon widgets.</summary>
    public IReadOnlyList<UiIconControl?> InvIcons => _invIcons;

    // ---- Runtime icon construction (CUIWareHouseDlg::InitIconUpdate) --------

    private void BuildIcons()
    {
        for (int i = 0; i < WarehouseProtocol.ItemsPerPage; i++)
        {
            _wareArea[i] = _root.GetChildAreaByOrder(UiAreaType.TradeNpc, i);
            _wareIcons[i] = MakeIcon(_wareArea[i]);
        }

        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
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

    // ---- Population --------------------------------------------------------

    /// <summary>Wire the WIZ_WAREHOUSE open reply.</summary>
    public void Bind(InGameState inGame)
    {
        _local = inGame.World.Local;
        _inventory = inGame.Inventory;
        inGame.WarehouseReceived += OnWarehouse;
    }

    private void OnWarehouse(byte sub, byte[] payload)
    {
        switch (sub)
        {
            case WarehouseProtocol.Open:
                Open(WarehouseProtocol.ParseOpen(payload), _inventory ?? _context.InGame.Inventory);
                break;

            case WarehouseProtocol.Input:
                OnResult(payload.Length > 2 && payload[2] == 0x01);
                break;

            case WarehouseProtocol.Output:
                OnResult(payload.Length > 2 && payload[2] == 0x01);
                break;
        }
    }

    /// <summary>
    /// CGameProcMain::MsgRecv_WareHouseOpen — fill both grids from the reply and the current
    /// inventory, reset to page 0 and show the window.
    /// </summary>
    public void Open(WarehouseContents contents, Inventory inventory)
    {
        _inventory = inventory;
        _local ??= _context.InGame.World.Local;
        _wareGold = contents.Gold;

        Array.Clear(_ware);
        foreach (WarehouseItem row in contents.Items)
        {
            (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find(row.ItemId);
            _ware[row.Index] = new WarehouseIconItem(
                (int)row.ItemId, row.Count, row.Durability, basic, ext);
        }

        _curPage = 0;
        PopulateAll();
        _root.SetVisible(true);
    }

    private void PopulateAll()
    {
        PopulateWare();
        PopulateInv();
        UpdatePageString();
        UpdateGoldStrings();
    }

    private void PopulateWare()
    {
        for (int i = 0; i < WarehouseProtocol.ItemsPerPage; i++)
        {
            UiIconControl? icon = _wareIcons[i];
            if (icon == null)
                continue;
            if (_wareArea[i] is { } area)
                icon.SetIconRegion(area.Region);

            WarehouseIconItem? item = _ware[_curPage * WarehouseProtocol.ItemsPerPage + i];
            ApplyIcon(icon, item?.ItemId ?? 0, item);
        }
    }

    private void PopulateInv()
    {
        Inventory inv = _inventory ?? _context.InGame.Inventory;
        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            UiIconControl? icon = _invIcons[i];
            if (icon == null)
                continue;
            if (_invArea[i] is { } area)
                icon.SetIconRegion(area.Region);

            InventoryItem? item = inv.BackpackItem(i);
            if (item == null || item.ItemId == 0)
            {
                ApplyIcon(icon, 0, null);
                continue;
            }

            (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find((uint)item.ItemId);
            ApplyIcon(icon, item.ItemId, new WarehouseIconItem(item.ItemId, item.Count, item.Durability, basic, ext));
        }
    }

    private void ApplyIcon(UiIconControl icon, int itemId, WarehouseIconItem? payload)
    {
        if (itemId == 0 || payload == null)
        {
            icon.SetVisible(false);
            icon.Payload = null;
            icon.IconTexture = string.Empty;
            icon.ItemSkillId = 0;
            return;
        }

        icon.IconTexture = ItemResourceNamer.MakeResourceFileName(payload.Basic, payload.Ext).IconFileName;
        icon.ItemSkillId = itemId;
        icon.Payload = payload;
        icon.SetVisible(true);
    }

    private void UpdatePageString()
    {
        if (_strPage != null)
            _strPage.Text = (_curPage + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateGoldStrings()
    {
        if (_strWareGold != null)
            _strWareGold.Text = _wareGold.ToString(CultureInfo.InvariantCulture);
        if (_strMyGold != null && _local != null)
            _strMyGold.Text = _local.Gold.ToString(CultureInfo.InvariantCulture);
    }

    // ---- Paging (CUIWareHouseDlg btn_page_up / btn_page_down) ---------------

    public void PageUp()
    {
        _curPage = Math.Max(0, _curPage - 1);
        PopulateWare();
        UpdatePageString();
    }

    public void PageDown()
    {
        _curPage = Math.Min(WarehouseProtocol.PageCount - 1, _curPage + 1);
        PopulateWare();
        UpdatePageString();
    }

    // ---- Message routing (CUIWareHouseDlg::ReceiveMessage) ------------------

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
                _root.SetVisible(false);
                break;

            case "btn_page_up":
                PageUp();
                break;

            case "btn_page_down":
                PageDown();
                break;

            case "btn_gold":
                OpenGoldPopup(deposit: true);
                break;

            case "btn_gold_warehouse":
                OpenGoldPopup(deposit: false);
                break;
        }
    }

    // ---- Gold in/out (GoldCountToWareOK / GoldCountFromWareOK) --------------

    private void OpenGoldPopup(bool deposit)
    {
        if (_countableEdit == null)
            return;
        int max = deposit ? (_local?.Gold ?? 0) : _wareGold;
        _countableEdit.Open(max, amount => GoldOk(deposit, amount));
    }

    /// <summary>
    /// The gold popup returned an amount. Deposit (btn_gold) moves gold into the warehouse;
    /// withdraw (btn_gold_warehouse) moves it out. Both update the strings/local gold optimistically
    /// and send the dwGold packet.
    /// </summary>
    public byte[]? GoldOk(bool deposit, int amount)
    {
        if (amount <= 0 || _local == null)
            return null;

        if (deposit)
        {
            if (amount > _local.Gold)
                return null;
            _local.Gold -= amount;
            _wareGold += amount;
            UpdateGoldStrings();
            byte[] packet = WarehouseProtocol.BuildGoldInput(amount);
            _context.Client.Send(packet);
            return packet;
        }
        else
        {
            if (amount > _wareGold)
                return null;
            _local.Gold += amount;
            _wareGold -= amount;
            UpdateGoldStrings();
            byte[] packet = WarehouseProtocol.BuildGoldOutput(amount);
            _context.Client.Send(packet);
            return packet;
        }
    }

    // ---- Item drag/drop (deposit / withdraw) -------------------------------

    private void OnIconDownFirst(UiControl sender)
    {
        if (sender is not UiIconControl icon || icon.Payload is not WarehouseIconItem)
            return;

        (bool isWare, int order) = FindSlot(icon);
        if (order < 0)
            return;

        _drag.SelectedIcon.Location = new UiWndIconInfo
        {
            Wnd = UiWnd.WareHouse,
            District = isWare ? UiWndDistrict.TradeNpc : UiWndDistrict.TradeMy,
            Order = order,
        };
        _drag.SelectedIcon.Item = icon.Payload;
        _drag.SelectedIcon.Icon = icon;
        icon.MoveToCursor(Cursor);
    }

    private void OnIconUp()
    {
        SelectedIconInfo sel = _drag.SelectedIcon;
        if (!sel.IsActive || sel.Item is not WarehouseIconItem item || sel.Icon is not { } icon)
        {
            sel.Clear();
            return;
        }

        bool srcIsWare = sel.Location.District == UiWndDistrict.TradeNpc;
        int srcOrder = sel.Location.Order;

        if (!ResolveDrop(out bool destIsWare, out int destOrder))
        {
            RestoreIcon(icon, srcIsWare, srcOrder);
            sel.Clear();
            return;
        }

        // Same-grid drop onto the source cell → restore (no-op).
        if (srcIsWare == destIsWare && srcOrder == destOrder)
        {
            RestoreIcon(icon, srcIsWare, srcOrder);
            sel.Clear();
            return;
        }

        if (!srcIsWare && destIsWare)
            BeginDeposit(item, srcOrder, destOrder);
        else if (srcIsWare && !destIsWare)
            BeginWithdraw(item, srcOrder, destOrder);
        else
            SendIntraMove(item, srcIsWare, srcOrder, destOrder); // ware→ware / inv→inv move

        sel.Clear();
    }

    private void BeginDeposit(WarehouseIconItem item, int invOrder, int wareOrder)
    {
        if (item.Basic?.Countable == true && _countableEdit != null)
        {
            _countableEdit.Open(item.Count, count => SendDeposit(item, invOrder, wareOrder, count));
            return;
        }

        SendDeposit(item, invOrder, wareOrder, item.Count);
    }

    /// <summary>Send the deposit (N3_SP_WARE_GET_IN) and record the pending op.</summary>
    public byte[] SendDeposit(WarehouseIconItem item, int invOrder, int wareOrder, int count)
    {
        int wareFlat = _curPage * WarehouseProtocol.ItemsPerPage + wareOrder;
        byte[] packet = WarehouseProtocol.BuildInput(
            item.ItemId, (byte)_curPage, (byte)invOrder, (byte)wareOrder, count);
        _context.Client.Send(packet);

        _drag.WaitFromServer = true;
        _pending = new PendingOp(OpKind.Deposit, invOrder, wareFlat, item, count);
        return packet;
    }

    private void BeginWithdraw(WarehouseIconItem item, int wareOrder, int invOrder)
    {
        if (item.Basic?.Countable == true && _countableEdit != null)
        {
            _countableEdit.Open(item.Count, count => SendWithdraw(item, wareOrder, invOrder, count));
            return;
        }

        SendWithdraw(item, wareOrder, invOrder, item.Count);
    }

    /// <summary>Send the withdraw (N3_SP_WARE_GET_OUT) and record the pending op.</summary>
    public byte[] SendWithdraw(WarehouseIconItem item, int wareOrder, int invOrder, int count)
    {
        int wareFlat = _curPage * WarehouseProtocol.ItemsPerPage + wareOrder;
        byte[] packet = WarehouseProtocol.BuildOutput(
            item.ItemId, (byte)_curPage, (byte)wareOrder, (byte)invOrder, count);
        _context.Client.Send(packet);

        _drag.WaitFromServer = true;
        _pending = new PendingOp(OpKind.Withdraw, invOrder, wareFlat, item, count);
        return packet;
    }

    private void SendIntraMove(WarehouseIconItem item, bool isWare, int srcOrder, int destOrder)
    {
        byte[] packet = isWare
            ? WarehouseProtocol.BuildWareMove(item.ItemId, (byte)_curPage, (byte)srcOrder, (byte)destOrder)
            : WarehouseProtocol.BuildInvMove(item.ItemId, (byte)_curPage, (byte)srcOrder, (byte)destOrder);
        _context.Client.Send(packet);
        PopulateAll(); // optimistic snap-back; the server drives the authoritative layout
    }

    /// <summary>
    /// The deposit/withdraw result byte landed: on success apply the model move, then repopulate
    /// (either way, which rolls a failed optimistic move back). Releases the drag lock.
    /// </summary>
    public void OnResult(bool ok)
    {
        if (ok && _pending is { } op)
            ApplyPending(op);

        _pending = null;
        _drag.WaitFromServer = false;
        _drag.Reset();
        PopulateAll();
    }

    private void ApplyPending(PendingOp op)
    {
        Inventory inv = _inventory ?? _context.InGame.Inventory;
        int invIndex = Inventory.BackpackIndex(op.InvOrder);

        if (op.Kind == OpKind.Deposit)
        {
            InventoryItem? src = inv.BackpackItem(op.InvOrder);
            if (src != null)
            {
                int remaining = src.Count - op.Count;
                if (op.Item.Basic?.Countable == true && remaining > 0)
                    inv.SetCount(invIndex, remaining);
                else
                    inv.Clear(invIndex);
            }

            WarehouseIconItem? existing = _ware[op.WareFlat];
            _ware[op.WareFlat] = existing != null && existing.ItemId == op.Item.ItemId && op.Item.Basic?.Countable == true
                ? existing with { Count = existing.Count + op.Count }
                : op.Item with { Count = op.Count };
        }
        else // Withdraw
        {
            WarehouseIconItem? src = _ware[op.WareFlat];
            if (src != null)
            {
                int remaining = src.Count - op.Count;
                _ware[op.WareFlat] = op.Item.Basic?.Countable == true && remaining > 0
                    ? src with { Count = remaining }
                    : null;
            }

            InventoryItem? existing = inv.BackpackItem(op.InvOrder);
            if (existing != null && existing.ItemId == op.Item.ItemId && op.Item.Basic?.Countable == true)
                inv.SetCount(invIndex, existing.Count + op.Count);
            else
                inv.Set(invIndex, new InventoryItem(op.Item.ItemId, op.Count, op.Item.Durability));
        }
    }

    // ---- Helpers -----------------------------------------------------------

    private bool ResolveDrop(out bool isWare, out int order)
    {
        for (int i = 0; i < WarehouseProtocol.ItemsPerPage; i++)
        {
            if (_wareArea[i]?.IsIn(Cursor.X, Cursor.Y) == true)
            {
                isWare = true;
                order = i;
                return true;
            }
        }

        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            if (_invArea[i]?.IsIn(Cursor.X, Cursor.Y) == true)
            {
                isWare = false;
                order = i;
                return true;
            }
        }

        isWare = false;
        order = -1;
        return false;
    }

    private (bool IsWare, int Order) FindSlot(UiIconControl icon)
    {
        for (int i = 0; i < WarehouseProtocol.ItemsPerPage; i++)
        {
            if (ReferenceEquals(_wareIcons[i], icon))
                return (true, i);
        }

        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            if (ReferenceEquals(_invIcons[i], icon))
                return (false, i);
        }

        return (false, -1);
    }

    private void RestoreIcon(UiIconControl icon, bool isWare, int order)
    {
        UiAreaControl? area = isWare ? _wareArea[order] : _invArea[order];
        if (area != null)
            icon.SetIconRegion(area.Region);
    }

    public void Show() => _root.SetVisible(true);

    public void Hide() => _root.SetVisible(false);
}
