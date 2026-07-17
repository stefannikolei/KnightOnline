using System.Globalization;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the dropped-item (loot box) dialog — port of <c>CUIDroppedItemDlg</c>
/// (Client/WarFare/UIDroppedItemDlg.cpp, UIWND_DROPITEM). The shipped <c>*_droppeditem_*.uif</c>
/// carries six <c>UI_AREA_TYPE_DROP_ITEM</c> slot regions (order 0..5) and six count labels
/// (string ids "0".."5"); the six loot icons are created at runtime and placed at each slot's
/// region (<c>InitIconUpdate</c>). <see cref="Populate"/> fills the rows from a parsed loot
/// bundle; picking a row up and releasing it (<c>UIMSG_ICON_UP</c>) sends
/// <c>WIZ_ITEM_GET</c> once per row, and <see cref="OnGetResult"/> routes a successful pickup
/// into the inventory and clears the taken row, hiding the dialog once empty. Pure/headless.
/// </summary>
public sealed class DroppedItemDialog
{
    /// <summary>MAX_ITEM_BUNDLE_DROP_PIECE — the six loot rows.</summary>
    public const int MaxPieces = ItemProtocol.MaxBundlePieces;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly ItemTableSet _items;
    private readonly IconDragState _drag;

    private readonly UiAreaControl?[] _area = new UiAreaControl?[MaxPieces];
    private readonly UiIconControl?[] _icon = new UiIconControl?[MaxPieces];
    private readonly UiStringControl?[] _count = new UiStringControl?[MaxPieces];
    private readonly DroppedIconItem?[] _row = new DroppedIconItem?[MaxPieces];
    private readonly bool[] _sent = new bool[MaxPieces];

    private uint _bundleId;
    private int _downOrder = -1;

    /// <summary>The opaque per-row payload (the __IconItemSkill analog for loot).</summary>
    public sealed record DroppedIconItem(
        uint ItemId, int Count, short Durability, ItemBasicRow Basic, ItemExtRow Ext);

    public DroppedItemDialog(GameContext context, UiControl root, ItemTableSet items, IconDragState drag)
    {
        _context = context;
        _root = root;
        _items = items;
        _drag = drag;

        for (int i = 0; i < MaxPieces; i++)
        {
            _area[i] = root.GetChildAreaByOrder(UiAreaType.DropItem, i);
            _icon[i] = MakeIcon(_area[i]);
            _count[i] = root.GetChildById<UiStringControl>(i.ToString(CultureInfo.InvariantCulture));
        }

        root.Message += OnMessage;
        _root.SetVisible(false);
    }

    /// <summary>The runtime dialog root (registered with the UI manager).</summary>
    public UiControl Root => _root;

    /// <summary>The live cursor position, fed by the executable each frame (tests set it directly).</summary>
    public UiPoint Cursor { get; set; }

    /// <summary>The six loot-row icon widgets (index = slot order 0..5).</summary>
    public IReadOnlyList<UiIconControl?> Icons => _icon;

    /// <summary>The bundle id the currently displayed loot belongs to.</summary>
    public uint BundleId => _bundleId;

    /// <summary>
    /// Raised after <see cref="OnGetResult"/> mutates the inventory model, so the executable can
    /// repopulate the inventory dialog through its own populate path.
    /// </summary>
    public Action? InventoryChanged { get; set; }

    private UiIconControl MakeIcon(UiAreaControl? area)
    {
        N3UiRect region = area?.Region ?? default;
        UiIconControl icon = UiIconControl.CreateRuntime(region);
        icon.DragState = _drag;
        icon.SetVisible(false);
        _root.AddChild(icon);
        return icon;
    }

    // ---- Population --------------------------------------------------------

    /// <summary>
    /// Fill the loot rows from a parsed bundle (EnterDroppedState + AddToItemTable +
    /// InitIconUpdate): resolve each item, place its icon at the slot region, attach the payload
    /// and show the count label for stackables. Clears the send guards and shows the dialog.
    /// </summary>
    public void Populate(uint bundleId, IReadOnlyList<LootItem> items)
    {
        _bundleId = bundleId;
        for (int i = 0; i < MaxPieces; i++)
            ClearRow(i);

        foreach (LootItem loot in items)
        {
            if (loot.Slot < 0 || loot.Slot >= MaxPieces)
                continue;

            (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find(loot.ItemId);
            if (basic == null || ext == null)
                continue;

            short durability = (short)(basic.MaxDurability + ext.MaxDurability);
            var row = new DroppedIconItem(loot.ItemId, loot.Count, durability, basic, ext);
            _row[loot.Slot] = row;

            UiIconControl? icon = _icon[loot.Slot];
            if (icon != null)
            {
                if (_area[loot.Slot] is { } area)
                    icon.SetIconRegion(area.Region);
                icon.IconTexture = ItemResourceNamer.MakeResourceFileName(basic, ext).IconFileName;
                icon.ItemSkillId = (int)loot.ItemId;
                icon.Payload = row;
                icon.SetVisible(true);
            }

            UpdateCountLabel(loot.Slot);
        }

        _root.SetVisible(true);
    }

    /// <summary>Send the loot-list request for a bundle and (on the reply) this dialog populates.</summary>
    public void RequestOpen(uint bundleId) => _context.InGame.SendBundleOpen(bundleId);

    // ---- Take item (UIMSG_ICON_UP → WIZ_ITEM_GET) --------------------------

    private void OnMessage(UiControl sender, uint msg)
    {
        switch (msg)
        {
            case UiMsg.IconDownFirst:
                _downOrder = sender is UiIconControl di ? FindSlot(di) : -1;
                break;

            case UiMsg.IconUp:
                OnIconUp(sender);
                break;
        }
    }

    private void OnIconUp(UiControl sender)
    {
        if (sender is not UiIconControl icon)
            return;

        int order = FindSlot(icon);
        if (order < 0 || order != _downOrder || _sent[order] || _row[order] is not { } row)
            return;

        _sent[order] = true;

        bool isGold = row.Basic.AttachPoint == KoItemPosition.Gold || row.ItemId == ItemProtocol.GoldItemId;
        uint sendId = isGold ? row.Basic.Id : row.ItemId;
        _context.Client.Send(ItemProtocol.BuildItemGet(_bundleId, sendId));
    }

    // ---- Pickup result (GetItemByIDToInventory) ----------------------------

    /// <summary>
    /// Route a WIZ_ITEM_GET reply: on a solo/rule pickup (result 1/5) drop the item into the
    /// inventory model at the server-assigned position and clear the taken row; gold pickups just
    /// clear the gold row. Hides the dialog once no rows remain. Party-distribution variants
    /// (2/3/4) and the failure/full notices (0/6/7) only clear rows / are surfaced elsewhere.
    /// </summary>
    public void OnGetResult(ItemGetResult result)
    {
        switch (result.Result)
        {
            case 0x01:
            case 0x05:
                if (result.ItemId == ItemProtocol.GoldItemId || result.ItemId == 0)
                {
                    ClearRowByItemId(ItemProtocol.GoldItemId);
                }
                else
                {
                    RouteToInventory(result.Pos, result.ItemId, result.Count);
                    ClearRowByItemId(result.ItemId);
                    InventoryChanged?.Invoke();
                }

                break;

            case 0x02: // party gold
                ClearRowByItemId(ItemProtocol.GoldItemId);
                break;

            case 0x03: // party member pickup
            case 0x04: // party other pickup
                ClearRowByItemId(result.ItemId);
                break;
        }

        if (!AnyRows())
            Hide();
    }

    private void RouteToInventory(byte pos, uint itemId, int count)
    {
        if (pos >= Inventory.BackpackSlotCount)
            return;

        (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find(itemId);
        short durability = (short)((basic?.MaxDurability ?? 0) + (ext?.MaxDurability ?? 0));
        _context.InGame.Inventory.Set(Inventory.BackpackIndex(pos), new InventoryItem((int)itemId, count, durability));
    }

    // ---- Helpers -----------------------------------------------------------

    private void UpdateCountLabel(int order)
    {
        UiStringControl? label = _count[order];
        if (label == null)
            return;

        DroppedIconItem? row = _row[order];
        if (row != null && row.Basic.Countable)
        {
            label.Text = row.Count.ToString(CultureInfo.InvariantCulture);
            label.SetVisible(true);
        }
        else
        {
            label.SetVisible(false);
        }
    }

    private void ClearRow(int order)
    {
        _row[order] = null;
        _sent[order] = false;
        if (_icon[order] is { } icon)
        {
            icon.SetVisible(false);
            icon.Payload = null;
            icon.IconTexture = string.Empty;
            icon.ItemSkillId = 0;
        }

        _count[order]?.SetVisible(false);
    }

    private void ClearRowByItemId(uint itemId)
    {
        for (int i = 0; i < MaxPieces; i++)
        {
            if (_row[i] is { } row && (row.ItemId == itemId || row.Basic.Id == itemId))
            {
                ClearRow(i);
                return;
            }
        }
    }

    private bool AnyRows()
    {
        for (int i = 0; i < MaxPieces; i++)
        {
            if (_row[i] != null)
                return true;
        }

        return false;
    }

    private int FindSlot(UiIconControl icon)
    {
        for (int i = 0; i < MaxPieces; i++)
        {
            if (ReferenceEquals(_icon[i], icon))
                return i;
        }

        return -1;
    }

    // ---- Show/hide ---------------------------------------------------------

    public void Show() => _root.SetVisible(true);

    public void Hide()
    {
        _root.SetVisible(false);
        for (int i = 0; i < MaxPieces; i++)
            _sent[i] = false;
    }
}
