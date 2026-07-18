using System.Globalization;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>The inventory sub-state (CUIInventory INV_STATE_*): normal drag/drop or NPC repair.</summary>
public enum InventoryMode
{
    /// <summary>INV_STATE_NORMAL — the ordinary drag/drop + right-click equip inventory.</summary>
    Normal = 0,

    /// <summary>INV_STATE_REPAIR — the blacksmith repair mode (CItemRepairMgr drives clicks).</summary>
    Repair = 1,
}

/// <summary>
/// Controller for the inventory dialog — port of <c>CUIInventory</c>
/// (Client/WarFare/UIInventory.cpp). The shipped <c>*_inventory_*.uif</c> carries only the
/// slot <see cref="UiAreaControl"/> regions (Slot 0..13 equip, Inv 0..27 backpack) and the
/// <c>area_char</c> / <c>area_samma</c> drop regions plus <c>text_weight</c>; the 42 item icons
/// are created at runtime (<c>CUIInventory::InitIconUpdate</c>) via
/// <see cref="UiIconControl.CreateRuntime"/> and placed at each slot's area region.
///
/// The drag/drop flow mirrors the original: <c>UIMSG_ICON_DOWN_FIRST</c> picks an icon up into
/// the shared <see cref="IconDragState.SelectedIcon"/>, <c>UIMSG_ICON_UP</c> resolves the drop
/// target (a Slot/Inv area under the cursor, <c>area_char</c> auto-equip, or <c>area_samma</c>
/// destroy), fills the <see cref="IconDragState.RecoveryJob"/>, sets
/// <see cref="IconDragState.WaitFromServer"/>, sends WIZ_ITEM_MOVE and optimistically snaps the
/// icon to the destination. The server's WIZ_ITEM_MOVE reply drives
/// <see cref="OnItemMoveResult"/> (commit on success, rollback on failure). Right-click
/// (<c>UIMSG_ICON_RUP</c>) is the quick equip/unequip shortcut. Pure/headless — only the icon
/// textures are strings the device renderer resolves.
/// </summary>
public sealed class InventoryDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly ItemTableSet _items;
    private readonly IconDragState _drag;

    private readonly UiControl? _areaChar;      // area_char — equip drop region
    private readonly UiControl? _areaDestroy;   // area_samma — destroy drop region
    private readonly UiStringControl? _textWeight;
    private readonly UiButton? _btnClose;
    private readonly UiButton? _btnDestroyOk;
    private readonly UiButton? _btnDestroyCancel;

    private readonly UiAreaControl?[] _slotArea = new UiAreaControl?[Inventory.EquipSlotCount];
    private readonly UiAreaControl?[] _invArea = new UiAreaControl?[Inventory.BackpackSlotCount];
    private readonly UiIconControl?[] _equip = new UiIconControl?[Inventory.EquipSlotCount];
    private readonly UiIconControl?[] _backpack = new UiIconControl?[Inventory.BackpackSlotCount];

    private Inventory? _inventory;
    private LocalPlayer? _local;

    // The in-flight move (flat item-array indices), committed/rolled back on the server reply.
    private int _pendingSrcFlat = -1;
    private int _pendingDestFlat = -1;

    // Right-click press slot, so RUP only fires when press+release land on the same icon.
    private int _rBtnDownIndex = -1;

    // The item queued for destruction (dropped on area_samma), pending confirm.
    private InventoryIconItem? _destroyItem;
    private int _destroyFlat = -1;

    // Repair mode (CItemRepairMgr): the pressed icon, and the in-flight repair (item + flat slot).
    private InventoryMode _mode = InventoryMode.Normal;
    private UiIconControl? _repairPressIcon;
    private int _repairPendingFlat = -1;

    /// <summary>The opaque per-icon payload (the __IconItemSkill analog).</summary>
    public sealed record InventoryIconItem(
        int ItemId, int Count, short Durability, byte Flag, ItemBasicRow? Basic, ItemExtRow? Ext);

    public InventoryDialog(GameContext context, UiControl root, ItemTableSet items, IconDragState drag)
    {
        _context = context;
        _root = root;
        _items = items;
        _drag = drag;

        _areaChar = root.GetChildById("area_char");
        _areaDestroy = root.GetChildById("area_samma");
        _textWeight = root.GetChildById<UiStringControl>("text_weight");
        _btnClose = root.GetChildById<UiButton>("btn_close");
        _btnDestroyOk = root.GetChildById<UiButton>("btn_Destroy_ok");
        _btnDestroyCancel = root.GetChildById<UiButton>("btn_Destroy_cancel");

        BuildIcons();

        root.Message += OnMessage;
        _root.SetVisible(false); // hidden until toggled open (btn_inventory / hotkey)
    }

    /// <summary>The runtime dialog root (registered with the UI manager).</summary>
    public UiControl Root => _root;

    /// <summary>
    /// The live cursor position, fed by the executable each frame (tests set it directly). The
    /// drag-follow and drop-target resolution read it, mirroring the C++ MouseGetPos() calls.
    /// </summary>
    public UiPoint Cursor { get; set; }

    /// <summary>True while the area_samma destroy-confirm is pending (real send is deferred).</summary>
    public bool DestroyConfirmPending => _destroyItem != null;

    /// <summary>The current inventory sub-state (normal drag/drop vs blacksmith repair).</summary>
    public InventoryMode Mode => _mode;

    /// <summary>
    /// The repair image-tooltip driven by <see cref="TickRepair"/> in repair mode
    /// (CUIRepairTooltipDlg). Set by the executable; null in headless tests.
    /// </summary>
    public RepairTooltipControl? RepairTooltip { get; set; }

    /// <summary>Raised when a repair click is blocked by insufficient gold (IDS_REPAIR_LACK_GOLD).</summary>
    public event Action? RepairLackGold;

    /// <summary>The hover result in repair mode: the item under the cursor and its repair price.</summary>
    public readonly record struct RepairHoverInfo(
        InventoryIconItem Item, byte Arm, int Order, int Cost, bool HaveEnough);

    /// <summary>The 14 equip-slot icon widgets (index = e_ItemSlot).</summary>
    public IReadOnlyList<UiIconControl?> EquipIcons => _equip;

    /// <summary>The 28 backpack icon widgets (index = backpack cell 0..27).</summary>
    public IReadOnlyList<UiIconControl?> BackpackIcons => _backpack;

    // ---- Runtime icon construction (CUIInventory::InitIconUpdate) -----------

    private void BuildIcons()
    {
        for (int i = 0; i < Inventory.EquipSlotCount; i++)
        {
            _slotArea[i] = _root.GetChildAreaByOrder(UiAreaType.Slot, i);
            _equip[i] = MakeIcon(_slotArea[i]);
        }

        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            _invArea[i] = _root.GetChildAreaByOrder(UiAreaType.Inv, i);
            _backpack[i] = MakeIcon(_invArea[i]);
        }
    }

    private UiIconControl MakeIcon(UiAreaControl? area)
    {
        N3UiRect region = area?.Region ?? default;
        UiIconControl icon = UiIconControl.CreateRuntime(region);
        icon.DragState = _drag;
        icon.SetVisible(false);
        _root.AddChild(icon); // parent = window, so icon messages bubble to the root controller
        return icon;
    }

    // ---- Population --------------------------------------------------------

    /// <summary>
    /// Fill every slot from the model (CUIInventory item refresh): resolve the (basic, ext)
    /// rows, compute the icon file name, attach the payload and show the icon; empty slots hide.
    /// Also resets each icon to its home slot region (undoing any in-flight drag displacement).
    /// </summary>
    public void Populate(Inventory inv)
    {
        _inventory = inv;

        for (int flat = 0; flat < Inventory.InventorySlotCount; flat++)
        {
            UiIconControl? icon = IconAt(flat);
            if (icon == null)
                continue;

            if (SlotArea(flat) is { } area)
                icon.SetIconRegion(area.Region); // snap home

            InventoryItem? item = inv.Get(flat);
            if (item == null || item.ItemId == 0)
            {
                icon.SetVisible(false);
                icon.Payload = null;
                icon.IconTexture = string.Empty;
                icon.ItemSkillId = 0;
                icon.DurabilityExhausted = false;
                continue;
            }

            (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find((uint)item.ItemId);
            icon.IconTexture = ItemResourceNamer.MakeResourceFileName(basic, ext).IconFileName;
            icon.ItemSkillId = item.ItemId;
            icon.Payload = new InventoryIconItem(item.ItemId, item.Count, item.Durability, item.Flag, basic, ext);
            icon.DurabilityExhausted = item.Durability == 0 && (basic?.MaxDurability ?? 0) > 0;
            icon.SetVisible(true);
        }

        UpdateWeight();
    }

    /// <summary>
    /// Wire the in-world hooks. Additive (<c>+=</c>) so it does not clobber the state bar's own
    /// single-assignment <see cref="InGameState.MyInfoReceived"/> hook — both refresh on MyInfo.
    /// </summary>
    public void Bind(InGameState inGame)
    {
        _local = inGame.World.Local;
        _inventory = inGame.Inventory;
        inGame.MyInfoReceived += _ => Populate(inGame.Inventory);
    }

    /// <summary>CUIInventory::UpdateWeight — "cur / max" from the local player block.</summary>
    public void UpdateWeight()
    {
        if (_textWeight == null || _local == null)
            return;
        _textWeight.Text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}", _local.CurWeight, _local.MaxWeight);
    }

    // ---- Server reply (CUIInventory::ReceiveResultFromServer) ---------------

    /// <summary>
    /// The WIZ_ITEM_MOVE reply landed: on success commit the pending model move (swap-aware) and
    /// leave the icons at their new home; on failure the model is untouched. Either way the icons
    /// are authoritatively re-placed from the (now-correct) model, rolling back a failed optimistic
    /// move, and the drag lock is released.
    /// </summary>
    public void OnItemMoveResult(bool ok)
    {
        if (ok && _pendingSrcFlat >= 0 && _pendingDestFlat >= 0)
            _inventory?.MoveItem(_pendingSrcFlat, _pendingDestFlat);

        _pendingSrcFlat = -1;
        _pendingDestFlat = -1;
        _drag.WaitFromServer = false;
        _drag.Reset();

        if (_inventory != null)
            Populate(_inventory);
    }

    /// <summary>
    /// The item under a cursor point, for the hover tooltip (CUIInventory::Render highlight →
    /// CUIImageTooltipDlg). Returns the visible icon whose region contains the point, or null.
    /// Skips the icon currently being dragged.
    /// </summary>
    public InventoryIconItem? HoveredItem(UiPoint cursor)
    {
        if (!_root.Visible)
            return null;

        for (int flat = 0; flat < Inventory.InventorySlotCount; flat++)
        {
            UiIconControl? icon = IconAt(flat);
            if (icon is { Visible: true } && icon.Payload is InventoryIconItem item
                && !ReferenceEquals(icon, _drag.SelectedIcon.Icon)
                && UiRectMath.IsIn(icon.Region, cursor.X, cursor.Y))
                return item;
        }

        return null;
    }

    // ---- Message routing (CUIInventory::ReceiveMessage) ---------------------

    private void OnMessage(UiControl sender, uint msg)
    {
        // Repair mode (CItemRepairMgr::Tick): a click on an item repairs it; drag/equip are off.
        if (_mode == InventoryMode.Repair)
        {
            switch (msg)
            {
                case UiMsg.ButtonClick:
                    OnButton(sender); // btn_close still works
                    break;
                case UiMsg.IconDownFirst:
                    _repairPressIcon = sender as UiIconControl;
                    break;
                case UiMsg.IconUp:
                    if (sender is UiIconControl up && ReferenceEquals(up, _repairPressIcon))
                        DoRepairClick(up);
                    _repairPressIcon = null;
                    break;
            }

            return;
        }

        switch (msg)
        {
            case UiMsg.ButtonClick:
                OnButton(sender);
                break;

            case UiMsg.IconDownFirst:
                OnIconDownFirst(sender);
                break;

            case UiMsg.IconDown:
                // Drag-follow: the held icon tracks the cursor (CUIInventory GetSampleRect).
                _drag.SelectedIcon.Icon?.MoveToCursor(Cursor);
                break;

            case UiMsg.IconUp:
                OnIconUp();
                break;

            case UiMsg.IconRDownFirst:
                _rBtnDownIndex = sender is UiIconControl ri ? FindSlot(ri) : -1;
                break;

            case UiMsg.IconRUp:
                OnIconRUp(sender);
                break;

            case UiMsg.IconDblClk:
                RestoreSelected();
                break;
        }
    }

    private void OnButton(UiControl sender)
    {
        if (ReferenceEquals(sender, _btnClose))
            Hide();
        else if (ReferenceEquals(sender, _btnDestroyOk))
            ConfirmDestroy();
        else if (ReferenceEquals(sender, _btnDestroyCancel))
            CancelDestroy();
    }

    private void OnIconDownFirst(UiControl sender)
    {
        if (sender is not UiIconControl icon)
            return;
        int flat = FindSlot(icon);
        if (flat < 0 || icon.Payload is not InventoryIconItem item)
            return;

        bool isSlot = Inventory.IsEquipSlot(flat);
        _drag.SelectedIcon.Location = new UiWndIconInfo
        {
            Wnd = UiWnd.Inventory,
            District = isSlot ? UiWndDistrict.InventorySlot : UiWndDistrict.InventoryInv,
            Order = isSlot ? flat : flat - Inventory.EquipSlotCount,
        };
        _drag.SelectedIcon.Item = item;
        _drag.SelectedIcon.Icon = icon;
        icon.MoveToCursor(Cursor);
    }

    private void OnIconUp()
    {
        SelectedIconInfo sel = _drag.SelectedIcon;
        if (!sel.IsActive || sel.Item is not InventoryIconItem item || sel.Icon is not { } icon)
        {
            sel.Clear();
            return;
        }

        bool srcIsSlot = sel.Location.District == UiWndDistrict.InventorySlot;
        int srcOrder = sel.Location.Order;
        int srcFlat = srcIsSlot ? srcOrder : Inventory.EquipSlotCount + srcOrder;

        // Dropped back on its own slot → no-op restore.
        if (AreaFor(srcIsSlot, srcOrder) is { } srcArea && srcArea.IsIn(Cursor.X, Cursor.Y))
        {
            SnapIconToSlot(icon, srcFlat);
            sel.Clear();
            return;
        }

        // area_samma → destroy confirm (real destroy packet deferred).
        if (_areaDestroy != null && _areaDestroy.IsIn(Cursor.X, Cursor.Y))
        {
            OpenDestroyConfirm(item, srcFlat, icon);
            return;
        }

        if (!ResolveDest(item, srcIsSlot, out bool destIsSlot, out int destOrder))
        {
            SnapIconToSlot(icon, srcFlat); // no valid target → restore
            sel.Clear();
            return;
        }

        Commit(item, srcIsSlot, srcOrder, destIsSlot, destOrder, icon);
    }

    private void OnIconRUp(UiControl sender)
    {
        if (sender is not UiIconControl icon)
            return;
        int flat = FindSlot(icon);
        if (flat < 0 || flat != _rBtnDownIndex || icon.Payload is not InventoryIconItem item)
            return;

        bool srcIsSlot = Inventory.IsEquipSlot(flat);
        int srcOrder = srcIsSlot ? flat : flat - Inventory.EquipSlotCount;

        _drag.SelectedIcon.Location = new UiWndIconInfo
        {
            Wnd = UiWnd.Inventory,
            District = srcIsSlot ? UiWndDistrict.InventorySlot : UiWndDistrict.InventoryInv,
            Order = srcOrder,
        };
        _drag.SelectedIcon.Item = item;
        _drag.SelectedIcon.Icon = icon;

        // Equipped → unequip into the first empty backpack cell; backpack → quick-equip.
        bool destIsSlot;
        int destOrder;
        if (srcIsSlot)
        {
            destIsSlot = false;
            destOrder = FirstEmptyBackpack();
        }
        else
        {
            destIsSlot = true;
            destOrder = GetArmDestinationIndex(item);
        }

        if (destOrder < 0)
        {
            _drag.SelectedIcon.Clear();
            return;
        }

        Commit(item, srcIsSlot, srcOrder, destIsSlot, destOrder, icon);
    }

    // ---- Move commit (CheckIconDropIfSuccessSendToServer / SendInvMsg) ------

    private void Commit(
        InventoryIconItem item, bool srcIsSlot, int srcOrder, bool destIsSlot, int destOrder, UiIconControl icon)
    {
        int srcFlat = srcIsSlot ? srcOrder : Inventory.EquipSlotCount + srcOrder;
        int destFlat = destIsSlot ? destOrder : Inventory.EquipSlotCount + destOrder;
        if (destFlat == srcFlat)
        {
            SnapIconToSlot(icon, srcFlat);
            _drag.SelectedIcon.Clear();
            return;
        }

        ItemMoveDirection dir = (srcIsSlot, destIsSlot) switch
        {
            (true, true) => ItemMoveDirection.SlotToSlot,           // 0x04
            (true, false) => ItemMoveDirection.SlotToInventory,     // 0x02
            (false, true) => ItemMoveDirection.InventoryToSlot,     // 0x01
            (false, false) => ItemMoveDirection.InventoryToInventory, // 0x03
        };

        // Lock input + record the recovery job (source + destination addresses).
        _drag.WaitFromServer = true;
        RecoveryJobInfo job = _drag.RecoveryJob;
        job.Clear();
        job.ItemSource = item;
        job.SourceStart = _drag.SelectedIcon.Location;
        job.SourceEnd = new UiWndIconInfo
        {
            Wnd = UiWnd.Inventory,
            District = destIsSlot ? UiWndDistrict.InventorySlot : UiWndDistrict.InventoryInv,
            Order = destOrder,
        };
        _pendingSrcFlat = srcFlat;
        _pendingDestFlat = destFlat;

        // The wire src/dest are district-relative orders (the direction byte carries the district).
        _context.Client.Send(ItemProtocol.BuildItemMove(dir, item.ItemId, (byte)srcOrder, (byte)destOrder));

        // Optimistic icon snap to the destination (authoritatively re-placed on the reply).
        SnapIconToSlot(icon, destFlat);
        _drag.SelectedIcon.Clear();
    }

    private bool ResolveDest(InventoryIconItem item, bool srcIsSlot, out bool destIsSlot, out int destOrder)
    {
        for (int i = 0; i < Inventory.EquipSlotCount; i++)
        {
            if (_slotArea[i]?.IsIn(Cursor.X, Cursor.Y) == true)
            {
                destIsSlot = true;
                destOrder = i;
                return true;
            }
        }

        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            if (_invArea[i]?.IsIn(Cursor.X, Cursor.Y) == true)
            {
                destIsSlot = false;
                destOrder = i;
                return true;
            }
        }

        // Dropped on the paperdoll region → auto-pick an equip slot (backpack source only).
        if (!srcIsSlot && _areaChar?.IsIn(Cursor.X, Cursor.Y) == true)
        {
            int arm = GetArmDestinationIndex(item);
            if (arm >= 0)
            {
                destIsSlot = true;
                destOrder = arm;
                return true;
            }
        }

        destIsSlot = false;
        destOrder = -1;
        return false;
    }

    /// <summary>
    /// CUIInventory::GetArmDestinationIndex — the equip slot an item wants, from its attach point
    /// and the current hand/ear/finger occupancy. Returns -1 when it cannot be worn (e.g. a
    /// two-hander with both hands full).
    /// </summary>
    private int GetArmDestinationIndex(InventoryIconItem item)
    {
        if (item.Basic == null)
            return -1;

        bool right = Occupied(EquipSlot.HandRight);
        bool left = Occupied(EquipSlot.HandLeft);

        switch (item.Basic.AttachPoint)
        {
            case KoItemPosition.Dual:
                if (right && left)
                    return (int)EquipSlot.HandRight;
                if (!right)
                    return (int)EquipSlot.HandRight;
                return RightHandIsTwoHander() ? (int)EquipSlot.HandRight : (int)EquipSlot.HandLeft;

            case KoItemPosition.RightHand:
                return (int)EquipSlot.HandRight;

            case KoItemPosition.LeftHand:
                return (int)EquipSlot.HandLeft;

            case KoItemPosition.TwoHandRight:
                return right && left ? -1 : (int)EquipSlot.HandRight;

            case KoItemPosition.TwoHandLeft:
                return right && left ? -1 : (int)EquipSlot.HandLeft;

            case KoItemPosition.Ear:
                if (!Occupied(EquipSlot.EarRight))
                    return (int)EquipSlot.EarRight;
                if (!Occupied(EquipSlot.EarLeft))
                    return (int)EquipSlot.EarLeft;
                return (int)EquipSlot.EarRight;

            case KoItemPosition.Head:
                return (int)EquipSlot.Head;

            case KoItemPosition.Neck:
                return (int)EquipSlot.Neck;

            case KoItemPosition.Upper:
                return (int)EquipSlot.Upper;

            case KoItemPosition.Shoulder: // cloak → shoulder
                return (int)EquipSlot.Shoulder;

            case KoItemPosition.Belt:
                return (int)EquipSlot.Belt;

            case KoItemPosition.Finger:
                if (!Occupied(EquipSlot.RingRight))
                    return (int)EquipSlot.RingRight;
                if (!Occupied(EquipSlot.RingLeft))
                    return (int)EquipSlot.RingLeft;
                return (int)EquipSlot.RingRight;

            case KoItemPosition.Lower:
                return (int)EquipSlot.Lower;

            case KoItemPosition.Gloves: // arm → gloves
                return (int)EquipSlot.Gloves;

            case KoItemPosition.Shoes:
                return (int)EquipSlot.Shoes;

            default:
                return -1;
        }
    }

    private bool RightHandIsTwoHander()
    {
        InventoryItem? held = _inventory?.Get((int)EquipSlot.HandRight);
        if (held == null)
            return false;
        (ItemBasicRow? basic, _) = _items.Find((uint)held.ItemId);
        return basic?.AttachPoint == KoItemPosition.TwoHandRight;
    }

    private bool Occupied(EquipSlot slot) => _inventory?.Get((int)slot) != null;

    private int FirstEmptyBackpack()
    {
        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            if (_inventory?.Get(Inventory.BackpackIndex(i)) == null)
                return i;
        }

        return -1;
    }

    // ---- Destroy (area_samma) ----------------------------------------------

    private void OpenDestroyConfirm(InventoryIconItem item, int flat, UiIconControl icon)
    {
        _destroyItem = item;
        _destroyFlat = flat;
        if (_areaDestroy != null)
            icon.SetIconRegion(_areaDestroy.Region); // park it over the destroy area
        _drag.SelectedIcon.Clear();
    }

    /// <summary>
    /// Deferred: the real WIZ_ITEM_TRADE/destroy packet lands in a later slice. For now the
    /// confirm just clears the pending state and re-places the icon from the (unchanged) model.
    /// </summary>
    private void ConfirmDestroy()
    {
        _destroyItem = null;
        _destroyFlat = -1;
        if (_inventory != null)
            Populate(_inventory);
    }

    private void CancelDestroy()
    {
        if (_destroyFlat >= 0 && IconAt(_destroyFlat) is { } icon)
            SnapIconToSlot(icon, _destroyFlat);
        _destroyItem = null;
        _destroyFlat = -1;
    }

    // ---- Helpers -----------------------------------------------------------

    private void RestoreSelected()
    {
        SelectedIconInfo sel = _drag.SelectedIcon;
        if (sel.Icon is { } icon)
        {
            bool isSlot = sel.Location.District == UiWndDistrict.InventorySlot;
            int flat = isSlot ? sel.Location.Order : Inventory.EquipSlotCount + sel.Location.Order;
            SnapIconToSlot(icon, flat);
        }

        sel.Clear();
    }

    private int FindSlot(UiIconControl icon)
    {
        for (int i = 0; i < Inventory.EquipSlotCount; i++)
        {
            if (ReferenceEquals(_equip[i], icon))
                return i;
        }

        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            if (ReferenceEquals(_backpack[i], icon))
                return Inventory.EquipSlotCount + i;
        }

        return -1;
    }

    private UiIconControl? IconAt(int flat) =>
        Inventory.IsEquipSlot(flat) ? _equip[flat] : _backpack[flat - Inventory.EquipSlotCount];

    private UiAreaControl? SlotArea(int flat) =>
        Inventory.IsEquipSlot(flat) ? _slotArea[flat] : _invArea[flat - Inventory.EquipSlotCount];

    private UiAreaControl? AreaFor(bool isSlot, int order) =>
        isSlot ? _slotArea[order] : _invArea[order];

    private void SnapIconToSlot(UiIconControl icon, int flat)
    {
        if (SlotArea(flat) is { } area)
            icon.SetIconRegion(area.Region);
    }

    // ---- Repair mode (CItemRepairMgr) --------------------------------------

    /// <summary>
    /// CItemRepairMgr::Tick (display half) — while in repair mode, show the repair price tooltip
    /// for the item under the cursor (red when unaffordable), or hide it. No-op without a bound
    /// <see cref="RepairTooltip"/> or outside repair mode.
    /// </summary>
    public void TickRepair(UiPoint cursor)
    {
        if (RepairTooltip is not { } tip)
            return;

        if (RepairHover(cursor) is { } info)
            tip.Show(info.Item.Basic, info.Item.Ext, info.Item.Durability, info.Cost, info.HaveEnough, cursor.X, cursor.Y);
        else
            tip.Hide();
    }

    /// <summary>
    /// The item under the cursor and its computed repair price (CItemRepairMgr::CalcRepairGold),
    /// or null when not in repair mode / not hovering a repairable item. Equipped slots are
    /// probed first (arm 0x01), then backpack cells (arm 0x02), mirroring the C++ scan order.
    /// </summary>
    public RepairHoverInfo? RepairHover(UiPoint cursor)
    {
        if (_mode != InventoryMode.Repair || !_root.Visible)
            return null;

        for (int i = 0; i < Inventory.EquipSlotCount; i++)
        {
            if (RepairAt(_equip[i], i, cursor) is { } info)
                return info;
        }

        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
        {
            if (RepairAt(_backpack[i], Inventory.EquipSlotCount + i, cursor) is { } info)
                return info;
        }

        return null;
    }

    private RepairHoverInfo? RepairAt(UiIconControl? icon, int flat, UiPoint cursor)
    {
        if (icon is not { Visible: true } || icon.Payload is not InventoryIconItem item
            || !UiRectMath.IsIn(icon.Region, cursor.X, cursor.Y))
            return null;
        return RepairFor(item, flat);
    }

    private RepairHoverInfo? RepairFor(InventoryIconItem item, int flat)
    {
        if (item.Basic == null)
            return null;

        int maxDur = item.Basic.MaxDurability + (item.Ext?.MaxDurability ?? 0);
        float allPrice = item.Basic.Price * (float)(item.Ext?.PriceMultiply ?? 0);
        int cost = RepairCost.Calc(allPrice, item.Durability, maxDur);

        byte arm = Inventory.IsEquipSlot(flat) ? RepairProtocol.ArmEquip : RepairProtocol.ArmInventory;
        int order = Inventory.IsEquipSlot(flat) ? flat : flat - Inventory.EquipSlotCount;
        bool haveEnough = _local == null || cost <= _local.Gold;
        return new RepairHoverInfo(item, arm, order, cost, haveEnough);
    }

    /// <summary>
    /// CItemRepairMgr::Tick (click half) — a completed left-click on a repairable item. With a
    /// positive price and enough gold, sends WIZ_ITEM_REPAIR and locks input until the reply;
    /// with too little gold, raises <see cref="RepairLackGold"/> and sends nothing.
    /// </summary>
    private void DoRepairClick(UiIconControl icon)
    {
        int flat = FindSlot(icon);
        if (flat < 0 || icon.Payload is not InventoryIconItem item || RepairFor(item, flat) is not { } repair)
            return;

        if (repair.Cost <= 0)
            return;

        if (!repair.HaveEnough)
        {
            RepairLackGold?.Invoke();
            return;
        }

        _repairPendingFlat = flat;
        _drag.WaitFromServer = true;
        _context.Client.Send(RepairProtocol.BuildRepair(repair.Arm, (byte)repair.Order, (uint)repair.Item.ItemId));
    }

    /// <summary>
    /// CItemRepairMgr::ReceiveResultFromServer — on success restore the repaired item to full
    /// durability, clear its UISTYLE_DURABILITY_EXHAUST icon style and repopulate; always update
    /// the player's gold and release the input lock.
    /// </summary>
    public void OnRepairResult(RepairResult result)
    {
        if (result.Success && _repairPendingFlat >= 0 && _inventory?.Get(_repairPendingFlat) is { } item)
        {
            (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find((uint)item.ItemId);
            short maxDur = (short)((basic?.MaxDurability ?? 0) + (ext?.MaxDurability ?? 0));
            _inventory.SetDurability(_repairPendingFlat, maxDur);
        }

        if (_local != null)
            _local.Gold = (int)result.Gold;

        _repairPendingFlat = -1;
        _drag.WaitFromServer = false;
        if (_inventory != null)
            Populate(_inventory);
    }

    // ---- Show/hide ---------------------------------------------------------

    /// <summary>
    /// CUIInventory::Open — show the window in the requested sub-state (normal, or the
    /// blacksmith repair mode entered from the NPC-event menu's Btn_Repair).
    /// </summary>
    public void Open(bool repair = false)
    {
        _mode = repair ? InventoryMode.Repair : InventoryMode.Normal;
        _repairPressIcon = null;
        _drag.SelectedIcon.Clear();
        _root.SetVisible(true);
        if (_inventory != null)
            Populate(_inventory);
    }

    public void Show()
    {
        _mode = InventoryMode.Normal;
        _root.SetVisible(true);
    }

    public void Hide()
    {
        _mode = InventoryMode.Normal;
        _root.SetVisible(false);
    }

    public void Toggle()
    {
        _mode = InventoryMode.Normal;
        _root.SetVisible(!_root.Visible);
    }
}
