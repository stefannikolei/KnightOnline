using OpenKO.Core.IO;
using OpenKO.Data;
using OpenKO.Data.Models;

namespace OpenKO.Servers.Aujard;

/// <summary>
/// Encodes/decodes the USERDATA/WAREHOUSE blob columns exactly like
/// <c>DBAgent::LoadUserData/UpdateUser/LoadWarehouseData/UpdateWarehouseData</c>:
/// little-endian, items = per slot [int32 num][int16 duration][int16 count],
/// serials = per slot [int64], quests = per entry [int16 id][uint8 state].
/// Short blobs read as zeroes (ByteBuffer past-end semantics), matching the C++.
/// </summary>
public static class UserDataBlobCodec
{
    public static void ApplySkillsBlob(UserData user, byte[] blob)
    {
        var buffer = FromBlob(blob);
        for (int i = 0; i < GameConstants.MaxSkills; i++)
            user.Skills[i] = buffer.ReadByte();
    }

    /// <summary>
    /// Inventory (equip+inventory, 42 slots) from the items/serials blobs, with the
    /// C++ validation: unknown items are dropped, counts &gt; ITEMCOUNT_MAX clamp,
    /// countable items with count &lt;= 0 are wiped.
    /// </summary>
    public static void ApplyInventoryBlobs(
        UserData user,
        byte[] itemsBlob,
        byte[] serialsBlob,
        Func<int, ItemRow?> itemLookup,
        Action<int>? onItemDropped = null)
    {
        var items = FromBlob(itemsBlob);
        var serials = FromBlob(serialsBlob);

        for (int i = 0; i < GameConstants.InventoryTotal; i++)
        {
            int itemId = items.ReadInt32();
            short duration = items.ReadInt16();
            short count = items.ReadInt16();
            long serial = serials.ReadInt64();

            ItemRow? table = itemLookup(itemId);
            ref ItemData slot = ref user.Items[i];

            if (table is not null)
            {
                slot.Num = itemId;
                slot.Duration = duration;
                slot.SerialNum = serial;
                slot.Flag = 0;
                slot.TimeRemaining = 0;

                if (count > GameConstants.ItemCountMax)
                {
                    slot.Count = GameConstants.ItemCountMax;
                }
                else if (table.IsCountable && count <= 0)
                {
                    slot = default;
                }
                else
                {
                    // NOTE: the C++ assigns sCount=1 for count<=0 and then immediately
                    // overwrites it with count; the effective behavior is count as-is.
                    slot.Count = count;
                }
            }
            else
            {
                slot = default;

                if (itemId > 0)
                    onItemDropped?.Invoke(itemId);
            }
        }
    }

    public static (byte[] Items, byte[] Serials) BuildInventoryBlobs(UserData user)
    {
        var items = new ByteBuffer(400);
        var serials = new ByteBuffer(400);

        for (int i = 0; i < GameConstants.InventoryTotal; i++)
        {
            ref readonly ItemData item = ref user.Items[i];
            items.Append(item.Num);
            items.Append(item.Duration);
            items.Append(item.Count);
            serials.Append(item.SerialNum);
        }

        return (items.Contents.ToArray(), serials.Contents.ToArray());
    }

    /// <summary>
    /// Quests from the blob with the C++ validation (id &gt; 100 or state &gt; 3 wipes
    /// the entry). Returns the recomputed quest total.
    /// </summary>
    public static short ApplyQuestBlob(UserData user, byte[] questsBlob)
    {
        var quests = FromBlob(questsBlob);

        short questTotal = 0;
        for (int i = 0; i < GameConstants.MaxQuest; i++)
        {
            ref UserQuest quest = ref user.Quests[i];
            quest.QuestId = quests.ReadInt16();
            quest.QuestState = quests.ReadByte();

            if (quest.QuestId > 100 || quest.QuestState > 3)
            {
                quest = default;
                continue;
            }

            if (quest.QuestId > 0)
                questTotal++;
        }

        return questTotal;
    }

    /// <summary>
    /// Serializes quests, wiping invalid entries in place like UpdateUser does.
    /// Returns (blob, recomputed quest total).
    /// </summary>
    public static (byte[] Blob, short QuestTotal) BuildQuestBlob(UserData user)
    {
        var quests = new ByteBuffer(400);

        short questTotal = 0;
        for (int i = 0; i < GameConstants.MaxQuest; i++)
        {
            ref UserQuest quest = ref user.Quests[i];

            if (quest.QuestId > 100 || quest.QuestState > 3)
                quest = default;
            else if (quest.QuestId > 0)
                questTotal++;

            quests.Append(quest.QuestId);
            quests.Append(quest.QuestState);
        }

        return (quests.Contents.ToArray(), questTotal);
    }

    /// <summary>
    /// Warehouse (192 slots) from the WarehouseData/strSerial blobs with the C++
    /// validation. Note: unlike the inventory, count is only floored to 1
    /// (the ITEMCOUNT_MAX clamp in the C++ is dead code — the final assignment
    /// overwrites it with the raw count).
    /// </summary>
    public static void ApplyWarehouseBlobs(
        UserData user,
        byte[] itemsBlob,
        byte[] serialsBlob,
        Func<int, ItemRow?> itemLookup,
        Action<int>? onItemDropped = null)
    {
        var items = FromBlob(itemsBlob);
        var serials = FromBlob(serialsBlob);

        for (int i = 0; i < GameConstants.WarehouseMax; i++)
        {
            int itemId = (int)items.ReadUInt32();
            short durability = items.ReadInt16();
            short count = items.ReadInt16();
            long serialNumber = serials.ReadInt64();

            ref WarehouseItemData slot = ref user.Warehouse[i];

            if (itemLookup(itemId) is not null)
            {
                slot.Num = itemId;
                slot.Duration = durability;

                if (count <= 0)
                    count = 1;

                slot.Count = count;
                slot.SerialNum = serialNumber;
            }
            else
            {
                slot.Num = 0;
                slot.Duration = 0;
                slot.Count = 0;

                if (itemId > 0)
                    onItemDropped?.Invoke(itemId);
            }
        }
    }

    public static (byte[] Items, byte[] Serials) BuildWarehouseBlobs(UserData user)
    {
        var items = new ByteBuffer(1600);
        var serials = new ByteBuffer(1600);

        for (int i = 0; i < GameConstants.WarehouseMax; i++)
        {
            ref readonly WarehouseItemData item = ref user.Warehouse[i];
            items.Append(item.Num);
            items.Append(item.Duration);
            items.Append(item.Count);
            serials.Append(item.SerialNum);
        }

        return (items.Contents.ToArray(), serials.Contents.ToArray());
    }

    /// <summary>
    /// Fresh-character starter weapon (LoadUserData tail): level 1, no exp, no gold
    /// gets a class-specific weapon into the first free inventory slot.
    /// </summary>
    public static void ApplyStarterWeapon(UserData user)
    {
        if (user.Level != 1 || user.Exp != 0 || user.Gold != 0)
            return;

        int emptySlot = 0;
        for (int j = GameConstants.SlotMax; j < GameConstants.InventoryTotal; j++)
        {
            if (user.Items[j].Num == 0)
            {
                emptySlot = j;
                break;
            }
        }

        if (emptySlot == GameConstants.InventoryTotal)
            return;

        ref ItemData slot = ref user.Items[emptySlot];
        switch (user.Class)
        {
            case 101: slot.Num = 120010000; slot.Duration = 5000; break;
            case 102: slot.Num = 110010000; slot.Duration = 4000; break;
            case 103: slot.Num = 180010000; slot.Duration = 5000; break;
            case 104: slot.Num = 190010000; slot.Duration = 10000; break;
            case 201: slot.Num = 120050000; slot.Duration = 5000; break;
            case 202: slot.Num = 110050000; slot.Duration = 4000; break;
            case 203: slot.Num = 180050000; slot.Duration = 5000; break;
            case 204: slot.Num = 190050000; slot.Duration = 10000; break;
            default:
                slot.Count = 1;
                slot.SerialNum = 0;
                break;
        }
    }

    private static ByteBuffer FromBlob(byte[] blob)
    {
        var buffer = new ByteBuffer(Math.Max(blob.Length, 1));
        buffer.Append(blob);
        buffer.SyncForRead();
        return buffer;
    }
}
