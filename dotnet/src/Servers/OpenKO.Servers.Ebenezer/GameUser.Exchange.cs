using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>_EXCHANGE_ITEM (GameDefine.h).</summary>
public sealed class ExchangeItem
{
    public int ItemId;
    public int Count;
    public short Duration;
    public byte Pos;
    public long SerialNum;
}

/// <summary>
/// The CUser exchange slice (User.cpp ExchangeProcess and friends): the
/// user-to-user trade with the inventory mirror backup.
/// </summary>
public sealed partial class GameUser
{
    // e_ExchangeOpcode (shared/packets.h).
    public const byte ExchangeReqCmd = 1;
    public const byte ExchangeAgreeCmd = 2;
    public const byte ExchangeAddCmd = 3;
    public const byte ExchangeOtherAdd = 4;
    public const byte ExchangeDecideCmd = 5;
    public const byte ExchangeOtherDecide = 6;
    public const byte ExchangeDoneCmd = 7;
    public const byte ExchangeCancelCmd = 8;

    private const byte ItemLogExchangePut = 4; // ITEM_LOG_EXCHANGE_PUT
    private const byte ItemLogExchangeGet = 5; // ITEM_LOG_EXCHANGE_GET

    /// <summary>m_bExchangeOK.</summary>
    public byte ExchangeOk;

    /// <summary>m_MirrorItem — the inventory backup while the trade window is open.</summary>
    public readonly ItemData[] MirrorItem = new ItemData[GameConstants.HaveMax];

    /// <summary>m_ExchangeItemList.</summary>
    public readonly List<ExchangeItem> ExchangeItemList = [];

    /// <summary>CUser::ExchangeProcess — WIZ_EXCHANGE dispatch.</summary>
    public void ExchangeProcess(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte subcommand = reader.GetByte();

        switch (subcommand)
        {
            case ExchangeReqCmd:
                ExchangeReq(body[1..]);
                break;

            case ExchangeAgreeCmd:
                ExchangeAgree(body[1..]);
                break;

            case ExchangeAddCmd:
                ExchangeAdd(body[1..]);
                break;

            case ExchangeDecideCmd:
                ExchangeDecide();
                break;

            case ExchangeCancelCmd:
                ExchangeCancel();
                break;
        }
    }

    /// <summary>CUser::ExchangeReq.</summary>
    public void ExchangeReq(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        short destId = reader.GetShort();

        GameUser? partner = null;

        bool fail = UserData is not { } user || ResHpType == UserDeadResHpType || user.Hp == 0;
        if (!fail)
        {
            partner = destId >= 0 && destId < world.Users.Length ? world.Users[destId] : null;
            if (partner is null
                || partner.ExchangeUser != -1
                || partner.UserData?.Nation != UserData!.Nation)
                fail = true;
        }

        if (fail || partner is null)
        {
            var failBuffer = new byte[4];
            var failWriter = new PacketWriter(failBuffer);
            failWriter.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
            failWriter.SetByte(ExchangeCancelCmd);
            Send(failWriter.Written);
            return;
        }

        ExchangeUser = destId;
        partner.ExchangeUser = SocketId;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        writer.SetByte(ExchangeReqCmd);
        writer.SetShort(SocketId);
        partner.Send(writer.Written);
    }

    /// <summary>CUser::ExchangeAgree.</summary>
    public void ExchangeAgree(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte result = reader.GetByte();

        GameUser? partner = GetExchangePartner();
        if (partner is null)
        {
            ExchangeUser = -1;
            return;
        }

        if (result == 0)
        {
            ExchangeUser = -1;
            partner.ExchangeUser = -1;
        }
        else
        {
            InitExchange(true);
            partner.InitExchange(true);
        }

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        writer.SetByte(ExchangeAgreeCmd);
        writer.SetShort(result); // the C++ writes the byte through SetShort
        partner.Send(writer.Written);
    }

    /// <summary>CUser::ExchangeAdd.</summary>
    public void ExchangeAdd(ReadOnlySpan<byte> body)
    {
        GameUser? partner = GetExchangePartner();
        if (partner is null)
        {
            ExchangeCancel();
            return;
        }

        var reader = new PacketReader(body);
        byte pos = reader.GetByte();
        var itemId = (int)reader.GetDWord();
        var count = (int)reader.GetDWord();

        int duration = 0;
        bool add = true;

        if (UserData is not { } user
            || world.ItemTable.GetValueOrDefault(itemId) is not { } table
            || (itemId != ItemGold && pos >= GameConstants.HaveMax)
            || ExchangeOk != 0)
        {
            SendExchangeAddFail();
            return;
        }

        if (itemId == ItemGold)
        {
            if (count > user.Gold || count <= 0)
            {
                SendExchangeAddFail();
                return;
            }

            foreach (ExchangeItem entry in ExchangeItemList)
            {
                if (entry.ItemId == ItemGold)
                {
                    entry.Count += count;
                    user.Gold -= count;
                    add = false;
                    break;
                }
            }

            if (add)
                user.Gold -= count;
        }
        else if (MirrorItem[pos].Num == itemId)
        {
            if (MirrorItem[pos].Count < count)
            {
                SendExchangeAddFail();
                return;
            }

            if (table.Countable != 0)
            {
                foreach (ExchangeItem entry in ExchangeItemList)
                {
                    if (entry.ItemId == itemId)
                    {
                        entry.Count += count;
                        MirrorItem[pos].Count -= (short)count;
                        add = false;
                        break;
                    }
                }
            }

            if (add)
                MirrorItem[pos].Count -= (short)count;

            duration = MirrorItem[pos].Duration;

            if (MirrorItem[pos].Count <= 0 || table.Countable == 0)
                MirrorItem[pos] = default;
        }
        else
        {
            SendExchangeAddFail();
            return;
        }

        bool gold = ExchangeItemList.Any(entry => entry.ItemId == ItemGold);

        // C++ quirk kept as-is: the cap check runs AFTER the gold/mirror deduction,
        // so an over-cap add still burns the offered gold/stack.
        if (ExchangeItemList.Count > (gold ? 13 : 12))
        {
            SendExchangeAddFail();
            return;
        }

        if (add)
        {
            // The C++ reads m_MirrorItem[pos] for the serial even on the gold
            // path, where pos can exceed HAVE_MAX (an out-of-bounds read); the
            // port clamps that to 0 instead of reading random memory.
            long serial = itemId != ItemGold || pos < GameConstants.HaveMax ? MirrorItem[pos].SerialNum : 0;
            ExchangeItemList.Add(new ExchangeItem
            {
                ItemId = itemId,
                Duration = (short)duration,
                Count = count,
                SerialNum = serial,
            });
        }

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        writer.SetByte(ExchangeAddCmd);
        writer.SetByte(0x01);
        Send(writer.Written);

        var otherBuffer = new byte[16];
        var otherWriter = new PacketWriter(otherBuffer);
        otherWriter.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        otherWriter.SetByte(ExchangeOtherAdd);
        otherWriter.SetDWord((uint)itemId);
        otherWriter.SetDWord((uint)count);
        otherWriter.SetShort(duration);
        partner.Send(otherWriter.Written);
    }

    private void SendExchangeAddFail()
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        writer.SetByte(ExchangeAddCmd);
        writer.SetByte(0x00);
        Send(writer.Written);
    }

    /// <summary>CUser::ExchangeDecide.</summary>
    public void ExchangeDecide()
    {
        GameUser? partner = GetExchangePartner();
        if (partner is null)
        {
            ExchangeCancel();
            return;
        }

        if (partner.ExchangeOk == 0)
        {
            ExchangeOk = 0x01;

            var waitBuffer = new byte[4];
            var waitWriter = new PacketWriter(waitBuffer);
            waitWriter.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
            waitWriter.SetByte(ExchangeOtherDecide);
            partner.Send(waitWriter.Written);
            return;
        }

        bool success = true;
        if (!ExecuteExchange() || !partner.ExecuteExchange())
        {
            // Only the gold is restored on failure; offered items stay burned
            // out of the mirror (C++ behavior).
            RestoreExchangeGold();
            partner.RestoreExchangeGold();
            success = false;
        }

        if (success)
        {
            int getMoney = ExchangeDone();
            int putMoney = partner.ExchangeDone();

            if (UserData is { } user && partner.UserData is { } partnerData)
            {
                if (getMoney > 0)
                    ItemLogToAgent(user.CharId, partnerData.CharId, ItemLogExchangeGet, 0, ItemGold, getMoney, 0);

                if (putMoney > 0)
                    ItemLogToAgent(user.CharId, partnerData.CharId, ItemLogExchangePut, 0, ItemGold, putMoney, 0);

                SendExchangeDone(this, partner, ItemLogExchangeGet);
                SendExchangeDone(partner, this, ItemLogExchangePut);

                SendItemWeight();
                partner.SendItemWeight();
            }
        }
        else
        {
            var failBuffer = new byte[4];
            var failWriter = new PacketWriter(failBuffer);
            failWriter.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
            failWriter.SetByte(ExchangeDoneCmd);
            failWriter.SetByte(0);
            Send(failWriter.Written);
            partner.Send(failWriter.Written);
        }

        InitExchange(false);
        partner.InitExchange(false);
    }

    /// <summary>
    /// The EXCHANGE_DONE packet for <paramref name="receiver"/> listing what
    /// <paramref name="giver"/> handed over, plus the per-item log lines.
    /// </summary>
    private void SendExchangeDone(GameUser receiver, GameUser giver, byte logType)
    {
        if (receiver.UserData is not { } user || giver.UserData is not { } giverData)
            return;

        var buffer = new byte[16 + giver.ExchangeItemList.Count * 16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        writer.SetByte(ExchangeDoneCmd);
        writer.SetByte(0x01);
        writer.SetDWord((uint)user.Gold);
        writer.SetShort((short)giver.ExchangeItemList.Count);

        foreach (ExchangeItem entry in giver.ExchangeItemList)
        {
            writer.SetByte(entry.Pos);
            writer.SetDWord((uint)entry.ItemId);
            writer.SetShort(entry.Count);
            writer.SetShort(entry.Duration);

            // Both log lines carry (my char, partner char) in the C++ — the
            // receiver/giver order is expressed only through the type.
            ItemLogToAgent(UserData?.CharId ?? string.Empty, GetExchangePartner()?.UserData?.CharId ?? giverData.CharId,
                logType, entry.SerialNum, entry.ItemId, entry.Count, entry.Duration);
        }

        receiver.Send(writer.Written);
    }

    /// <summary>The failure path's gold-only backup (repeated in the C++).</summary>
    private void RestoreExchangeGold()
    {
        if (UserData is not { } user)
            return;

        foreach (ExchangeItem entry in ExchangeItemList)
        {
            if (entry.ItemId == ItemGold)
            {
                user.Gold += entry.Count;
                break;
            }
        }
    }

    /// <summary>CUser::ExchangeCancel.</summary>
    public void ExchangeCancel()
    {
        GameUser? partner = GetExchangePartner();

        RestoreExchangeGold();
        InitExchange(false);

        if (partner is not null)
        {
            partner.ExchangeCancel();

            var buffer = new byte[4];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
            writer.SetByte(ExchangeCancelCmd);
            partner.Send(writer.Written);
        }
    }

    /// <summary>CUser::InitExchange — start backs up the inventory, end clears the state.</summary>
    public void InitExchange(bool start)
    {
        ExchangeItemList.Clear();

        if (start)
        {
            if (UserData is not { } user)
                return;

            for (int i = 0; i < GameConstants.HaveMax; i++)
                MirrorItem[i] = user.Items[GameConstants.SlotMax + i];
        }
        else
        {
            ExchangeUser = -1;
            ExchangeOk = 0;
            Array.Clear(MirrorItem);
        }
    }

    /// <summary>CUser::ExecuteExchange — places the partner's items into my mirror.</summary>
    public bool ExecuteExchange()
    {
        GameUser? partner = GetExchangePartner();
        if (partner is null)
            return false;

        short weight = 0;
        int i = 0;

        foreach (ExchangeItem entry in partner.ExchangeItemList)
        {
            if (entry.ItemId >= ItemNoTrade)
                return false;

            if (entry.ItemId == ItemGold)
                continue;

            Item? table = world.ItemTable.GetValueOrDefault(entry.ItemId);
            if (table is null)
                continue; // C++ quirk: i keeps its previous value for the final check

            for (i = 0; i < GameConstants.HaveMax; i++)
            {
                if (MirrorItem[i].Num == 0 && table.Countable == 0)
                {
                    MirrorItem[i].Num = entry.ItemId;
                    MirrorItem[i].Duration = entry.Duration;
                    MirrorItem[i].Count = (short)entry.Count;
                    MirrorItem[i].SerialNum = entry.SerialNum;

                    entry.Pos = (byte)i;
                    weight += (short)table.Weight;
                    break;
                }

                if (MirrorItem[i].Num == entry.ItemId && table.Countable == 1)
                {
                    MirrorItem[i].Count += (short)entry.Count;
                    if (MirrorItem[i].Count > MaxItemCount)
                        MirrorItem[i].Count = MaxItemCount;

                    entry.Pos = (byte)i;
                    weight += (short)(table.Weight * entry.Count);
                    break;
                }
            }

            // Countable item the user does not stack yet: first empty slot.
            // (The C++ does not copy the serial here — kept as-is.)
            if (i == GameConstants.HaveMax && table.Countable == 1)
            {
                for (i = 0; i < GameConstants.HaveMax; i++)
                {
                    if (MirrorItem[i].Num != 0)
                        continue;

                    MirrorItem[i].Num = entry.ItemId;
                    MirrorItem[i].Duration = entry.Duration;
                    MirrorItem[i].Count = (short)entry.Count;

                    entry.Pos = (byte)i;
                    weight += (short)(table.Weight * entry.Count);
                    break;
                }
            }

            if (i == GameConstants.HaveMax)
                return false; // no free inventory slot
        }

        return weight + ItemWeight <= MaxWeight;
    }

    /// <summary>
    /// CUser::ExchangeDone — takes the partner's gold entry off their list and
    /// restores my mirror into the live inventory; returns the received gold.
    /// </summary>
    public int ExchangeDone()
    {
        GameUser? partner = GetExchangePartner();
        if (partner is null || UserData is not { } user)
            return 0;

        int money = 0;
        for (int j = partner.ExchangeItemList.Count - 1; j >= 0; j--)
        {
            if (partner.ExchangeItemList[j].ItemId == ItemGold)
            {
                money = partner.ExchangeItemList[j].Count;
                partner.ExchangeItemList.RemoveAt(j);
            }
        }

        if (money > 0)
            user.Gold += money;

        for (int i = 0; i < GameConstants.HaveMax; i++)
        {
            user.Items[GameConstants.SlotMax + i] = MirrorItem[i];

            Item? table = world.ItemTable.GetValueOrDefault(user.Items[GameConstants.SlotMax + i].Num);
            if (table is null)
                continue;

            if (table.Countable == 0 && user.Items[GameConstants.SlotMax + i].SerialNum == 0)
                user.Items[GameConstants.SlotMax + i].SerialNum = world.GenerateItemSerial();
        }

        return money;
    }

    private GameUser? GetExchangePartner()
        => ExchangeUser >= 0 && ExchangeUser < world.Users.Length ? world.Users[ExchangeUser] : null;
}
