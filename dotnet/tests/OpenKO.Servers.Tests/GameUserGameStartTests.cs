using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>Tests for the GAMESTART slice: item stats, ability formulas and the packet burst.</summary>
public class GameUserGameStartTests
{
    private static Item MakeItem(int id, byte kind = 0, short damage = 0, short armor = 0,
        short maxHpBonus = 0, short strengthBonus = 0, short weight = 0, byte countable = 0) => new()
    {
        ID = id,
        Name = $"item{id}",
        Kind = kind,
        Slot = 0,
        Race = 0,
        ClassId = 0,
        Damage = damage,
        Delay = 10,
        Range = 0,
        Weight = weight,
        Durability = 5000,
        BuyPrice = 0,
        SellPrice = 0,
        Armor = armor,
        Countable = countable,
        MagicEffect = 0,
        SpecialEffect = 0,
        MinLevel = 1,
        MaxLevel = 83,
        RequiredRank = 0,
        RequiredTitle = 0,
        RequiredStrength = 0,
        RequiredStamina = 0,
        RequiredDexterity = 0,
        RequiredIntelligence = 0,
        RequiredCharisma = 0,
        SellingGroup = 0,
        Type = 0,
        HitRate = 0,
        EvasionRate = 0,
        DaggerArmor = 0,
        SwordArmor = 0,
        MaceArmor = 0,
        AxeArmor = 0,
        SpearArmor = 0,
        BowArmor = 0,
        FireDamage = 0,
        IceDamage = 0,
        LightningDamage = 0,
        PoisonDamage = 0,
        HpDrain = 0,
        MpDamage = 0,
        MpDrain = 0,
        MirrorDamage = 0,
        DropRate = 0,
        StrengthBonus = strengthBonus,
        StaminaBonus = 0,
        DexterityBonus = 0,
        IntelligenceBonus = 0,
        CharismaBonus = 0,
        MaxHpBonus = maxHpBonus,
        MaxMpBonus = 0,
        FireResist = 0,
        ColdResist = 0,
        LightningResist = 0,
        MagicResist = 0,
        PoisonResist = 0,
        CurseResist = 0,
    };

    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new ZoneMeta(1, 21));
        world.CoefficientTable[105] = new Coefficient
        {
            ClassId = 105,
            ShortSword = 0,
            Sword = 0.005,
            Axe = 0,
            Club = 0,
            Spear = 0,
            Pole = 0,
            Staff = 0,
            Bow = 0,
            HitPoint = 0.1,
            ManaPoint = 0.05,
            Sp = 0,
            Armor = 0.5,
            HitRate = 0,
            EvasionRate = 0,
        };
        world.LevelUpTable[10] = 1000;
        world.ItemTable[810210000] = MakeItem(810210000, kind: 21, damage: 100, weight: 15); // sword
        world.ItemTable[220100000] = MakeItem(220100000, armor: 30, maxHpBonus: 50, weight: 5); // pauldron
        return world;
    }

    private static (GameUser User, List<byte[]> Frames, FakeDbAgent Db) MakeInGameUser(EbenezerWorld world)
    {
        var db = new FakeDbAgent();
        var frames = new List<byte[]>();
        short id = world.Register(i => new GameUser(i, world, db, NullLogger.Instance));
        GameUser user = world.Users[id]!;
        user.Transmit = frame =>
        {
            frames.Add(frame);
            return true;
        };

        UserData data = db.Users.Get(id)!;
        data.AccountId = "acct";
        data.CharId = "Hero";
        data.Zone = 21;
        data.Nation = 1;
        data.Class = 105;
        data.Level = 10;
        data.Str = 70;
        data.Sta = 60;
        data.Dex = 50;
        data.Intel = 50;
        data.Cha = 50;
        data.Hp = 100;
        data.Mp = 100;
        data.CurX = 500f;
        data.CurZ = 500f;
        data.Items[GameConstants.SlotRightHand].Num = 810210000;
        data.Items[GameConstants.SlotRightHand].Duration = 5000;
        data.Items[GameConstants.SlotBreast].Num = 220100000;
        data.Items[GameConstants.SlotBreast].Duration = 5000;
        user.UserData = data;

        return (user, frames, db);
    }

    private static byte[] Unframe(byte[] frame)
    {
        int len = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        return frame.AsSpan(4, len).ToArray();
    }

    [Fact]
    public void SetDetailData_ComputesStatsFromCoefficientsAndItems()
    {
        EbenezerWorld world = MakeWorld();
        (GameUser user, _, _) = MakeInGameUser(world);

        user.SetDetailData();

        // Item bonuses: sword damage 100 → ItemHit; pauldron Ac 30, MaxHp +50.
        Assert.Equal(100, user.ItemHit);
        Assert.Equal(30, user.ItemAc);
        Assert.Equal(50, user.ItemMaxHp);
        Assert.Equal(20, user.ItemWeight); // 15 + 5

        // TotalHit = 0.005*100*(70+40) + 0.005*100*10*70 + 3 = 55 + 350 + 3.
        Assert.Equal(408, user.TotalHit);

        // TotalAc = 0.5 * (level 10 + item 30) = 20.
        Assert.Equal(20, user.TotalAc);

        // MaxHp = 0.1*10*10*60 + 0.1*10*60/… = 600 + 60 + 12 + 50 = 722.
        Assert.Equal(722, user.MaxHp);

        // MaxMp = 0.05*10*10*(50+30) + 0.1*10*2*80 + 80/5 + 20 = 400+160+16+20 = 596.
        Assert.Equal(596, user.MaxMp);

        Assert.Equal(1000, user.MaxExp);        // LEVEL_UP[10]
        Assert.Equal(3500, user.MaxWeight);     // 70 * 50
        Assert.Equal(0, user.ZoneIndex);
        Assert.Equal(10, user.RegionX);         // 500 / 48
    }

    [Fact]
    public void BrokenItems_OnlyGiveHalfStats()
    {
        EbenezerWorld world = MakeWorld();
        (GameUser user, _, _) = MakeInGameUser(world);
        user.UserData!.Items[GameConstants.SlotRightHand].Duration = 0; // broken sword

        user.SetSlotItemValue();

        Assert.Equal(50, user.ItemHit); // Damage / 2
    }

    [Fact]
    public async Task GameStart_Loading_SendsTheFullPacketBurst()
    {
        EbenezerWorld world = MakeWorld();
        world.Notices[0] = "Willkommen";
        world.Year = 1;
        world.Weather = 2;
        (GameUser user, List<byte[]> frames, _) = MakeInGameUser(world);
        user.SetDetailData();
        frames.Clear();

        var aiPackets = new List<(int Zone, byte[] Data)>();
        world.SendToAiServer = (zone, data) => aiPackets.Add((zone, data));

        await user.ParsingAsync([0x0D, 0x01]); // WIZ_GAMESTART, loading

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Equal(
            new byte[] { 0x0E, 0x5E, 0x2E, 0x13, 0x14, 0x0D },
            payloads.Select(p => p[0]).ToArray()); // MYINFO, ZONEABILITY, NOTICE, TIME, WEATHER, GAMESTART

        // WIZ_MYINFO header: [sid i16][len1 "Hero"][x*10][z*10]...
        byte[] myInfo = payloads[0];
        Assert.Equal(user.SocketId, BinaryPrimitives.ReadInt16LittleEndian(myInfo.AsSpan(1)));
        Assert.Equal(4, myInfo[3]);
        Assert.Equal((ushort)5000, BinaryPrimitives.ReadUInt16LittleEndian(myInfo.AsSpan(8))); // x*10

        // Notice count 1 + text.
        Assert.Equal(1, payloads[2][1]);

        // AG_USER_INFO went to the AI server for zone 21.
        (int aiZone, byte[] aiData) = Assert.Single(aiPackets);
        Assert.Equal(21, aiZone);
        Assert.Equal(OpenKO.Core.Protocol.AiOpcode.AG_USER_INFO, aiData[0]);
    }

    [Fact]
    public async Task GameStart_Finished_ComputesLostExpAndAppliesDeathPenalty()
    {
        EbenezerWorld world = MakeWorld();
        (GameUser user, List<byte[]> frames, _) = MakeInGameUser(world);
        user.SetDetailData();
        user.UserData!.City = 23; // died: 3% penalty digit, no-halving digit
        frames.Clear();

        await user.ParsingAsync([0x0D, 0x02]);

        Assert.Equal(ConnectionState.GameStart, user.State);
        // level 10 → --level → LEVEL_UP row for level 10 (array[9]) = 1000; 1000*3/100 = 30.
        Assert.Equal(30, user.LostExp);
        // The death penalty drains the HP: WIZ_HP_CHANGE went out, then SetMaxHp floors HP at 5.
        Assert.Contains(frames, f => Unframe(f)[0] == 0x17);
        Assert.Equal(5, user.UserData.Hp);
    }

    [Fact]
    public async Task GameStart_IgnoredOnceInGame()
    {
        EbenezerWorld world = MakeWorld();
        (GameUser user, List<byte[]> frames, _) = MakeInGameUser(world);
        user.SetDetailData();
        user.State = ConnectionState.GameStart;
        frames.Clear();

        await user.ParsingAsync([0x0D, 0x01]);

        Assert.Empty(frames);
    }
}
