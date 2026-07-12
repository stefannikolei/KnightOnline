using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.GameData.Maps;
using OpenKO.Servers.AIServer.Ai;
using Xunit;
using Npc = OpenKO.Servers.AIServer.Ai.Npc;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the CMagicProcess port (UserMagicProcessor): the AG_MAGIC_ATTACK_REQ
/// payload flow, Type3 heal + duration slots, the Type4 speed buff, Type7 sleep
/// and the area attack sweep.
/// </summary>
public class UserMagicProcessorTests
{
    private const int NpcBand = 10000;
    private const byte MagicEffecting = 3;
    private const byte MagicFail = 4;

    private static Magic MakeMagic(int id, byte type1, byte moral = 0) => new()
    {
        ID = id,
        BeforeAction = 0,
        TargetAction = 0,
        SelfEffect = 0,
        FlyingEffect = 0,
        TargetEffect = 0,
        Moral = moral,
        SkillLevel = 0,
        Skill = 0,
        ManaCost = 0,
        HpCost = 0,
        ItemGroup = 0,
        UseItem = 0,
        CastTime = 0,
        RecastTime = 0,
        SuccessRate = 100,
        Type1 = type1,
        Type2 = 0,
        Range = 0,
        Etc = 0,
        Event = 0,
    };

    private static MagicType3 MakeType3(int id, short firstDamage, byte duration = 0, short timeDamage = 0, byte radius = 0) => new()
    {
        ID = id,
        Radius = radius,
        Angle = 0,
        DirectType = 1,
        FirstDamage = firstDamage,
        EndDamage = 0,
        TimeDamage = timeDamage,
        Duration = duration,
        Attribute = 0, // NONE_R
    };

    private static MagicType4 MakeType4(int id, byte buffType, byte speed, short duration) => new()
    {
        ID = id,
        BuffType = buffType,
        Radius = 0,
        Duration = duration,
        AttackSpeed = 0,
        Speed = speed,
        Armor = 0,
        ArmorPercent = 100,
        AttackPower = 0,
        MagicPower = 0,
        MaxHp = 0,
        MaxHpPercent = 100,
        MaxMp = 0,
        MaxMpPercent = 100,
        HitRate = 100,
        AvoidRate = 100,
        Strength = 0,
        Stamina = 0,
        Dexterity = 0,
        Intelligence = 0,
        Charisma = 0,
        FireResist = 0,
        ColdResist = 0,
        LightningResist = 0,
        MagicResist = 0,
        DiseaseResist = 0,
        PoisonResist = 0,
        ExpPercent = 0,
    };

    private static MagicType7 MakeType7(int id, short damage, short duration, byte radius = 0) => new()
    {
        ID = id,
        ValidGroup = 0,
        NationChange = 0,
        MonsterNumber = 0,
        TargetChange = 0,
        StateChange = 0,
        Radius = radius,
        HitRate = 100,
        Duration = duration,
        Damage = damage,
        Vision = 0,
        NeedItem = 0,
    };

    private static AiWorld MakeWorld() => new() { Rand = (min, _) => min, Clock = () => 42.0 };

    private static AiZone MakeZone(AiWorld world, int zoneNumber = 21)
    {
        var map = (GameMap)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameMap));
        var zone = new AiZone
        {
            ServerNo = 1,
            ZoneNumber = zoneNumber,
            Map = map,
            Regions = new Region[1, 1] { { new Region() } },
        };
        world.Zones.Add(zone);
        return zone;
    }

    private static AiUser MakeUser(AiWorld world, int uid, byte nation, List<byte[]> outbox)
    {
        var user = new AiUser
        {
            Uid = uid,
            UserId = "tester",
            Nation = nation,
            Live = AiUser.UserLive,
            HP = 100,
            World = world,
            ZoneIndex = 0,
            SendToZone = p =>
            {
                outbox.Add(p);
                return ValueTask.CompletedTask;
            },
        };
        world.Users[uid] = user;
        return user;
    }

    private static Npc MakeNpc(AiWorld world, short nid, byte group, int hp = 100)
    {
        var npc = new Npc
        {
            Nid = nid,
            World = world,
            Group = group,
            State = NpcState.Standing,
            MaxHP = 500,
            HP = hp,
            ZoneIndex = 0,
        };
        world.Npcs[nid] = npc;
        return npc;
    }

    /// <summary>[command][tid][magicid][data1..6][totalDex][righthand] wie RecvMagicAttackReq.</summary>
    private static byte[] MakePayload(byte command, short tid, int magicId,
        short data1 = 0, short data3 = 0, short totalDex = 0, short righthand = 0)
    {
        var buf = new byte[23];
        buf[0] = command;
        BitConverter.GetBytes(tid).CopyTo(buf, 1);
        BitConverter.GetBytes(magicId).CopyTo(buf, 3);
        BitConverter.GetBytes(data1).CopyTo(buf, 7);
        BitConverter.GetBytes(data3).CopyTo(buf, 11);
        BitConverter.GetBytes(totalDex).CopyTo(buf, 19);
        BitConverter.GetBytes(righthand).CopyTo(buf, 21);
        return buf;
    }

    [Fact]
    public void Type3Heal_RestoresNpcHpAndBroadcasts()
    {
        var world = MakeWorld();
        MakeZone(world);
        var outbox = new List<byte[]>();
        AiUser user = MakeUser(world, 7, nation: 1, outbox);
        Npc target = MakeNpc(world, 5, group: 2, hp: 50);

        world.MagicTable[500] = MakeMagic(500, type1: 3, moral: 2);
        world.MagicType3Table[500] = MakeType3(500, firstDamage: 30);

        user.MagicProcess.MagicPacket(MakePayload(MagicEffecting, (short)(5 + NpcBand), 500));

        // FirstDamage >= 0 → applied directly (no GetMagicDamage scaling).
        Assert.Equal(80, target.HP);

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(22, sent.Length);
        Assert.Equal(AiOpcode.AG_MAGIC_ATTACK_RESULT, sent[0]);
        Assert.Equal(MagicEffecting, sent[1]);
        Assert.Equal(500, BitConverter.ToInt32(sent, 2));
        Assert.Equal(7, BitConverter.ToInt16(sent, 6));                  // sid = user id
        Assert.Equal((short)(5 + NpcBand), BitConverter.ToInt16(sent, 8));
        Assert.Equal(1, BitConverter.ToInt16(sent, 12));                 // result
        Assert.Equal(2, BitConverter.ToInt16(sent, 16));                 // moral echoed
    }

    [Fact]
    public void Type3Duration_FillsDotSlotWithNegativeAmount()
    {
        var world = MakeWorld();
        MakeZone(world);
        var outbox = new List<byte[]>();
        AiUser user = MakeUser(world, 7, nation: 1, outbox);
        Npc target = MakeNpc(world, 5, group: 2);
        target.InitMagicValuable(); // sets the MagicType3 slots to AttackUserId == -1

        world.MagicTable[510] = MakeMagic(510, type1: 3);
        world.MagicType3Table[510] = MakeType3(510, firstDamage: 0, duration: 10, timeDamage: -340);

        user.MagicProcess.MagicPacket(MakePayload(MagicEffecting, (short)(5 + NpcBand), 510));

        // GetMagicDamage(-340): (-340*20)/170 = -40 → |40| → (short)(0.7*40) = 28 → -28.
        // Slot amount: -28 / (10/2) = -5.
        Assert.Equal(7, target.MagicType3[0].AttackUserId);
        Assert.Equal(10, target.MagicType3[0].Duration);
        Assert.Equal(2, target.MagicType3[0].Interval);
        Assert.Equal(-5, target.MagicType3[0].HpAmount);
        Assert.Equal(42.0, target.MagicType3[0].StartTime);
    }

    [Fact]
    public void Type4SpeedBuff_ScalesNpcSpeedAndBroadcasts()
    {
        var world = MakeWorld();
        MakeZone(world);
        var outbox = new List<byte[]>();
        AiUser user = MakeUser(world, 7, nation: 1, outbox);
        Npc target = MakeNpc(world, 5, group: 2);
        target.OldSpeed1 = 2f;
        target.OldSpeed2 = 4f;

        world.MagicTable[600] = MakeMagic(600, type1: 4);
        world.MagicType4Table[600] = MakeType4(600, buffType: 6, speed: 150, duration: 20);

        user.MagicProcess.MagicPacket(MakePayload(MagicEffecting, (short)(5 + NpcBand), 600));

        Assert.Equal(3f, target.Speed1);
        Assert.Equal(6f, target.Speed2);
        Assert.Equal(150, target.MagicType4[5].Amount);
        Assert.Equal(20, target.MagicType4[5].DurationTime);
        Assert.Equal(42.0, target.MagicType4[5].StartTime);

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(MagicEffecting, sent[1]);
        Assert.Equal(1, BitConverter.ToInt16(sent, 12)); // result
    }

    [Fact]
    public void Type4UnknownBuff_SendsMagicFail()
    {
        var world = MakeWorld();
        MakeZone(world);
        var outbox = new List<byte[]>();
        AiUser user = MakeUser(world, 7, nation: 1, outbox);
        MakeNpc(world, 5, group: 2);

        world.MagicTable[610] = MakeMagic(610, type1: 4);
        world.MagicType4Table[610] = MakeType4(610, buffType: 3, speed: 0, duration: 0); // 3: no case

        user.MagicProcess.MagicPacket(MakePayload(MagicEffecting, (short)(5 + NpcBand), 610));

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(MagicFail, sent[1]);
    }

    [Fact]
    public void Type7Sleep_PutsNpcToSleep()
    {
        var world = MakeWorld();
        MakeZone(world);
        var outbox = new List<byte[]>();
        AiUser user = MakeUser(world, 7, nation: 1, outbox);
        Npc target = MakeNpc(world, 5, group: 2);

        world.MagicTable[700] = MakeMagic(700, type1: 7, moral: 7);
        world.MagicType7Table[700] = MakeType7(700, damage: 0, duration: 30);

        user.MagicProcess.MagicPacket(MakePayload(MagicEffecting, (short)(5 + NpcBand), 700));

        Assert.Equal(NpcState.Sleeping, target.State);
        Assert.Equal(30, target.Delay);

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(MagicEffecting, sent[1]);
        Assert.Equal(7, BitConverter.ToInt16(sent, 16)); // moral echoed
    }

    [Fact]
    public void AreaHeal_HitsEnemiesInRadiusAndSkipsFriends()
    {
        var world = MakeWorld();
        AiZone zone = MakeZone(world);
        var outbox = new List<byte[]>();
        AiUser user = MakeUser(world, 7, nation: 1, outbox);

        Npc enemy = MakeNpc(world, 5, group: 2, hp: 50);
        enemy.CurX = 10f;
        enemy.CurZ = 10f;
        Npc friend = MakeNpc(world, 6, group: 1, hp: 50);
        friend.CurX = 12f;
        friend.CurZ = 12f;
        Npc farEnemy = MakeNpc(world, 8, group: 2, hp: 50);
        farEnemy.CurX = 40f;
        farEnemy.CurZ = 40f;

        zone.Regions[0, 0].Npcs.Add(5 + NpcBand);
        zone.Regions[0, 0].Npcs.Add(6 + NpcBand);
        zone.Regions[0, 0].Npcs.Add(8 + NpcBand);

        world.MagicTable[800] = MakeMagic(800, type1: 3, moral: 10);
        world.MagicType3Table[800] = MakeType3(800, firstDamage: 200, radius: 15);

        // tid == -1 → area attack around (data1, data3) = (10, 10).
        user.MagicProcess.MagicPacket(MakePayload(MagicEffecting, -1, 800, data1: 10, data3: 10));

        // GetMagicDamage(200) = 16 (dex 0, rand → min); only the near enemy is hit.
        Assert.Equal(66, enemy.HP);
        Assert.Equal(50, friend.HP);
        Assert.Equal(50, farEnemy.HP);

        byte[] sent = Assert.Single(outbox);
        Assert.Equal((short)(5 + NpcBand), BitConverter.ToInt16(sent, 8)); // per-target packet
        Assert.Equal(10, BitConverter.ToInt16(sent, 16));                  // moral
    }

    [Fact]
    public void UnknownMagicId_ResetsStateSilently()
    {
        var world = MakeWorld();
        MakeZone(world);
        var outbox = new List<byte[]>();
        AiUser user = MakeUser(world, 7, nation: 1, outbox);

        user.MagicProcess.MagicState = 0x02; // CASTING
        user.MagicProcess.MagicPacket(MakePayload(MagicEffecting, 5, 999));

        Assert.Empty(outbox); // the C++ fail packet is commented out
        Assert.Equal(UserMagicProcessor.StateNone, user.MagicProcess.MagicState);
    }

    [Fact]
    public void GetWeatherDamage_BuffsMatchingAttribute()
    {
        var user = new AiUser();

        user.MagicProcess.GetWeatherType = () => 0x01; // WEATHER_FINE
        Assert.Equal(110, user.MagicProcess.GetWeatherDamage(100, 1)); // fire buffed
        Assert.Equal(100, user.MagicProcess.GetWeatherDamage(100, 2)); // ice unaffected

        user.MagicProcess.GetWeatherType = () => 0x03; // WEATHER_SNOW
        Assert.Equal(110, user.MagicProcess.GetWeatherDamage(100, 2));
    }
}
