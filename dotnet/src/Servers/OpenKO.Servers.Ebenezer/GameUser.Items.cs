using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser inventory slice (User.cpp): equip/inventory moves, the NPC
/// merchant trade, loot pickup, destruction, repair and the NPC click events.
/// </summary>
public sealed partial class GameUser
{
    // e_ItemMoveDirection.
    private const byte ItemMoveInvenSlot = 1;
    private const byte ItemMoveSlotInven = 2;
    private const byte ItemMoveInvenInven = 3;
    private const byte ItemMoveSlotSlot = 4;

    private const int ReservedSlot = 14;        // RESERVED (== SLOT_MAX)
    private const int ItemNoTrade = 900000001;  // ITEM_NO_TRADE
    private const byte SaleTypeFull = 1;        // SALE_TYPE_FULL

    // e_ItemLogType.
    private const byte ItemLogMerchantBuy = 1;
    private const byte ItemLogMerchantSell = 2;
    private const byte ItemLogMonsterGet = 3;
    private const byte ItemLogDestroy = 6;

    private const byte NpcTypeMerchant = 21; // NPC_MERCHANT
    private const byte NpcTypeTinker = 22;   // NPC_TINKER

    /// <summary>m_sExchangeUser (user-to-user trade partner; -1 = none).</summary>
    public short ExchangeUser = -1;

    /// <summary>CUser::ItemMove — WIZ_ITEM_MOVE.</summary>
    public void ItemMove(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte dir = reader.GetByte();
        var itemId = (int)reader.GetDWord();
        int srcPos = reader.GetByte();
        int destPos = reader.GetByte();

        if (ExchangeUser != -1)
        {
            SendItemMoveFail();
            return;
        }

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null
            || dir > 0x04
            || srcPos >= GameConstants.InventoryTotal
            || destPos >= GameConstants.InventoryTotal)
        {
            SendItemMoveFail();
            return;
        }

        if (destPos > GameConstants.SlotMax && dir is ItemMoveInvenSlot or ItemMoveSlotSlot)
        {
            SendItemMoveFail();
            return;
        }

        if (dir == ItemMoveSlotInven && srcPos > GameConstants.SlotMax)
        {
            SendItemMoveFail();
            return;
        }

        if ((dir == ItemMoveInvenSlot && destPos == ReservedSlot)
            || (dir == ItemMoveSlotInven && srcPos == ReservedSlot))
        {
            SendItemMoveFail();
            return;
        }

        if (dir is ItemMoveInvenSlot or ItemMoveSlotSlot)
        {
            if ((table.Race != 0 && table.Race != user.Race)
                || !ItemEquipAvailable(table))
            {
                SendItemMoveFail();
                return;
            }
        }

        bool ok = dir switch
        {
            ItemMoveInvenSlot => MoveInvenToSlot(user, table, itemId, srcPos, destPos),
            ItemMoveSlotInven => MoveSlotToInven(user, itemId, srcPos, destPos),
            ItemMoveInvenInven => MoveInvenToInven(user, itemId, srcPos, destPos),
            ItemMoveSlotSlot => MoveSlotToSlot(user, table, itemId, srcPos, destPos),
            _ => true,
        };

        if (!ok)
        {
            SendItemMoveFail();
            return;
        }

        // Only equip changes recompute the stats.
        if (dir != ItemMoveInvenInven)
        {
            SetSlotItemValue();
            SetUserAbility();
        }

        SendItemMoveStats();
        SendItemWeight();

        if (dir == ItemMoveInvenSlot && IsVisibleSlot(destPos))
            UserLookChange(destPos, itemId, user.Items[destPos].Duration);

        if (dir == ItemMoveSlotInven && IsVisibleSlot(srcPos))
            UserLookChange(srcPos, 0, 0);

        SendAiUserUpdate();
    }

    private static bool IsVisibleSlot(int pos)
        => pos is GameConstants.SlotHead or GameConstants.SlotBreast or GameConstants.SlotShoulder
            or GameConstants.SlotLeftHand or GameConstants.SlotRightHand or GameConstants.SlotLeg
            or GameConstants.SlotGlove or GameConstants.SlotFoot;

    private void SendItemMoveFail()
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_MOVE);
        writer.SetByte(0x00);
        Send(writer.Written);
    }

    /// <summary>The WIZ_ITEM_MOVE 0x01 stat refresh blob (shared with the durability break).</summary>
    private void SendItemMoveStats()
    {
        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_MOVE);
        writer.SetByte(0x01);
        writer.SetShort(TotalHit);
        writer.SetShort(TotalAc);
        writer.SetShort(GetMaxWeightForClient());
        writer.SetShort(MaxHp);
        writer.SetShort(MaxMp);
        writer.SetShort(ItemStr + StrAmount);
        writer.SetShort(ItemSta + StaAmount);
        writer.SetShort(ItemDex + DexAmount);
        writer.SetShort(ItemIntel + IntelAmount);
        writer.SetShort(ItemCham + ChaAmount);
        writer.SetShort(FireR);
        writer.SetShort(ColdR);
        writer.SetShort(LightningR);
        writer.SetShort(MagicR);
        writer.SetShort(DiseaseR);
        writer.SetShort(PoisonR);
        Send(writer.Written);
    }

    /// <summary>The recurring inventory↔slot swap block (count forced to 1 on the equip side).</summary>
    private void EquipSwap(UserData user, int srcAbs, int destSlot, int itemId)
    {
        short duration = user.Items[srcAbs].Duration;
        long serial = user.Items[srcAbs].SerialNum;

        user.Items[srcAbs].Num = user.Items[destSlot].Num;
        user.Items[srcAbs].Duration = user.Items[destSlot].Duration;
        user.Items[srcAbs].Count = user.Items[destSlot].Count;
        user.Items[srcAbs].SerialNum = user.Items[destSlot].SerialNum;

        if (user.Items[srcAbs].Num != 0 && user.Items[srcAbs].SerialNum == 0)
            user.Items[srcAbs].SerialNum = world.GenerateItemSerial();

        user.Items[destSlot].Num = itemId;
        user.Items[destSlot].Duration = duration;
        user.Items[destSlot].Count = 1;
        user.Items[destSlot].SerialNum = serial;

        if (user.Items[destSlot].SerialNum == 0)
            user.Items[destSlot].SerialNum = world.GenerateItemSerial();
    }

    /// <summary>Equip the inventory item into <paramref name="hand"/> and push the two-hander from <paramref name="otherHand"/> back.</summary>
    private void EquipAndDisplaceHand(UserData user, int srcAbs, int hand, int otherHand)
    {
        user.Items[hand] = user.Items[srcAbs];
        if (user.Items[hand].SerialNum == 0)
            user.Items[hand].SerialNum = world.GenerateItemSerial();

        user.Items[srcAbs] = user.Items[otherHand];
        if (user.Items[srcAbs].SerialNum == 0)
            user.Items[srcAbs].SerialNum = world.GenerateItemSerial();

        user.Items[otherHand] = default;
    }

    private bool MoveInvenToSlot(UserData user, Item table, int itemId, int srcPos, int destPos)
    {
        int srcAbs = GameConstants.SlotMax + srcPos;

        if (itemId != user.Items[srcAbs].Num)
            return false;

        if (!IsValidSlotPos(table, destPos))
            return false;

        // Right-hand weapon (or either-hand into the right) vs a two-hander in the left.
        if (table.Slot == 0x01 || (table.Slot == 0x00 && destPos == GameConstants.SlotRightHand))
        {
            if (user.Items[GameConstants.SlotLeftHand].Num != 0)
            {
                Item? leftTable = world.ItemTable.GetValueOrDefault(user.Items[GameConstants.SlotLeftHand].Num);
                if (leftTable is not null)
                {
                    if (leftTable.Slot == 0x04)
                        EquipAndDisplaceHand(user, srcAbs, GameConstants.SlotRightHand, GameConstants.SlotLeftHand);
                    else
                        EquipSwap(user, srcAbs, destPos, itemId);
                }

                // C++ quirk kept as-is: an unknown left-hand item makes this a no-op success.
            }
            else
            {
                EquipSwap(user, srcAbs, destPos, itemId);
            }
        }
        // Left-hand weapon (or either-hand into the left) vs a two-hander in the right.
        else if (table.Slot == 0x02 || (table.Slot == 0x00 && destPos == GameConstants.SlotLeftHand))
        {
            if (user.Items[GameConstants.SlotRightHand].Num != 0)
            {
                Item? rightTable = world.ItemTable.GetValueOrDefault(user.Items[GameConstants.SlotRightHand].Num);
                if (rightTable is not null)
                {
                    if (rightTable.Slot == 0x03)
                        EquipAndDisplaceHand(user, srcAbs, GameConstants.SlotLeftHand, GameConstants.SlotRightHand);
                    else
                        EquipSwap(user, srcAbs, destPos, itemId);
                }
            }
            else
            {
                EquipSwap(user, srcAbs, destPos, itemId);
            }
        }
        // Two-hander carried in the right hand.
        else if (table.Slot == 0x03)
        {
            if (user.Items[GameConstants.SlotLeftHand].Num != 0
                && user.Items[GameConstants.SlotRightHand].Num != 0)
                return false;

            if (user.Items[GameConstants.SlotLeftHand].Num != 0)
                EquipAndDisplaceHand(user, srcAbs, GameConstants.SlotRightHand, GameConstants.SlotLeftHand);
            else
                EquipSwap(user, srcAbs, destPos, itemId);
        }
        // Two-hander carried in the left hand.
        else if (table.Slot == 0x04)
        {
            if (user.Items[GameConstants.SlotLeftHand].Num != 0
                && user.Items[GameConstants.SlotRightHand].Num != 0)
                return false;

            if (user.Items[GameConstants.SlotRightHand].Num != 0)
                EquipAndDisplaceHand(user, srcAbs, GameConstants.SlotLeftHand, GameConstants.SlotRightHand);
            else
                EquipSwap(user, srcAbs, destPos, itemId);
        }
        else
        {
            EquipSwap(user, srcAbs, destPos, itemId);
        }

        return true;
    }

    private bool MoveSlotToInven(UserData user, int itemId, int srcPos, int destPos)
    {
        int destAbs = GameConstants.SlotMax + destPos;

        if (itemId != user.Items[srcPos].Num)
            return false;

        if (user.Items[destAbs].Num != 0)
            return false;

        user.Items[destAbs] = user.Items[srcPos];
        if (user.Items[destAbs].SerialNum == 0)
            user.Items[destAbs].SerialNum = world.GenerateItemSerial();

        user.Items[srcPos] = default;
        return true;
    }

    private bool MoveInvenToInven(UserData user, int itemId, int srcPos, int destPos)
    {
        int srcAbs = GameConstants.SlotMax + srcPos;
        int destAbs = GameConstants.SlotMax + destPos;

        if (itemId != user.Items[srcAbs].Num)
            return false;

        ItemData src = user.Items[srcAbs];

        user.Items[srcAbs] = user.Items[destAbs];
        if (user.Items[srcAbs].SerialNum == 0
            && world.ItemTable.GetValueOrDefault(user.Items[srcAbs].Num) is { Countable: 0 })
            user.Items[srcAbs].SerialNum = world.GenerateItemSerial();

        user.Items[destAbs] = src;
        if (user.Items[destAbs].SerialNum == 0
            && world.ItemTable.GetValueOrDefault(user.Items[destAbs].Num) is { Countable: 0 })
            user.Items[destAbs].SerialNum = world.GenerateItemSerial();

        return true;
    }

    private bool MoveSlotToSlot(UserData user, Item table, int itemId, int srcPos, int destPos)
    {
        if (itemId != user.Items[srcPos].Num)
            return false;

        if (!IsValidSlotPos(table, destPos))
            return false;

        if (user.Items[destPos].Num != 0)
        {
            Item? destTable = world.ItemTable.GetValueOrDefault(user.Items[destPos].Num);
            if (destTable is null)
                return true; // like the C++: silently keeps the success path

            if (destTable.Slot != 0x00)
                return false;
        }

        ItemData src = user.Items[srcPos];

        user.Items[srcPos] = user.Items[destPos];
        if (user.Items[destPos].Num != 0 && user.Items[srcPos].SerialNum == 0)
            user.Items[srcPos].SerialNum = world.GenerateItemSerial();

        user.Items[destPos] = src;
        if (user.Items[destPos].SerialNum == 0)
            user.Items[destPos].SerialNum = world.GenerateItemSerial();

        return true;
    }

    /// <summary>CUser::IsValidSlotPos — item slot class vs equip position.</summary>
    public bool IsValidSlotPos(Item table, int destPos) => table.Slot switch
    {
        0 => destPos is GameConstants.SlotRightHand or GameConstants.SlotLeftHand,
        1 or 3 => destPos == GameConstants.SlotRightHand,
        2 or 4 => destPos == GameConstants.SlotLeftHand,
        5 => destPos == GameConstants.SlotBreast,
        6 => destPos == GameConstants.SlotLeg,
        7 => destPos == GameConstants.SlotHead,
        8 => destPos == GameConstants.SlotGlove,
        9 => destPos == GameConstants.SlotFoot,
        10 => destPos is GameConstants.SlotRightEar or GameConstants.SlotLeftEar,
        11 => destPos == GameConstants.SlotNeck,
        12 => destPos is GameConstants.SlotRightRing or GameConstants.SlotLeftRing,
        13 => destPos == GameConstants.SlotShoulder,
        14 => destPos == GameConstants.SlotWaist,
        _ => true,
    };

    /// <summary>CUser::ItemEquipAvailable.</summary>
    public bool ItemEquipAvailable(Item table)
    {
        if (UserData is not { } user)
            return false;

        return table.RequiredRank <= user.Rank
            && table.RequiredTitle <= user.Title
            && table.RequiredStrength <= user.Str
            && table.RequiredStamina <= user.Sta
            && table.RequiredDexterity <= user.Dex
            && table.RequiredIntelligence <= user.Intel
            && table.RequiredCharisma <= user.Cha;
    }

    /// <summary>CUser::ItemTrade — WIZ_ITEM_TRADE (NPC merchant buy/sell + quickslot move).</summary>
    public void ItemTrade(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        byte result = 0;

        if (ResHpType == UserDeadResHpType || user.Hp == 0)
        {
            logger.LogError("ItemTrade: dead user cannot trade [charId={CharId}]", user.CharId);
            SendItemTradeFail(0x01);
            return;
        }

        var reader = new PacketReader(body);
        byte type = reader.GetByte();

        int group = 0;
        short npcId = 0;
        if (type == 0x01)
        {
            group = (int)reader.GetDWord();
            npcId = reader.GetShort();
        }

        var itemId = (int)reader.GetDWord();
        int pos = reader.GetByte();
        int destPos = 0;
        int count = 0;

        if (type == 0x03)
            destPos = reader.GetByte();
        else
            count = reader.GetShort();

        if (itemId >= ItemNoTrade)
        {
            SendItemTradeFail(result);
            return;
        }

        // Inventory-to-inventory quick move.
        if (type == 0x03)
        {
            if (pos >= GameConstants.HaveMax
                || destPos >= GameConstants.HaveMax
                || itemId != user.Items[GameConstants.SlotMax + pos].Num)
            {
                var failBuffer = new byte[4];
                var failWriter = new PacketWriter(failBuffer);
                failWriter.SetByte((byte)GameOpcode.WIZ_ITEM_TRADE);
                failWriter.SetByte(0x04);
                Send(failWriter.Written);
                return;
            }

            // C++ quirk kept as-is: the serial number does not travel with the swap.
            short duration = user.Items[GameConstants.SlotMax + pos].Duration;
            short itemCount = user.Items[GameConstants.SlotMax + pos].Count;

            user.Items[GameConstants.SlotMax + pos].Num = user.Items[GameConstants.SlotMax + destPos].Num;
            user.Items[GameConstants.SlotMax + pos].Duration = user.Items[GameConstants.SlotMax + destPos].Duration;
            user.Items[GameConstants.SlotMax + pos].Count = user.Items[GameConstants.SlotMax + destPos].Count;
            user.Items[GameConstants.SlotMax + destPos].Num = itemId;
            user.Items[GameConstants.SlotMax + destPos].Duration = duration;
            user.Items[GameConstants.SlotMax + destPos].Count = itemCount;

            var moveBuffer = new byte[4];
            var moveWriter = new PacketWriter(moveBuffer);
            moveWriter.SetByte((byte)GameOpcode.WIZ_ITEM_TRADE);
            moveWriter.SetByte(0x03);
            Send(moveWriter.Written);
            return;
        }

        if (ExchangeUser != -1)
        {
            SendItemTradeFail(result);
            return;
        }

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
        {
            SendItemTradeFail(0x01);
            return;
        }

        if (pos >= GameConstants.HaveMax || count <= 0 || count > MaxItemCount)
        {
            SendItemTradeFail(0x02);
            return;
        }

        int slot = GameConstants.SlotMax + pos;

        if (type == 0x01)
        {
            // Buy sequence.
            if (!world.PointCheckFlag)
            {
                SendItemTradeFail(0x01);
                return;
            }

            GameNpc? npc = world.Npcs.GetValueOrDefault(npcId);
            if (npc is null || npc.SellingGroup != group)
            {
                SendItemTradeFail(0x01);
                return;
            }

            if (table.Countable == 0 && count != 1)
            {
                SendItemTradeFail(0x02);
                return;
            }

            if (user.Items[slot].Num != 0)
            {
                if (user.Items[slot].Num == itemId)
                {
                    if (table.Countable == 0 || count <= 0)
                    {
                        SendItemTradeFail(0x02);
                        return;
                    }

                    if (table.Countable != 0 && count + user.Items[slot].Count > MaxItemCount)
                    {
                        SendItemTradeFail(0x04);
                        return;
                    }
                }
                else
                {
                    SendItemTradeFail(0x02);
                    return;
                }
            }

            long buyPrice = (long)table.BuyPrice * count;
            if (buyPrice is < 0 or > 2_100_000_000 || user.Gold < buyPrice)
            {
                SendItemTradeFail(0x03);
                return;
            }

            int addedWeight = table.Countable != 0 ? table.Weight * count : table.Weight;
            if (addedWeight + ItemWeight > MaxWeight)
            {
                SendItemTradeFail(0x04);
                return;
            }

            user.Items[slot].Num = itemId;
            user.Items[slot].Duration = table.Durability;
            user.Gold -= (int)buyPrice;

            if (table.Countable != 0 && count > 0)
            {
                user.Items[slot].Count += (short)count;
            }
            else
            {
                user.Items[slot].Count = 1;
                user.Items[slot].SerialNum = world.GenerateItemSerial();
            }

            SendItemWeight();
            ItemLogToAgent(user.CharId, npc.Name, ItemLogMerchantBuy, user.Items[slot].SerialNum,
                itemId, count, table.Durability);
        }
        else
        {
            // Sell sequence.
            if (user.Items[slot].Num != itemId)
            {
                SendItemTradeFail(0x02);
                return;
            }

            if (user.Items[slot].Count < count)
            {
                SendItemTradeFail(0x03);
                return;
            }

            int durability = user.Items[slot].Duration;

            // C++ quirk kept as-is: SellPrice is the sale TYPE, the price base is BuyPrice.
            long salePrice = table.Countable != 0 && count > 0
                ? (long)table.BuyPrice * count
                : table.BuyPrice;

            if (table.SellPrice != SaleTypeFull)
                salePrice /= user.PremiumType != 0 ? 6 : 4;

            if (salePrice is < 0 or > 2_100_000_000)
            {
                SendItemTradeFail(0x03);
                return;
            }

            user.Gold += (int)salePrice;

            if (table.Countable != 0 && count > 0)
            {
                user.Items[slot].Count -= (short)count;
                if (user.Items[slot].Count <= 0)
                    user.Items[slot] = default;
            }
            else
            {
                user.Items[slot] = default;
            }

            SendItemWeight();
            ItemLogToAgent(user.CharId, "MERCHANT SELL", ItemLogMerchantSell, 0, itemId, count, durability);
        }

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_TRADE);
        writer.SetByte(0x01);
        writer.SetDWord((uint)user.Gold);
        Send(writer.Written);
    }

    private void SendItemTradeFail(byte result)
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_TRADE);
        writer.SetByte(0x00);
        writer.SetByte(result);
        Send(writer.Written);
    }

    /// <summary>CUser::ItemGet — pick one stack out of a loot bundle in the own region.</summary>
    public void ItemGet(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        var bundleIndex = (uint)reader.GetDWord();

        if (bundleIndex < 1 || ExchangeUser != -1)
        {
            SendItemGetFail();
            return;
        }

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null || !map.IsValidRegion(RegionX, RegionZ))
        {
            SendItemGetFail();
            return;
        }

        ZoneItem? bundle = map.Regions[RegionX, RegionZ].Items.GetValueOrDefault(bundleIndex);
        if (bundle is null)
        {
            SendItemGetFail();
            return;
        }

        var itemId = (int)reader.GetDWord();

        int stack = Array.IndexOf(bundle.ItemId, itemId);
        if (stack < 0)
        {
            SendItemGetFail();
            return;
        }

        short count = bundle.Count[stack];

        if (!map.RegionItemRemove(RegionX, RegionZ, bundleIndex, itemId, count))
        {
            SendItemGetFail();
            return;
        }

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
        {
            SendItemGetFail();
            return;
        }

        // GetItemRoutingUser attaches with the party slice; solo pickup only.
        GameUser getUser = this;

        int pos = getUser.GetEmptySlot(itemId, table.Countable);

        if (pos != 0xFF)
        {
            if (pos >= GameConstants.HaveMax)
            {
                SendItemGetFail();
                return;
            }

            ref ItemData slot = ref getUser.UserData!.Items[GameConstants.SlotMax + pos];

            if (slot.Num != 0 && (table.Countable != 1 || slot.Num != itemId))
            {
                SendItemGetFail();
                return;
            }

            int addedWeight = table.Countable != 0 ? table.Weight * count : table.Weight;
            if (addedWeight + getUser.ItemWeight > getUser.MaxWeight)
            {
                var full = new byte[4];
                var fullWriter = new PacketWriter(full);
                fullWriter.SetByte((byte)GameOpcode.WIZ_ITEM_GET);
                fullWriter.SetByte(0x06);
                getUser.Send(fullWriter.Written);
                return;
            }

            slot.Num = itemId;

            if (table.Countable != 0)
            {
                slot.Count += count;
                if (slot.Count > MaxItemCount)
                    slot.Count = MaxItemCount;
            }
            else
            {
                slot.Count = 1;
                slot.SerialNum = world.GenerateItemSerial();
            }

            getUser.SendItemWeight();
            slot.Duration = table.Durability;
            ItemLogToAgent(getUser.UserData.CharId, "MONSTER", ItemLogMonsterGet, slot.SerialNum,
                itemId, count, table.Durability);

            var buffer = new byte[24];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_ITEM_GET);
            writer.SetByte(0x01); // solo pickup (0x05 = routed to a party member)
            writer.SetByte((byte)pos);
            writer.SetDWord((uint)itemId);
            writer.SetShort(getUser.UserData.Items[GameConstants.SlotMax + pos].Count);
            writer.SetDWord((uint)getUser.UserData.Gold);
            getUser.Send(writer.Written);

            // The WIZ_ITEM_GET 0x03 party notification attaches with the party slice.
        }
        else
        {
            // No free slot: only gold can still be picked up.
            if (itemId != ItemGold || count is <= 0 or >= 32767)
            {
                if (itemId != ItemGold)
                    SendItemGetFail();

                return;
            }

            // The party gold split attaches with the party slice.
            user.Gold += count;

            var buffer = new byte[24];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_ITEM_GET);
            writer.SetByte(0x01);
            writer.SetByte((byte)pos);
            writer.SetDWord((uint)itemId);
            writer.SetShort(count);
            writer.SetDWord((uint)user.Gold);
            Send(writer.Written);
        }
    }

    private void SendItemGetFail()
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_GET);
        writer.SetByte(0x00);
        Send(writer.Written);
    }

    /// <summary>CUser::BundleOpenReq — list the contents of a loot bundle.</summary>
    public void BundleOpenReq(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        var bundleIndex = (uint)reader.GetDWord();
        if (bundleIndex < 1)
            return;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null || !map.IsValidRegion(RegionX, RegionZ))
            return;

        ZoneItem? bundle = map.Regions[RegionX, RegionZ].Items.GetValueOrDefault(bundleIndex);
        if (bundle is null)
            return;

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_BUNDLE_OPEN_REQ);
        for (int i = 0; i < 6; i++)
        {
            writer.SetDWord((uint)bundle.ItemId[i]);
            writer.SetShort(bundle.Count[i]);
        }

        Send(writer.Written);
    }

    /// <summary>CUser::ItemRemove — destroy an item (WIZ_ITEM_REMOVE).</summary>
    public void ItemRemove(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte slotType = reader.GetByte();
        int pos = reader.GetByte();
        var itemId = (int)reader.GetDWord();

        // C++ quirk kept as-is: both bounds use > rather than >=.
        int abs;
        if (slotType == 1)
        {
            if (pos > GameConstants.SlotMax || user.Items[pos].Num != itemId)
            {
                SendItemRemoveFail();
                return;
            }

            abs = pos;
        }
        else
        {
            if (pos > GameConstants.HaveMax || user.Items[GameConstants.SlotMax + pos].Num != itemId)
            {
                SendItemRemoveFail();
                return;
            }

            abs = GameConstants.SlotMax + pos;
        }

        short count = user.Items[abs].Count;
        short durability = user.Items[abs].Duration;
        long serial = user.Items[abs].SerialNum;

        user.Items[abs] = default;

        SendItemWeight();

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_REMOVE);
        writer.SetByte(0x01);
        Send(writer.Written);

        ItemLogToAgent(user.CharId, "DESTROY", ItemLogDestroy, serial, itemId, count, durability);
    }

    private void SendItemRemoveFail()
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_REMOVE);
        writer.SetByte(0x00);
        Send(writer.Written);
    }

    /// <summary>CUser::ItemRepair — WIZ_ITEM_REPAIR.</summary>
    public void ItemRepair(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        int pos = reader.GetByte();
        int slot = reader.GetByte();
        var itemId = (int)reader.GetDWord();

        int abs = -1;
        if (pos == 1)
        {
            if (slot >= GameConstants.SlotMax || user.Items[slot].Num != itemId)
            {
                SendItemRepairFail(user);
                return;
            }

            abs = slot;
        }
        else if (pos == 2)
        {
            if (slot >= GameConstants.HaveMax || user.Items[GameConstants.SlotMax + slot].Num != itemId)
            {
                SendItemRepairFail(user);
                return;
            }

            abs = GameConstants.SlotMax + slot;
        }

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null || abs < 0)
        {
            SendItemRepairFail(user);
            return;
        }

        int durability = table.Durability;
        if (durability == 1)
        {
            SendItemRepairFail(user);
            return;
        }

        int quantity = durability - user.Items[abs].Duration;

        var money = (int)((((table.BuyPrice - 10) / 10000.0f) + Math.Pow(table.BuyPrice, 0.75))
            * quantity / (double)durability);
        if (money > user.Gold)
        {
            SendItemRepairFail(user);
            return;
        }

        user.Gold -= money;
        user.Items[abs].Duration = (short)durability;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_REPAIR);
        writer.SetByte(0x01);
        writer.SetDWord((uint)user.Gold);
        Send(writer.Written);
    }

    private void SendItemRepairFail(UserData user)
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_REPAIR);
        writer.SetByte(0x00);
        writer.SetDWord((uint)user.Gold);
        Send(writer.Written);
    }

    /// <summary>CUser::NpcEvent — clicking an NPC (merchant/tinker subset).</summary>
    public void NpcEvent(ReadOnlySpan<byte> body)
    {
        if (!world.PointCheckFlag)
            return;

        var reader = new PacketReader(body);
        short nid = reader.GetShort();

        GameNpc? npc = world.Npcs.GetValueOrDefault(nid);
        if (npc is null)
            return;

        switch (npc.NpcType)
        {
            case NpcTypeMerchant:
            {
                var buffer = new byte[8];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_TRADE_NPC);
                writer.SetDWord((uint)npc.SellingGroup);
                Send(writer.Written);
                break;
            }

            case NpcTypeTinker:
            {
                var buffer = new byte[8];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_REPAIR_NPC);
                writer.SetDWord((uint)npc.SellingGroup);
                Send(writer.Written);
                break;
            }

            // Warehouse/officer/rental and the quest NPCs (ClientEvent) attach
            // with their slices.
        }
    }

    /// <summary>CUser::ItemLogToAgent — the WIZ_ITEM_LOG message for the ItemManager.</summary>
    public void ItemLogToAgent(string sourceId, string targetId, byte type, long serial,
        int itemId, int count, int durability)
    {
        var buffer = new byte[64 + sourceId.Length + targetId.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_LOG);
        writer.SetString2(Encoding.Latin1.GetBytes(sourceId));
        writer.SetString2(Encoding.Latin1.GetBytes(targetId));
        writer.SetByte(type);
        writer.SetInt64(serial);
        writer.SetDWord((uint)itemId);
        writer.SetShort(count);
        writer.SetShort(durability);

        world.ItemLogSink?.Invoke(buffer[..writer.Index]);
    }
}
