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
/// The WIZ_ITEM_MOVE 0x01 reply's recomputed stat blob (CGameProcMain::MsgRecv_ItemMove,
/// GameProcMain.cpp:3267 / GameUser.Items.cs SendItemMoveStats). All five leading fields are
/// int16; the five stat deltas and six resists are uint16. <see cref="Success"/> is false for
/// a 0x00 rejection (every stat field then zero).
/// </summary>
public readonly record struct ItemMoveResult(
    bool Success,
    short Attack,
    short Guard,
    short WeightMax,
    short HpMax,
    short MspMax,
    ushort StrDelta,
    ushort StaDelta,
    ushort DexDelta,
    ushort IntDelta,
    ushort MagicAttackDelta,
    ushort ResistFire,
    ushort ResistCold,
    ushort ResistLight,
    ushort ResistMagic,
    ushort ResistCurse,
    ushort ResistPoison);

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

    /// <summary>
    /// Parse the WIZ_ITEM_MOVE reply (CGameProcMain::MsgRecv_ItemMove). On a 0x01 result the
    /// server appends the recomputed attack/guard/weightMax/hpMax/mspMax (int16), the five stat
    /// deltas and the six resistances (uint16); a 0x00 result carries no stats.
    /// </summary>
    public static ItemMoveResult ParseItemMoveResult(ReadOnlySpan<byte> payload)
    {
        // opcode + result + 16 int16/uint16 stat fields = 34 bytes on a 0x01 reply.
        const int fullLength = 2 + (16 * 2);

        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte result = r.GetByte();
        if (result != 0x01 || payload.Length < fullLength)
            return new ItemMoveResult(result == 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        short attack = r.GetShort();
        short guard = r.GetShort();
        short weightMax = r.GetShort();
        short hpMax = r.GetShort();
        short mspMax = r.GetShort();

        ushort str = (ushort)r.GetShort();
        ushort sta = (ushort)r.GetShort();
        ushort dex = (ushort)r.GetShort();
        ushort intel = (ushort)r.GetShort();
        ushort magicAttack = (ushort)r.GetShort();

        ushort resistFire = (ushort)r.GetShort();
        ushort resistCold = (ushort)r.GetShort();
        ushort resistLight = (ushort)r.GetShort();
        ushort resistMagic = (ushort)r.GetShort();
        ushort resistCurse = (ushort)r.GetShort();
        ushort resistPoison = (ushort)r.GetShort();

        return new ItemMoveResult(
            true, attack, guard, weightMax, hpMax, mspMax,
            str, sta, dex, intel, magicAttack,
            resistFire, resistCold, resistLight, resistMagic, resistCurse, resistPoison);
    }
}
