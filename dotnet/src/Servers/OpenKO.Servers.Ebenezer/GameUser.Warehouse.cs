using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser warehouse slice (User.cpp WarehouseProcess): the compressed
/// inventory download and the gold/item transfers between bank and inventory.
/// </summary>
public sealed partial class GameUser
{
    // e_WarehouseOpcode (shared/packets.h).
    public const byte WarehouseOpen = 0x01;
    public const byte WarehouseInput = 0x02;
    public const byte WarehouseOutput = 0x03;
    public const byte WarehouseMove = 0x04;
    public const byte WarehouseInvenMove = 0x05;
    public const byte WarehouseReq = 0x10;

    private const byte ItemLogWarehousePut = 7;
    private const byte ItemLogWarehouseGet = 8;

    /// <summary>CUser::WarehouseProcess — WIZ_WAREHOUSE.</summary>
    public void WarehouseProcess(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte command = reader.GetByte();

        if (ResHpType == UserDeadResHpType || user.Hp == 0)
        {
            logger.LogError("WarehouseProcess: dead user cannot use the warehouse [charId={CharId}]", user.CharId);
            return;
        }

        if (ExchangeUser != -1)
        {
            SendWarehouseFail(command);
            return;
        }

        if (command == WarehouseOpen)
        {
            var openBuffer = new byte[16 + GameConstants.WarehouseMax * 8];
            var openWriter = new PacketWriter(openBuffer);
            openWriter.SetByte((byte)GameOpcode.WIZ_WAREHOUSE);
            openWriter.SetByte(WarehouseOpen);
            openWriter.SetByte(1); // success
            openWriter.SetDWord((uint)user.Bank);

            for (int i = 0; i < GameConstants.WarehouseMax; i++)
            {
                openWriter.SetDWord((uint)user.Warehouse[i].Num);
                openWriter.SetShort(user.Warehouse[i].Duration);
                openWriter.SetShort(user.Warehouse[i].Count);
            }

            SendCompressingPacket(openWriter.Written);
            return;
        }

        var itemId = (int)reader.GetDWord();
        int page = reader.GetByte();
        int srcPos = reader.GetByte();
        int destPos = reader.GetByte();

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
        {
            SendWarehouseFail(command);
            return;
        }

        int referencePos = 24 * page;

        switch (command)
        {
            case WarehouseInput:
            {
                var count = (int)reader.GetDWord();

                if (itemId == ItemGold)
                {
                    if ((long)user.Bank + count > 2_100_000_000 || user.Gold - count < 0)
                    {
                        SendWarehouseFail(command);
                        return;
                    }

                    user.Bank += count;
                    user.Gold -= count;
                    break;
                }

                // C++ quirk kept as-is: the bound uses > WAREHOUSE_MAX.
                if (user.Items[GameConstants.SlotMax + srcPos].Num != itemId
                    || referencePos + destPos > GameConstants.WarehouseMax
                    || referencePos + destPos >= user.Warehouse.Length
                    || (user.Warehouse[referencePos + destPos].Num != 0 && table.Countable == 0)
                    || user.Items[GameConstants.SlotMax + srcPos].Count < count)
                {
                    SendWarehouseFail(command);
                    return;
                }

                ref WarehouseItemData ware = ref user.Warehouse[referencePos + destPos];
                ref ItemData inven = ref user.Items[GameConstants.SlotMax + srcPos];

                ware.Num = itemId;
                ware.Duration = inven.Duration;
                ware.SerialNum = inven.SerialNum;

                if (table.Countable == 0 && ware.SerialNum == 0)
                    ware.SerialNum = world.GenerateItemSerial();

                if (table.Countable != 0)
                    ware.Count += (short)count;
                else
                    ware.Count = inven.Count;

                if (table.Countable == 0)
                {
                    inven = default;
                }
                else
                {
                    inven.Count -= (short)count;
                    if (inven.Count <= 0)
                        inven = default;
                }

                SendItemWeight();
                ItemLogToAgent(user.AccountId, user.CharId, ItemLogWarehousePut, 0, itemId, count,
                    user.Warehouse[referencePos + destPos].Duration);
                break;
            }

            case WarehouseOutput:
            {
                var count = (int)reader.GetDWord();

                if (itemId == ItemGold)
                {
                    if ((long)user.Gold + count > 2_100_000_000 || user.Bank - count < 0)
                    {
                        SendWarehouseFail(command);
                        return;
                    }

                    user.Gold += count;
                    user.Bank -= count;
                    break;
                }

                int addedWeight = table.Countable != 0 ? table.Weight * count : table.Weight;
                if (addedWeight + ItemWeight > MaxWeight
                    || referencePos + srcPos > GameConstants.WarehouseMax
                    || referencePos + srcPos >= user.Warehouse.Length
                    || user.Warehouse[referencePos + srcPos].Num != itemId
                    || (user.Items[GameConstants.SlotMax + destPos].Num != 0 && table.Countable == 0)
                    || user.Warehouse[referencePos + srcPos].Count < count)
                {
                    SendWarehouseFail(command);
                    return;
                }

                ref WarehouseItemData ware = ref user.Warehouse[referencePos + srcPos];
                ref ItemData inven = ref user.Items[GameConstants.SlotMax + destPos];

                inven.Num = itemId;
                inven.Duration = ware.Duration;
                inven.SerialNum = ware.SerialNum;

                if (table.Countable != 0)
                {
                    inven.Count += (short)count;
                }
                else
                {
                    if (inven.SerialNum == 0)
                        inven.SerialNum = world.GenerateItemSerial();

                    inven.Count = ware.Count;
                }

                if (table.Countable == 0)
                {
                    ware = default;
                }
                else
                {
                    ware.Count -= (short)count;
                    if (ware.Count <= 0)
                        ware = default;
                }

                SendItemWeight();
                ItemLogToAgent(user.CharId, user.AccountId, ItemLogWarehouseGet, 0, itemId, count,
                    user.Items[GameConstants.SlotMax + destPos].Duration);
                break;
            }

            case WarehouseMove:
            {
                if (referencePos + srcPos > GameConstants.WarehouseMax
                    || referencePos + srcPos >= user.Warehouse.Length
                    || referencePos + destPos >= user.Warehouse.Length
                    || user.Warehouse[referencePos + srcPos].Num != itemId
                    || user.Warehouse[referencePos + destPos].Num != 0)
                {
                    SendWarehouseFail(command);
                    return;
                }

                user.Warehouse[referencePos + destPos] = user.Warehouse[referencePos + srcPos];
                user.Warehouse[referencePos + srcPos] = default;
                break;
            }

            case WarehouseInvenMove:
            {
                if (itemId != user.Items[GameConstants.SlotMax + srcPos].Num)
                {
                    SendWarehouseFail(command);
                    return;
                }

                (user.Items[GameConstants.SlotMax + srcPos], user.Items[GameConstants.SlotMax + destPos]) =
                    (user.Items[GameConstants.SlotMax + destPos], user.Items[GameConstants.SlotMax + srcPos]);
                break;
            }
        }

        user.WarehouseFlag = 1;

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_WAREHOUSE);
        writer.SetByte(command);
        writer.SetByte(0x01);
        Send(writer.Written);
    }

    private void SendWarehouseFail(byte command)
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_WAREHOUSE);
        writer.SetByte(command);
        writer.SetByte(0x00);
        Send(writer.Written);
    }
}
