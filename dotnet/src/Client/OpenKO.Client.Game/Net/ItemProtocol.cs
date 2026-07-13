using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>The four e_ItemMoveDirection modes (CUser::ItemMove).</summary>
public enum ItemMoveDirection : byte
{
    InventoryToSlot = 1, // equip
    SlotToInventory = 2, // unequip
    InventoryToInventory = 3,
    SlotToSlot = 4,
}

/// <summary>
/// The client item packets (CUser::ItemMove): the WIZ_ITEM_MOVE request and its
/// result byte. Field order is pinned against the C# Ebenezer (dir, item id,
/// source, destination).
/// </summary>
public static class ItemProtocol
{
    public static byte[] BuildItemMove(ItemMoveDirection dir, int itemId, byte srcPos, byte destPos)
    {
        var buffer = new byte[8];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ITEM_MOVE);
        w.SetByte((byte)dir);
        w.SetDWord((uint)itemId);
        w.SetByte(srcPos);
        w.SetByte(destPos);
        return w.Written.ToArray();
    }

    /// <summary>WIZ_ITEM_MOVE reply: 0x00 = rejected, 0x01 = applied (stat blob follows).</summary>
    public static bool ParseItemMoveSucceeded(ReadOnlySpan<byte> payload) => payload.Length >= 2 && payload[1] == 0x01;
}
