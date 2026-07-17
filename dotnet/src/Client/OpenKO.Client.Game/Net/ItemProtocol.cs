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

/// <summary>One dropped-loot slot (CUIDroppedItemDlg::AddToItemTable order i).</summary>
public readonly record struct LootItem(int Slot, uint ItemId, int Count);

/// <summary>
/// The parsed WIZ_BUNDLE_OPEN_REQ reply (MsgRecv_ItemBundleOpen): up to six loot
/// pieces, tagged with the pending bundle id the client requested. The bundle id is
/// not on the wire — the client remembers it from the open request.
/// </summary>
public readonly record struct LootBundle(uint BundleId, IReadOnlyList<LootItem> Items);

/// <summary>
/// The parsed WIZ_ITEM_GET reply (MsgRecv_ItemDroppedGetResult). <see cref="Result"/>
/// is 0 fail/full, 1 solo pickup (→ inventory), 2 party gold, 3 party member pickup,
/// 4 party other, 5 party rule pickup, 6 too-heavy, 7 inventory full. The optional
/// <see cref="Pos"/>/<see cref="ItemId"/>/<see cref="Count"/>/<see cref="GoldId"/> are
/// only present for results 1/2/5; <see cref="CharacterName"/> only for result 3.
/// </summary>
public readonly record struct ItemGetResult(
    byte Result, byte Pos, uint ItemId, int Count, uint GoldId, string CharacterName);

/// <summary>
/// The client item packets (CUser::ItemMove): the WIZ_ITEM_MOVE request and its
/// result byte. Field order is pinned against the C# Ebenezer (dir, item id,
/// source, destination).
/// </summary>
public static class ItemProtocol
{
    /// <summary>MAX_ITEM_BUNDLE_DROP_PIECE — the six slots a dropped bundle carries.</summary>
    public const int MaxBundlePieces = 6;

    /// <summary>The special gold item id (SubProcPerTrade.h dwGold).</summary>
    public const uint GoldItemId = 900000000;

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

    /// <summary>
    /// CGameProcMain::MsgSend_RequestItemBundleOpen — ask the server for a corpse/box's
    /// loot list: <c>[WIZ_BUNDLE_OPEN_REQ=0x24][u32 bundleId]</c> (5 bytes).
    /// </summary>
    public static byte[] BuildBundleOpenRequest(uint bundleId)
    {
        var buffer = new byte[5];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_BUNDLE_OPEN_REQ);
        w.SetDWord(bundleId);
        return w.Written.ToArray();
    }

    /// <summary>
    /// CUIDroppedItemDlg take-item: <c>[WIZ_ITEM_GET=0x26][u32 bundleId][u32 itemId]</c>
    /// (9 bytes). The item id is the full encoded id (basic + ext); the caller passes the
    /// base id only for gold.
    /// </summary>
    public static byte[] BuildItemGet(uint bundleId, uint itemId)
    {
        var buffer = new byte[9];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ITEM_GET);
        w.SetDWord(bundleId);
        w.SetDWord(itemId);
        return w.Written.ToArray();
    }

    /// <summary>
    /// Parse the WIZ_BUNDLE_OPEN_REQ reply (MsgRecv_ItemBundleOpen): six <c>[u32 itemId]
    /// [i16 count]</c> pairs. Empty slots (itemId 0) are skipped; the slot order is kept so
    /// the dialog can place each icon at its <c>UI_AREA_TYPE_DROP_ITEM</c> region.
    /// </summary>
    public static IReadOnlyList<LootItem> ParseBundleOpen(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode

        var items = new List<LootItem>(MaxBundlePieces);
        for (int i = 0; i < MaxBundlePieces; i++)
        {
            uint itemId = r.GetDWord();
            int count = r.GetShort();
            if (itemId != 0)
                items.Add(new LootItem(i, itemId, count));
        }

        return items;
    }

    /// <summary>Parse the WIZ_ITEM_GET reply (CGameProcMain::MsgRecv_ItemDroppedGetResult).</summary>
    public static ItemGetResult ParseItemGetResult(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte result = r.GetByte();

        byte pos = 0;
        uint itemId = 0;
        int count = 0;
        uint goldId = 0;
        string name = string.Empty;

        if (result is 0x01 or 0x02 or 0x05)
        {
            pos = r.GetByte();
            itemId = r.GetDWord();
            if (result is 0x01 or 0x05)
                count = r.GetShort();
            goldId = r.GetDWord();
        }
        else if (result == 0x03)
        {
            itemId = r.GetDWord();
            int len = r.GetShort();
            if (len > 0 && len <= r.Remaining)
                name = OpenKO.Core.Text.KoEncoding.Cp949.GetString(r.GetString(len));
        }

        return new ItemGetResult(result, pos, itemId, count, goldId, name);
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
