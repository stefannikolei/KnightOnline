namespace OpenKO.Client.Game.World;

/// <summary>One inventory/equipment slot occupant.</summary>
public sealed record InventoryItem(int ItemId, int Count, short Durability);

/// <summary>
/// The client-side inventory (the CUser item array): a flat position map over
/// the equipment slots and the backpack. The initial fill comes from the full
/// WIZ_MYINFO item block (a later slice); here the model supports the local
/// move/swap the drag-and-drop UI performs and the server confirms.
/// </summary>
public sealed class Inventory
{
    private readonly Dictionary<int, InventoryItem> _slots = [];

    public IReadOnlyDictionary<int, InventoryItem> Slots => _slots;

    public InventoryItem? Get(int position) => _slots.GetValueOrDefault(position);

    public void Set(int position, InventoryItem item) => _slots[position] = item;

    public bool Remove(int position) => _slots.Remove(position);

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
