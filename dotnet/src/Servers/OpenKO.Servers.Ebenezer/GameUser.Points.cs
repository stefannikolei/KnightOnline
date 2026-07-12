using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser stat/skill point slice (User.cpp): WIZ_POINT_CHANGE,
/// WIZ_SKILLPT_CHANGE, the WIZ_CLASS_CHANGE family and the GM
/// weather/time updates.
/// </summary>
public sealed partial class GameUser
{
    // e_StatType (GameDefine.h).
    private const byte StatTypeStr = 1;
    private const byte StatTypeSta = 2;
    private const byte StatTypeDex = 3;
    private const byte StatTypeIntel = 4;
    private const byte StatTypeCha = 5;

    // e_ClassChangeOpcode (shared/packets.h).
    public const byte ClassChangeReqCmd = 0x01;
    public const byte ClassChangeResult = 0x02;
    public const byte AllPointChangeCmd = 0x03;
    public const byte AllSkillPtChangeCmd = 0x04;
    public const byte ChangeMoneyReq = 0x05;

    // Class codes (GameDefine.h).
    private const int KarusWarrior = 101;
    private const int KarusRogue = 102;
    private const int KarusWizard = 103;
    private const int KarusPriest = 104;
    private const int Berserker = 105;
    private const int Guardian = 106;
    private const int Hunter = 107;
    private const int Penetrator = 108;
    private const int Sorcerer = 109;
    private const int Necromancer = 110;
    private const int Shaman = 111;
    private const int DarkPriest = 112;
    private const int ElmoWarrior = 201;
    private const int ElmoRogue = 202;
    private const int ElmoWizard = 203;
    private const int ElmoPriest = 204;
    private const int Blade = 205;
    private const int Protector = 206;
    private const int Ranger = 207;
    private const int Assassin = 208;
    private const int Mage = 209;
    private const int Enchanter = 210;
    private const int Cleric = 211;
    private const int Druid = 212;

    // Races (GameDefine.h).
    private const int KarusBig = 1;
    private const int KarusMiddle = 2;
    private const int KarusSmall = 3;
    private const int KarusWoman = 4;
    private const int Barbarian = 11;
    private const int ElmoradMan = 12;
    private const int ElmoradWoman = 13;

    /// <summary>CUser::PointChange — spend one free stat point.</summary>
    public void PointChange(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        int value = reader.GetShort();

        if (type > 5 || System.Math.Abs(value) > 1)
            return;

        if (user.Points < 1)
            return;

        switch (type)
        {
            case StatTypeStr when user.Str == 255:
            case StatTypeSta when user.Sta == 255:
            case StatTypeDex when user.Dex == 255:
            case StatTypeIntel when user.Intel == 255:
            case StatTypeCha when user.Cha == 255:
                return;
        }

        // C++ quirk kept as-is: the VALUE (which may be -1) is subtracted, so a
        // crafted -1 refunds a point while still raising the stat.
        user.Points = (byte)(user.Points - value);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_POINT_CHANGE);
        writer.SetByte(type);

        switch (type)
        {
            case StatTypeStr:
                writer.SetShort(++user.Str);
                SetUserAbility();
                break;

            case StatTypeSta:
                writer.SetShort(++user.Sta);
                SetMaxHp();
                SetMaxMp();
                break;

            case StatTypeDex:
                writer.SetShort(++user.Dex);
                SetUserAbility();
                break;

            case StatTypeIntel:
                writer.SetShort(++user.Intel);
                SetMaxMp();
                break;

            case StatTypeCha:
                writer.SetShort(++user.Cha);
                break;
        }

        writer.SetShort(MaxHp);
        writer.SetShort(MaxMp);
        writer.SetShort(TotalHit);
        writer.SetShort(GetMaxWeightForClient());
        Send(writer.Written);
    }

    /// <summary>
    /// CUser::SkillPointChange — spend one free skill point. Success sends NO
    /// packet (the client applies it optimistically); only failure replies.
    /// </summary>
    public void SkillPointChange(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        if (type > 8)
            return;

        if (user.Skills[0] >= 1 && user.Skills[type] + 1 <= user.Level)
        {
            user.Skills[0] -= 1;
            user.Skills[type] += 1;
            return;
        }

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SKILLPT_CHANGE);
        writer.SetByte(type);
        writer.SetByte(user.Skills[type]);
        Send(writer.Written);
    }

    /// <summary>CUser::UpdateGameWeather — GM WIZ_WEATHER/WIZ_TIME broadcast.</summary>
    public void UpdateGameWeather(ReadOnlySpan<byte> body, GameOpcode type)
    {
        if (UserData is not { Authority: GameConstants.AuthorityManager })
            return;

        var reader = new PacketReader(body);

        if (type == GameOpcode.WIZ_WEATHER)
        {
            world.Weather = reader.GetByte();
            world.WeatherAmount = reader.GetShort();

            var buffer = new byte[8];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_WEATHER);
            writer.SetByte((byte)world.Weather);
            writer.SetShort(world.WeatherAmount);
            world.SendAll(writer.Written);
        }
        else if (type == GameOpcode.WIZ_TIME)
        {
            short year = reader.GetShort();
            short month = reader.GetShort();
            short date = reader.GetShort();
            world.Hour = reader.GetShort();
            world.Minute = reader.GetShort();

            var buffer = new byte[16];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_TIME);
            writer.SetShort(year);
            writer.SetShort(month);
            writer.SetShort(date);
            writer.SetShort(world.Hour);
            writer.SetShort(world.Minute);
            world.SendAll(writer.Written);
        }
    }

    /// <summary>CUser::ClassChange — WIZ_CLASS_CHANGE dispatch.</summary>
    public void ClassChange(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte type = reader.GetByte();

        if (type == ClassChangeReqCmd)
        {
            ClassChangeReq();
            return;
        }

        if (type == AllPointChangeCmd)
        {
            AllPointChange();
            return;
        }

        if (type == AllSkillPtChangeCmd)
        {
            AllSkillPointChange();
            return;
        }

        if (type == ChangeMoneyReq)
        {
            byte subType = reader.GetByte();
            int money = ResetCost();

            if (subType == 2)
                money = (int)(money * 1.5); // skill resets cost more

            // Winner discount / global discount.
            if ((world.Discount == 1 && world.OldVictory == user.Nation) || world.Discount == 2)
                money = (int)(money * 0.5);

            if (subType is 1 or 2)
            {
                var moneyBuffer = new byte[8];
                var moneyWriter = new PacketWriter(moneyBuffer);
                moneyWriter.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
                moneyWriter.SetByte(ChangeMoneyReq);
                moneyWriter.SetDWord((uint)money);
                Send(moneyWriter.Written);
            }

            return;
        }

        // Any other type: the next byte is the requested class code.
        int classCode = reader.GetByte();
        bool success = user.Class switch
        {
            KarusWarrior => classCode is Berserker or Guardian,
            KarusRogue => classCode is Hunter or Penetrator,
            KarusWizard => classCode is Sorcerer or Necromancer,
            KarusPriest => classCode is Shaman or DarkPriest,
            ElmoWarrior => classCode is Blade or Protector,
            ElmoRogue => classCode is Ranger or Assassin,
            ElmoWizard => classCode is Mage or Enchanter,
            ElmoPriest => classCode is Cleric or Druid,
            _ => false,
        };

        if (!success)
        {
            var failBuffer = new byte[4];
            var failWriter = new PacketWriter(failBuffer);
            failWriter.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
            failWriter.SetByte(ClassChangeResult);
            failWriter.SetByte(0);
            Send(failWriter.Written);
            return;
        }

        user.Class = (short)classCode;

        if (PartyIndex != -1)
        {
            var partyBuffer = new byte[8];
            var partyWriter = new PacketWriter(partyBuffer);
            partyWriter.SetByte((byte)GameOpcode.WIZ_PARTY);
            partyWriter.SetByte(PartyClassChange);
            partyWriter.SetShort(SocketId);
            partyWriter.SetShort(user.Class);
            world.SendPartyMember(PartyIndex, partyWriter.Written);
        }
    }

    /// <summary>The (level*2)^3.4 reset price with the level brackets.</summary>
    private int ResetCost()
    {
        if (UserData is not { } user)
            return 0;

        var money = (int)System.Math.Pow(user.Level * 2, 3.4);
        money = money / 100 * 100;

        if (user.Level < 30)
            money = (int)(money * 0.4);
        else if (user.Level is >= 60 and <= 90)
            money = (int)(money * 1.5);

        return money;
    }

    /// <summary>CUser::ClassChangeReq — 1 ok, 2 under level 10, 3 already changed.</summary>
    public void ClassChangeReq()
    {
        if (UserData is not { } user)
            return;

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
        writer.SetByte(ClassChangeResult);

        if (user.Level < 10)
            writer.SetByte(2);
        else if (user.Class % 100 > 4)
            writer.SetByte(3);
        else
            writer.SetByte(1);

        Send(writer.Written);
    }

    /// <summary>CUser::AllSkillPointChange — the paid full skill reset.</summary>
    public void AllSkillPointChange()
    {
        if (UserData is not { } user)
            return;

        byte type = 0; // 0 not enough money, 1 success, 2 nothing to reset

        int cost = (int)(ResetCost() * 1.5); // skills cost one bracket more

        if ((world.Discount == 1 && world.OldVictory == user.Nation) || world.Discount == 2)
            cost = (int)(cost * 0.5);

        int money = user.Gold - cost;

        bool fail = money < 0 || user.Level < 10;
        if (!fail)
        {
            int skillPoints = 0;
            for (int i = 1; i < 9; i++)
                skillPoints += user.Skills[i];

            if (skillPoints <= 0)
            {
                type = 2;
                fail = true;
            }
            else
            {
                // The C++ recomputes the pool instead of adding (overflow guard).
                user.Skills[0] = (byte)((user.Level - 9) * 2);
                for (int j = 1; j < 9; j++)
                    user.Skills[j] = 0;

                user.Gold = money;
                type = 1;
            }
        }

        if (!fail)
        {
            var buffer = new byte[12];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
            writer.SetByte(AllSkillPtChangeCmd);
            writer.SetByte(type);
            writer.SetDWord((uint)user.Gold);
            writer.SetByte(user.Skills[0]);
            Send(writer.Written);
            return;
        }

        var failBuffer = new byte[8];
        var failWriter = new PacketWriter(failBuffer);
        failWriter.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
        failWriter.SetByte(AllSkillPtChangeCmd);
        failWriter.SetByte(type);
        failWriter.SetDWord((uint)cost);
        Send(failWriter.Written);
    }

    /// <summary>CUser::AllPointChange — the paid full stat reset.</summary>
    public void AllPointChange()
    {
        if (UserData is not { } user)
            return;

        byte type = 0;
        int cost = 0;
        bool success = false;

        if (user.Level <= 80)
        {
            cost = ResetCost();
            if ((world.Discount == 1 && world.OldVictory == user.Nation) || world.Discount == 2)
                cost = (int)(cost * 0.5);

            int money = user.Gold - cost;
            if (money >= 0)
            {
                bool wearing = false;
                for (int i = 0; i < GameConstants.SlotMax; i++)
                {
                    if (user.Items[i].Num != 0)
                    {
                        type = 0x04; // must undress first
                        wearing = true;
                        break;
                    }
                }

                if (!wearing)
                {
                    // An unknown race matches no case in the C++ switch and
                    // still succeeds (without touching the stats) — kept as-is.
                    (byte str, byte sta, byte dex, byte intel, byte cha)? baseStats = user.Race switch
                    {
                        KarusBig or KarusMiddle or Barbarian => ((byte)65, (byte)65, (byte)60, (byte)50, (byte)50),
                        KarusWoman => ((byte)50, (byte)60, (byte)60, (byte)70, (byte)50),
                        KarusSmall or ElmoradWoman => ((byte)50, (byte)50, (byte)70, (byte)70, (byte)50),
                        ElmoradMan => ((byte)60, (byte)60, (byte)70, (byte)50, (byte)50),
                        _ => null,
                    };

                    bool alreadyBase = baseStats is { } check
                        && user.Str == check.str && user.Sta == check.sta && user.Dex == check.dex
                        && user.Intel == check.intel && user.Cha == check.cha;

                    if (alreadyBase)
                    {
                        type = 2; // already at the base line
                    }
                    else
                    {
                        if (baseStats is { } stats)
                        {
                            (user.Str, user.Sta, user.Dex, user.Intel, user.Cha) =
                                (stats.str, stats.sta, stats.dex, stats.intel, stats.cha);
                        }

                        user.Points = (byte)((user.Level - 1) * 3 + 10);
                        user.Gold = money;

                        SetUserAbility();
                        SendAiUserUpdate();
                        success = true;
                    }
                }
            }
        }

        if (success)
        {
            type = 1;
            var buffer = new byte[32];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
            writer.SetByte(AllPointChangeCmd);
            writer.SetByte(type);
            writer.SetDWord((uint)user.Gold);
            writer.SetShort(user.Str);
            writer.SetShort(user.Sta);
            writer.SetShort(user.Dex);
            writer.SetShort(user.Intel);
            writer.SetShort(user.Cha);
            writer.SetShort(MaxHp);
            writer.SetShort(MaxMp);
            writer.SetShort(TotalHit);
            writer.SetShort(GetMaxWeightForClient());
            writer.SetShort(user.Points);
            Send(writer.Written);
        }

        // C++ quirk kept as-is: the success path has NO return before the
        // fail_return label, so a successful reset also sends this packet.
        var tailBuffer = new byte[8];
        var tailWriter = new PacketWriter(tailBuffer);
        tailWriter.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
        tailWriter.SetByte(AllPointChangeCmd);
        tailWriter.SetByte(type);
        tailWriter.SetDWord((uint)cost);
        Send(tailWriter.Written);
    }
}
