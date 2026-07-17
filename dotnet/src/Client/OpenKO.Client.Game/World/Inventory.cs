namespace OpenKO.Client.Game.World;

/// <summary>
/// The fourteen equipment positions in WIZ_MYINFO / inventory wire order
/// (ITEM_SLOT_POS_* in UIInventory.h). The value is the flat slot index inside
/// the equipment band (0..13) of the item array.
/// </summary>
public enum EquipSlot : byte
{
    EarRight = 0,
    Head = 1,
    EarLeft = 2,
    Neck = 3,
    Upper = 4,
    Shoulder = 5,
    HandRight = 6,
    Belt = 7,
    HandLeft = 8,
    RingRight = 9,
    Lower = 10,
    RingLeft = 11,
    Gloves = 12,
    Shoes = 13,
}

/// <summary>
/// One inventory/equipment slot occupant. <see cref="Flag"/> (rental/bound) and
/// <see cref="TimeRemaining"/> (rental seconds left) come straight off the wire —
/// they were previously read and discarded.
/// </summary>
public sealed record InventoryItem(
    int ItemId,
    int Count,
    short Durability,
    byte Flag = 0,
    short TimeRemaining = 0);

/// <summary>
/// The client-side inventory (the CUser item array): a flat position map over the
/// fourteen equipment slots (index 0..13) and the twenty-eight backpack slots
/// (index 14..41). The initial fill comes from the full WIZ_MYINFO item block; the
/// model supports the local move/swap the drag-and-drop UI performs and the server
/// confirms, plus count/durability mutation for stack-splits and repair views.
/// </summary>
public sealed class Inventory
{
    /// <summary>SLOT_MAX — the fourteen equipment positions (index 0..13).</summary>
    public const int EquipSlotCount = 14;

    /// <summary>HAVE_MAX — the twenty-eight backpack positions (index 14..41).</summary>
    public const int BackpackSlotCount = 28;

    /// <summary>SLOT_MAX + HAVE_MAX — the full item array length.</summary>
    public const int InventorySlotCount = EquipSlotCount + BackpackSlotCount;

    private readonly Dictionary<int, InventoryItem> _slots = [];

    public IReadOnlyDictionary<int, InventoryItem> Slots => _slots;

    /// <summary>True when <paramref name="index"/> addresses an equipment slot (0..13).</summary>
    public static bool IsEquipSlot(int index) => index is >= 0 and < EquipSlotCount;

    /// <summary>True when <paramref name="index"/> addresses a backpack slot (14..41).</summary>
    public static bool IsBackpackSlot(int index) =>
        index >= EquipSlotCount && index < InventorySlotCount;

    /// <summary>The flat item-array index of a backpack cell (0..27 → 14..41).</summary>
    public static int BackpackIndex(int backpackCell) => EquipSlotCount + backpackCell;

    public InventoryItem? Get(int position) => _slots.GetValueOrDefault(position);

    /// <summary>
    /// CUIInventory::GetCountInInvByID — the total stack count held for an item id across the
    /// inventory (the hotkey bar's per-slot consumable count and the cast exhaust-item gate read
    /// this). Ids are full item ids (base*1000 + ext), so an exact <see cref="InventoryItem.ItemId"/>
    /// match is equivalent to the C++ base/ext split compare. Returns 0 when the item is absent.
    /// </summary>
    public int CountById(int itemId)
    {
        if (itemId == 0)
            return 0;

        int total = 0;
        foreach (InventoryItem item in _slots.Values)
        {
            if (item.ItemId == itemId)
                total += item.Count;
        }

        return total;
    }

    /// <summary>The item worn in an equipment slot, or null.</summary>
    public InventoryItem? EquipItem(EquipSlot slot) => _slots.GetValueOrDefault((int)slot);

    /// <summary>The item in a backpack cell (0..27), or null.</summary>
    public InventoryItem? BackpackItem(int backpackCell) =>
        _slots.GetValueOrDefault(BackpackIndex(backpackCell));

    public void Set(int position, InventoryItem item) => _slots[position] = item;

    public bool Remove(int position) => _slots.Remove(position);

    /// <summary>Clears a slot (alias for <see cref="Remove"/>, reads as intent).</summary>
    public bool Clear(int position) => _slots.Remove(position);

    /// <summary>
    /// Replaces the stack size at <paramref name="position"/>. Returns false when the
    /// slot is empty. A non-positive count clears the slot.
    /// </summary>
    public bool SetCount(int position, int count)
    {
        if (!_slots.TryGetValue(position, out InventoryItem? item))
            return false;

        if (count <= 0)
            return _slots.Remove(position);

        _slots[position] = item with { Count = count };
        return true;
    }

    /// <summary>
    /// Replaces the durability at <paramref name="position"/> (repair/wear). Returns
    /// false when the slot is empty.
    /// </summary>
    public bool SetDurability(int position, short durability)
    {
        if (!_slots.TryGetValue(position, out InventoryItem? item))
            return false;

        _slots[position] = item with { Durability = durability };
        return true;
    }

    /// <summary>
    /// Moves the item at <paramref name="source"/> to <paramref name="destination"/>;
    /// swaps when the destination is occupied. Returns false if the source is empty.
    /// </summary>
    public bool MoveItem(int source, int destination)
    {
        if (source == destination || !_slots.TryGetValue(source, out InventoryItem? moving))
            return false;

        if (_slots.TryGetValue(destination, out InventoryItem? occupant))
            _slots[source] = occupant;
        else
            _slots.Remove(source);

        _slots[destination] = moving;
        return true;
    }
}
