using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser magic-support slice (User.cpp): the per-user CMagicProcess
/// instance, the type-3/type-4 duration bookkeeping ticked after every packet,
/// state changes and the item/job helpers the magic gate needs.
/// </summary>
public sealed partial class GameUser
{
    private MagicProcessor? _magic;

    /// <summary>m_MagicProcess (m_pSrcUser = this).</summary>
    public MagicProcessor Magic => _magic ??= new MagicProcessor(world, this, logger);

    /// <summary>m_sDuration1..9 keyed by buff type (index 0 unused).</summary>
    public readonly short[] DurationType4 = new short[10];

    /// <summary>m_fStartTime1..9 keyed by buff type (index 0 unused).</summary>
    public readonly double[] StartTimeType4 = new double[10];

    /// <summary>m_fLastRegeneTime (battle-zone summon throttle).</summary>
    public double LastRegeneTime;

    /// <summary>m_fHPLastTimeNormal (sit/stand HP regeneration).</summary>
    public double HpLastTimeNormal;

    /// <summary>m_bHPIntervalNormal.</summary>
    public byte HpIntervalNormal = 5;

    private const byte UserSitDown = 2; // USER_SITDOWN

    /// <summary>
    /// The C++ runs this at the end of CUser::Parsing for EVERY received packet
    /// (there is no dedicated timer): sit/stand regeneration, the type-3 DoT
    /// ticks and the type-4 buff expiry.
    /// </summary>
    private void PacketTimerTail(byte opcode)
    {
        if (UserData is null)
            return;

        double currentTime = world.Clock();

        if (opcode == (byte)GameOpcode.WIZ_GAMESTART)
        {
            HpLastTimeNormal = currentTime;

            for (int i = 0; i < HpLastTime.Length; i++)
                HpLastTime[i] = currentTime;
        }

        if (HpLastTimeNormal != 0.0
            && currentTime - HpLastTimeNormal > HpIntervalNormal
            && AbnormalType != AbnormalBlinking)
            HpTimeChange(currentTime);

        if (Type3Flag)
        {
            for (int i = 0; i < HpLastTime.Length; i++)
            {
                if (HpLastTime[i] != 0.0 && currentTime - HpLastTime[i] > HpInterval[i])
                {
                    HpTimeChangeType3(currentTime);
                    break;
                }
            }
        }

        if (Type4Flag)
            Type4DurationTick(currentTime);

        // Should you stop blinking?
        if (AbnormalType == AbnormalBlinking)
            BlinkTimeCheck(currentTime);
    }

    /// <summary>CUser::HPTimeChange — passive HP/MP regeneration.</summary>
    public void HpTimeChange(double currentTime)
    {
        HpLastTimeNormal = currentTime;

        if (UserData is not { } user)
            return;

        if (ResHpType == UserDeadResHpType)
            return;

        if (user.Zone == ZoneSnowBattle && world.BattleOpen == SnowBattle)
        {
            if (user.Hp < 1)
                return;

            HpChange(5);
            return;
        }

        if (ResHpType == UserStanding)
        {
            if (user.Hp < 1)
                return;

            if (MaxHp != user.Hp)
                HpChange((int)((user.Level * (1 + user.Level / 60.0) + 1) * 0.2) + 3);

            if (MaxMp != user.Mp)
                MSpChange((int)((user.Level * (1 + user.Level / 60.0) + 1) * 0.2) + 3);
        }
        else if (ResHpType == UserSitDown)
        {
            if (user.Hp < 1)
                return;

            if (MaxHp != user.Hp)
                HpChange((int)(user.Level * (1 + user.Level / 30.0)) + 3);

            if (MaxMp != user.Mp)
                MSpChange((int)(MaxMp * 5 / ((user.Level - 1) + 30)) + 3);
        }
    }

    /// <summary>CUser::HPTimeChangeType3 — apply the DoT/HoT slots and expire them.</summary>
    public void HpTimeChangeType3(double currentTime)
    {
        if (UserData is not { } user)
            return;

        for (int i = 0; i < HpLastTime.Length; i++)
            HpLastTime[i] = currentTime;

        if (ResHpType == UserDeadResHpType)
            return;

        for (int h = 0; h < HpAmount.Length; h++)
        {
            HpChange(HpAmount[h]);

            GameUser? source = SourceId[h] >= 0 && SourceId[h] < world.Users.Length
                ? world.Users[SourceId[h]]
                : null;
            source?.SendTargetHP(0, SocketId, HpAmount[h]);

            if (user.Hp == 0)
            {
                ResHpType = UserDeadResHpType;

                // Killed by an NPC DoT.
                if (SourceId[h] >= EbenezerWorld.NpcBand)
                {
                    if (user.Zone != user.Nation && user.Zone < 3)
                        ExpChange(-MaxExp / 100);
                    else
                        ExpChange(-MaxExp / 20);
                }
                else if (source is not null)
                {
                    if (source.PartyIndex == -1)
                        source.LoyaltyChange(SocketId);
                    else
                        source.LoyaltyDivide(SocketId);

                    source.GoldChange(SocketId, 0);
                }

                short killer = SourceId[h];

                InitType3();
                InitType4();

                if (killer is >= 0 and < EbenezerWorld.MaxUser)
                {
                    WhoKilledMe = killer;

                    if (user.Zone != user.Nation && user.Zone < 3)
                        ExpChange(-MaxExp / 100);
                }

                break;
            }
        }

        // Expire finished slots.
        for (int i = 0; i < HpDuration.Length; i++)
        {
            if (HpDuration[i] > 0
                && (currentTime - HpStartTime[i] >= HpDuration[i] || ResHpType == UserDeadResHpType))
            {
                var buffer = new byte[8];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
                writer.SetByte(MagicProcessor.MagicType3End);
                writer.SetByte(HpAmount[i] > 0 ? (byte)100 : (byte)200);
                Send(writer.Written);

                HpStartTime[i] = 0.0;
                HpLastTime[i] = 0.0;
                HpAmount[i] = 0;
                HpDuration[i] = 0;
                HpInterval[i] = 5;
                SourceId[i] = -1;
            }
        }

        int remaining = 0;
        foreach (byte duration in HpDuration)
            remaining += duration;

        if (remaining == 0)
            Type3Flag = false;

        // WIZ_PARTY/PARTY_STATUSCHANGE attaches with the party slice.
    }

    /// <summary>CUser::Type4Duration — expire at most one buff per tick.</summary>
    public void Type4DurationTick(double currentTime)
    {
        byte expired = 0;

        for (byte buffType = 1; buffType <= 9 && expired == 0; buffType++)
        {
            if (DurationType4[buffType] == 0)
                continue;

            if (currentTime <= StartTimeType4[buffType] + DurationType4[buffType])
                continue;

            DurationType4[buffType] = 0;
            StartTimeType4[buffType] = 0.0;
            expired = buffType;

            switch (buffType)
            {
                case 1: MaxHpAmount = 0; break;
                case 2: AcAmount = 0; break;
                case 3:
                    StateChange([3, 1]); // ABNORMAL_NORMAL
                    break;
                case 4: AttackAmount = 100; break;
                case 5: AttackSpeedAmount = 100; break;
                case 6: SpeedAmount = 100; break;
                case 7:
                    StrAmount = 0;
                    StaAmount = 0;
                    DexAmount = 0;
                    IntelAmount = 0;
                    ChaAmount = 0;
                    break;
                case 8:
                    FireRAmount = 0;
                    ColdRAmount = 0;
                    LightningRAmount = 0;
                    MagicRAmount = 0;
                    DiseaseRAmount = 0;
                    PoisonRAmount = 0;
                    break;
                case 9:
                    HitRateAmount = 100;
                    AvoidRateAmount = 100;
                    break;
            }
        }

        if (expired != 0)
        {
            Type4Buff[expired - 1] = 0;

            SetSlotItemValue();
            SetUserAbility();
            SendAiUserUpdate();

            var buffer = new byte[8];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
            writer.SetByte(MagicProcessor.MagicType4End);
            writer.SetByte(expired);
            Send(writer.Written);
        }

        int remaining = 0;
        foreach (byte buff in Type4Buff)
            remaining += buff;

        if (remaining == 0)
            Type4Flag = false;

        // WIZ_PARTY/PARTY_STATUSCHANGE attaches with the party slice.
    }

    /// <summary>CUser::StateChange — sit/stand, party flag, abnormal type.</summary>
    public void StateChange(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        byte buff = reader.GetByte();

        if (type > 5)
            return;

        // Operators only.
        if (type == 5 && user.Authority != GameConstants.AuthorityManager)
            return;

        if (type == 1)
            ResHpType = buff;
        else if (type == 2)
            NeedParty = buff;
        else if (type == 3)
            AbnormalType = buff;

        uint result = type switch
        {
            1 => ResHpType,
            2 => NeedParty,
            3 => AbnormalType,
            _ => buff,
        };

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_STATE_CHANGE);
        writer.SetShort(SocketId);
        writer.SetByte(type);
        writer.SetDWord(result);
        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ);
    }

    /// <summary>CUser::JobGroupCheck — job group vs concrete class ids.</summary>
    public bool JobGroupCheck(short jobGroupId)
    {
        if (UserData is not { } user)
            return false;

        if (jobGroupId >= 100)
            return user.Class == jobGroupId;

        // Karus 101.. / El Morad 201.. class ids (shared/globals.h).
        return jobGroupId switch
        {
            1 => user.Class is 101 or 105 or 106 or 201 or 205 or 206,          // warriors
            2 => user.Class is 102 or 107 or 108 or 202 or 207 or 208,          // rogues
            3 => user.Class is 103 or 109 or 110 or 203 or 209 or 210,          // mages
            4 => user.Class is 104 or 111 or 112 or 204 or 211 or 212,          // clerics
            5 => user.Class is 105 or 205,                                      // attack warriors
            6 => user.Class is 106 or 206,                                      // defense warriors
            7 => user.Class is 107 or 207,                                      // archers
            8 => user.Class is 108 or 208,                                      // assassins
            9 => user.Class is 109 or 209,                                      // attack mages
            10 => user.Class is 110 or 210,                                     // pet mages
            11 => user.Class is 111 or 211,                                     // heal clerics
            12 => user.Class is 112 or 212,                                     // curse clerics
            _ => false,
        };
    }

    /// <summary>
    /// CUser::ItemCountChange. Returns 0 (no such item), 1 (insufficient count)
    /// or 2 (success). type 0 scans the equip slots, 1 the inventory (with the
    /// original overlapping loop bounds).
    /// </summary>
    public byte ItemCountChange(int itemId, int type, int amount)
    {
        if (UserData is not { } user)
            return 0;

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
            return 0;

        byte result = 0;
        int slot = -1;

        for (int i = GameConstants.SlotMax * type; i < GameConstants.SlotMax + GameConstants.HaveMax * type; i++)
        {
            if (user.Items[i].Num != itemId)
                continue;

            if (table.RequiredDexterity > user.Dex + ItemDex + DexAmount && table.RequiredDexterity != 0)
                return result;

            if (table.RequiredStrength > user.Str + ItemStr + StrAmount && table.RequiredStrength != 0)
                return result;

            if (table.RequiredStamina > user.Sta + ItemSta + StaAmount && table.RequiredStamina != 0)
                return result;

            if (table.RequiredIntelligence > user.Intel + ItemIntel + IntelAmount && table.RequiredIntelligence != 0)
                return result;

            if (table.RequiredCharisma > user.Cha + ItemCham + ChaAmount && table.RequiredCharisma != 0)
                return result;

            if (table.Countable == 0)
                return 2;

            slot = i;
            user.Items[i].Count -= (short)amount;

            if (user.Items[i].Count == 0)
            {
                user.Items[i].Num = 0;
                result = 2;
                break;
            }

            if (user.Items[i].Count < 0)
            {
                user.Items[i].Count += (short)amount;
                result = 1;
                break;
            }

            result = 2;
        }

        if (result < 2)
            return result;

        SendItemWeight();

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_COUNT_CHANGE);
        writer.SetShort(1);
        writer.SetByte((byte)type);
        writer.SetByte((byte)(slot - type * GameConstants.SlotMax));
        writer.SetDWord((uint)itemId);
        writer.SetDWord((uint)user.Items[slot].Count);
        Send(writer.Written);

        return result;
    }

    /// <summary>CUser::GoldGain — capped money gain with the WIZ_GOLD_CHANGE notice.</summary>
    public void GoldGain(int gold)
    {
        if (UserData is not { } user)
            return;

        if (user.Gold < 0)
        {
            logger.LogError("GoldGain: user has negative gold [charId={CharId} gold={Gold}]", user.CharId, user.Gold);
            return;
        }

        if (gold < 0)
            gold = 0;

        long total = (long)user.Gold + gold;
        if (total > 2_100_000_000) // MAX_GOLD
            total = 2_100_000_000;

        user.Gold = (int)total;

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_GOLD_CHANGE);
        writer.SetByte(GoldChangeGain);
        writer.SetDWord((uint)gold);
        writer.SetDWord((uint)user.Gold);
        Send(writer.Written);
    }
}
