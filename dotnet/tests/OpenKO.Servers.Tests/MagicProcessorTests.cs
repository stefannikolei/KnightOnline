using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the Ebenezer magic slice (stage 4.7): the WIZ_MAGIC_PROCESS flow,
/// type 3 damage/DoT, type 4 buffs with expiry, cancels and the NPC-cast path.
/// </summary>
public class MagicProcessorTests
{
    private const int AttackSpellId = 112801; // type 3 direct damage
    private const int DotSpellId = 112802;    // type 3 duration curse
    private const int BuffSpellId = 500001;   // type 4 AC buff

    private static Magic MakeMagic(int id, byte type1, byte moral, short manaCost = 30) => new()
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
        ManaCost = manaCost,
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

    private static MagicType3 MakeType3(int id, short firstDamage, short timeDamage, byte duration) => new()
    {
        ID = id,
        Radius = 0,
        Angle = 0,
        DirectType = 1,
        FirstDamage = firstDamage,
        EndDamage = 0,
        TimeDamage = timeDamage,
        Duration = duration,
        Attribute = 1, // fire
    };

    private static MagicType4 MakeType4(int id, byte buffType, short armor, short duration) => new()
    {
        ID = id,
        BuffType = buffType,
        Radius = 0,
        Duration = duration,
        AttackSpeed = 100,
        Speed = 100,
        Armor = armor,
        ArmorPercent = 100,
        AttackPower = 100,
        MagicPower = 100,
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

    private static double _now;

    private static EbenezerWorld MakeWorld()
    {
        _now = 1000.0;
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 21, mapSize: 480f));
        world.Rand = Math.Min;   // deterministic low rolls (myrand swaps reversed ranges)
        world.Clock = () => _now;
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

        world.MagicTable[AttackSpellId] = MakeMagic(AttackSpellId, type1: 3, moral: 7); // MORAL_ENEMY
        world.MagicType3Table[AttackSpellId] = MakeType3(AttackSpellId, firstDamage: -100, timeDamage: 0, duration: 0);

        world.MagicTable[DotSpellId] = MakeMagic(DotSpellId, type1: 3, moral: 7);
        world.MagicType3Table[DotSpellId] = MakeType3(DotSpellId, firstDamage: 0, timeDamage: -400, duration: 10);

        world.MagicTable[BuffSpellId] = MakeMagic(BuffSpellId, type1: 4, moral: 1, manaCost: 20); // MORAL_SELF
        world.MagicType4Table[BuffSpellId] = MakeType4(BuffSpellId, buffType: 2, armor: 50, duration: 60);

        world.MagicTable[SummonSpellId] = MakeMagic(SummonSpellId, type1: 8, moral: 2, manaCost: 20); // MORAL_FRIEND_WITHME
        world.MagicType8Table[SummonSpellId] = new MagicType8
        {
            ID = SummonSpellId, Target = 0, Radius = 0, WarpType = 12, ExpRecover = 0,
        };

        world.MagicType8Table[ResurrectType8Id] = new MagicType8
        {
            ID = ResurrectType8Id, Target = 0, Radius = 0, WarpType = 11, ExpRecover = 300,
        };

        world.MagicTable[CureSpellId] = MakeMagic(CureSpellId, type1: 5, moral: 2, manaCost: 10);
        world.MagicType5Table[CureSpellId] = new MagicType5
        {
            ID = CureSpellId, Type = 3, ExpRecover = 50, NeedStone = 0, // resurrection
        };
        world.MagicType5Table[490041] = new MagicType5
        {
            ID = 490041, Type = 3, ExpRecover = 0, NeedStone = 3, // stone of resurrection
        };

        world.HomeTable[1] = MakeHome(1);
        world.HomeTable[2] = MakeHome(2);

        return world;
    }

    private const int SummonSpellId = 800001;    // type 8 warp type 12
    private const int ResurrectType8Id = 800002; // type 8 warp type 11
    private const int CureSpellId = 903001;      // type 5 resurrection

    private static Home MakeHome(byte nation) => new()
    {
        Nation = nation,
        ElmoZoneX = 100, ElmoZoneZ = 100, ElmoZoneLX = 10, ElmoZoneLZ = 10,
        KarusZoneX = 200, KarusZoneZ = 200, KarusZoneLX = 10, KarusZoneLZ = 10,
        FreeZoneX = 300, FreeZoneZ = 300, FreeZoneLX = 10, FreeZoneLZ = 10,
        BattleZoneX = 400, BattleZoneZ = 400, BattleZoneLX = 10, BattleZoneLZ = 10,
        BattleZone2X = 0, BattleZone2Z = 0, BattleZone2LX = 0, BattleZone2LZ = 0,
    };

    private static (GameUser User, List<byte[]> Frames) MakeFighter(
        EbenezerWorld world, FakeDbAgent db, string charId, byte nation, float x = 100, float z = 100)
    {
        var frames = new List<byte[]>();
        short id = world.Register(i => new GameUser(i, world, db, NullLogger.Instance));
        GameUser user = world.Users[id]!;
        user.Transmit = frame =>
        {
            frames.Add(frame);
            return true;
        };

        UserData data = db.Users.Get(id)!;
        data.AccountId = $"acct{id}";
        data.CharId = charId;
        data.Zone = 21;
        data.Nation = nation;
        data.Class = 105;
        data.Level = 10;
        data.Str = 70;
        data.Sta = 60;
        data.Dex = 50;
        data.Intel = 50;
        data.Cha = 50;
        data.Hp = 100;
        data.Mp = 100;
        data.CurX = x;
        data.CurZ = z;
        user.UserData = data;
        user.SetDetailData();
        user.State = ConnectionState.GameStart;
        world.Zones[0].RegionUserAdd(user.RegionX, user.RegionZ, user.SocketId);
        return (user, frames);
    }

    private static byte[] Unframe(byte[] frame)
    {
        int len = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        return frame.AsSpan(4, len).ToArray();
    }

    private static byte[] MagicPacketBody(byte command, int magicId, short sid, short tid)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(command);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        for (int i = 0; i < 6; i++)
            writer.SetShort(0);

        return buffer[..writer.Index];
    }

    [Fact]
    public void Casting_EchoesBufferedToTheRegion()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, _) = MakeFighter(world, db, "Mage", nation: 1);
        (GameUser enemy, _) = MakeFighter(world, db, "Enemy", nation: 2, x: 110, z: 110);

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicCasting, AttackSpellId, caster.SocketId, enemy.SocketId));

        byte[]? buffered = enemy.RegionPacketClear();
        Assert.NotNull(buffered);
        Assert.Equal(0x31, buffered![5]); // WIZ_MAGIC_PROCESS
        Assert.Equal(MagicProcessor.MagicCasting, buffered[6]);
    }

    [Fact]
    public void Effecting_DirectDamage_HurtsTargetAndCostsMana()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, List<byte[]> casterFrames) = MakeFighter(world, db, "Mage", nation: 1);
        (GameUser enemy, _) = MakeFighter(world, db, "Enemy", nation: 2, x: 110, z: 110);
        casterFrames.Clear();

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, AttackSpellId, caster.SocketId, enemy.SocketId));

        // totalHit = -100*50/170 = -29 → damage = (short)(0.7*-29 + 0.2*-29) = -26 → /3 = -8.
        Assert.Equal(92, enemy.UserData!.Hp);
        Assert.Equal(70, caster.UserData!.Mp); // mana cost 30

        byte[][] payloads = [.. casterFrames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x22); // WIZ_TARGET_HP
    }

    [Fact]
    public void Effecting_WithoutMana_FailsWithoutDamage()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, List<byte[]> casterFrames) = MakeFighter(world, db, "Mage", nation: 1);
        (GameUser enemy, _) = MakeFighter(world, db, "Enemy", nation: 2, x: 110, z: 110);
        caster.UserData!.Mp = 10;
        casterFrames.Clear();

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, AttackSpellId, caster.SocketId, enemy.SocketId));

        Assert.Equal(100, enemy.UserData!.Hp);
        byte[] fail = Unframe(Assert.Single(casterFrames));
        Assert.Equal(0x31, fail[0]);
        Assert.Equal(MagicProcessor.MagicFail, fail[1]);
    }

    [Fact]
    public void Effecting_SameNation_FailsTheMoralCheck()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, List<byte[]> casterFrames) = MakeFighter(world, db, "Mage", nation: 1);
        (GameUser friend, _) = MakeFighter(world, db, "Friend", nation: 1, x: 110, z: 110);
        casterFrames.Clear();

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, AttackSpellId, caster.SocketId, friend.SocketId));

        Assert.Equal(100, friend.UserData!.Hp);
        byte[] fail = Unframe(Assert.Single(casterFrames));
        Assert.Equal(MagicProcessor.MagicFail, fail[1]);
    }

    [Fact]
    public void Type4Buff_AppliesExpiresViaThePacketTail()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, List<byte[]> frames) = MakeFighter(world, db, "Mage", nation: 1);
        short oldAc = caster.TotalAc;
        frames.Clear();

        // Self buff: +50 armor for 60 seconds.
        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, BuffSpellId, caster.SocketId, caster.SocketId));

        Assert.Equal(50, caster.AcAmount);
        Assert.Equal(2, caster.Type4Buff[1]); // friendly buff
        Assert.True(caster.Type4Flag);
        Assert.Equal(60, caster.DurationType4[2]);
        Assert.Equal(80, caster.UserData!.Mp); // per-target mana cost 20

        // Let it expire: the tail runs after any received packet.
        _now += 61;
        frames.Clear();
        caster.Type4DurationTick(world.Clock());

        Assert.Equal(0, caster.AcAmount);
        Assert.Equal(0, caster.Type4Buff[1]);
        Assert.False(caster.Type4Flag);
        Assert.Equal(oldAc, caster.TotalAc);

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x31 && p[1] == MagicProcessor.MagicType4End && p[2] == 2);
    }

    [Fact]
    public void Type3Dot_TicksAndExpires()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, _) = MakeFighter(world, db, "Mage", nation: 1);
        (GameUser enemy, List<byte[]> enemyFrames) = MakeFighter(world, db, "Enemy", nation: 2, x: 110, z: 110);

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, DotSpellId, caster.SocketId, enemy.SocketId));

        // totalHit = -400*50/170 = -117 → damage -105 → /3 = -35 → per tick -35/(10/2) = -7.
        Assert.True(enemy.Type3Flag);
        Assert.Equal(-7, enemy.HpAmount[0]);
        Assert.Equal(2, enemy.HpInterval[0]);
        Assert.Equal(caster.SocketId, enemy.SourceId[0]);
        Assert.Equal(100, enemy.UserData!.Hp); // FirstDamage 0: no initial hit

        // First tick after the interval.
        _now += 3;
        enemy.HpTimeChangeType3(world.Clock());
        Assert.Equal(93, enemy.UserData.Hp);

        // Expiry: past the 10s duration the slot resets and notifies.
        _now += 11;
        enemyFrames.Clear();
        enemy.HpTimeChangeType3(world.Clock());

        Assert.False(enemy.Type3Flag);
        Assert.Equal(0, enemy.HpAmount[0]);
        Assert.Equal(5, enemy.HpInterval[0]);
        byte[][] payloads = [.. enemyFrames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x31 && p[1] == MagicProcessor.MagicType3End && p[2] == 200);
    }

    [Fact]
    public void CancelCommand_RemovesTheBuff()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, List<byte[]> frames) = MakeFighter(world, db, "Mage", nation: 1);

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, BuffSpellId, caster.SocketId, caster.SocketId));
        Assert.Equal(50, caster.AcAmount);
        frames.Clear();

        // MAGIC_CANCEL targets the buffed user via sid.
        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicCancel, BuffSpellId, caster.SocketId, -1));

        Assert.Equal(0, caster.AcAmount);
        Assert.Equal(0, caster.Type4Buff[1]);
        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x31 && p[1] == MagicProcessor.MagicType4End && p[2] == 2);
    }

    [Fact]
    public void NpcCastMagic_RunsThroughTheAiLinkProcessor()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser victim, _) = MakeFighter(world, db, "Hero", nation: 1);
        world.Npcs[10005] = new GameNpc
        {
            Nid = 10005, ZoneIndex = 0, CurZone = 21, RegionX = 2, RegionZ = 2,
            Group = 2, HitRate = 100, NpcState = GameNpc.StateLive,
        };

        var link = new AiLink(0, world, NullLogger.Instance);

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicProcessor.MagicEffecting);
        writer.SetDWord(AttackSpellId);
        writer.SetShort(10005);
        writer.SetShort(victim.SocketId);
        for (int i = 0; i < 6; i++)
            writer.SetShort(0);

        link.Parsing(buffer.AsSpan(0, writer.Index));

        // The NPC hit lands via GetMagicDamage (low rolls → GREAT_SUCCESS).
        Assert.True(victim.UserData!.Hp < 100);
    }

    [Fact]
    public async Task Regene_RespawnAtBindPoint_SetsBlinking()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        user.UserData!.Bind = 5;
        world.Zones[0].ObjectEvents[5] = new ObjectEvent { Type = 0, Life = 1, PosX = 200f, PosZ = 300f };
        user.ResHpType = 3; // USER_DEAD
        user.LostExp = 100;
        user.WhoKilledMe = 7;
        frames.Clear();

        await user.ParsingAsync([0x12, 0x01]); // WIZ_REGENE, normal respawn

        // Deterministic min rolls: offset 0/100 < 2.5 → +1.5.
        Assert.Equal(201.5f, user.UserData.CurX);
        Assert.Equal(301.5f, user.UserData.CurZ);
        Assert.Equal(3, user.AbnormalType); // ABNORMAL_BLINKING
        Assert.Equal(1, user.ResHpType);    // USER_STANDING
        Assert.Equal(0, user.LostExp);
        Assert.Equal(-1, user.WhoKilledMe);
        Assert.Equal(4, user.RegionX); // 201.5 / 48

        byte[] regene = frames.Select(Unframe).First(p => p[0] == 0x12);
        Assert.Equal(2010, BinaryPrimitives.ReadUInt16LittleEndian(regene.AsSpan(1))); // (ushort)201.5 * 10
    }

    [Fact]
    public async Task Blink_EndsAfterTenSecondsAndNotifiesTheAi()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        var aiPackets = new List<(int Zone, byte[] Data)>();
        world.SendToAiServer = (zone, data) => aiPackets.Add((zone, data));
        (GameUser user, _) = MakeFighter(world, db, "Hero", nation: 1);
        user.UserData!.Bind = 5;
        world.Zones[0].ObjectEvents[5] = new ObjectEvent { Life = 1, PosX = 200f, PosZ = 300f };
        user.ResHpType = 3;

        await user.ParsingAsync([0x12, 0x01]);
        Assert.Equal(3, user.AbnormalType);
        aiPackets.Clear();

        _now += 11;
        await user.ParsingAsync([0xFF]); // any packet runs the timer tail

        Assert.Equal(1, user.AbnormalType); // ABNORMAL_NORMAL
        Assert.Equal(user.MaxHp, user.UserData.Hp); // normal regene: full refill
        Assert.Contains(aiPackets, p => p.Data[0] == AiOpcode.AG_USER_REGENE);
        Assert.Contains(aiPackets, p => p.Data[0] == AiOpcode.AG_USER_INOUT && p.Data[1] == 0x03); // USER_REGENE
    }

    [Fact]
    public async Task WarpProcess_JumpsWithinTheZone()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        frames.Clear();

        var packet = new byte[5];
        packet[0] = 0x1E; // WIZ_WARP
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1), 1500);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(3), 1400);
        await user.ParsingAsync(packet);

        Assert.Equal(150f, user.UserData!.CurX);
        Assert.Equal(140f, user.UserData.CurZ);
        Assert.Equal(3, user.RegionX); // 150 / 48
        Assert.Contains(user.SocketId, world.Zones[0].Regions[3, 2].Users);

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x1E
            && BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(1)) == 1500);
    }

    [Fact]
    public void Type8_SummonPullsTheTargetToTheCaster()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, _) = MakeFighter(world, db, "Mage", nation: 1);
        (GameUser friend, List<byte[]> friendFrames) = MakeFighter(world, db, "Friend", nation: 1, x: 300, z: 300);
        friendFrames.Clear();

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, SummonSpellId, caster.SocketId, friend.SocketId));

        Assert.Equal(100f, friend.UserData!.CurX); // pulled to the caster
        Assert.Equal(100f, friend.UserData.CurZ);
        Assert.Equal(80, caster.UserData!.Mp); // mana cost 20

        byte[][] payloads = [.. friendFrames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x1E); // WIZ_WARP
    }

    [Fact]
    public void Type8_ResurrectionRevivesADeadTarget()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        var aiPackets = new List<(int Zone, byte[] Data)>();
        world.SendToAiServer = (zone, data) => aiPackets.Add((zone, data));
        (GameUser caster, _) = MakeFighter(world, db, "Cleric", nation: 1);
        (GameUser dead, _) = MakeFighter(world, db, "Fallen", nation: 1, x: 110, z: 110);
        dead.UserData!.Hp = 0;
        dead.ResHpType = 3;
        dead.UserData.Exp = 100;

        caster.Magic.ExecuteType8(ResurrectType8Id, caster.SocketId, dead.SocketId, 0, 0, 0);

        Assert.Equal(1, dead.ResHpType); // USER_STANDING
        Assert.Equal(dead.MaxHp, dead.UserData.Hp);
        Assert.Equal(103, dead.UserData.Exp); // + ExpRecover 300/100
        Assert.Contains(aiPackets, p => p.Data[0] == AiOpcode.AG_USER_REGENE);
    }

    [Fact]
    public void Type5_Resurrection_RunsRegene()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser caster, _) = MakeFighter(world, db, "Cleric", nation: 1);
        (GameUser dead, List<byte[]> deadFrames) = MakeFighter(world, db, "Fallen", nation: 1, x: 110, z: 110);
        dead.UserData!.Bind = 5;
        world.Zones[0].ObjectEvents[5] = new ObjectEvent { Life = 1, PosX = 200f, PosZ = 300f };
        dead.UserData.Hp = 0;
        dead.ResHpType = 3;
        dead.LostExp = 100;
        dead.UserData.Exp = 500;
        deadFrames.Clear();

        caster.Magic.MagicPacket(MagicPacketBody(MagicProcessor.MagicEffecting, CureSpellId, caster.SocketId, dead.SocketId));

        Assert.Equal(3, dead.AbnormalType);        // blinking
        Assert.Equal(1, dead.ResHpType);           // standing
        Assert.Equal(1, dead.RegeneType);          // REGENE_MAGIC
        Assert.Equal(0, dead.UserData.Mp);         // MP emptied
        Assert.Equal(550, dead.UserData.Exp);      // + LostExp 100 * ExpRecover 50 / 100

        byte[][] payloads = [.. deadFrames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x12); // WIZ_REGENE
    }

    [Fact]
    public void HpTimeChange_StandingRegeneratesHpAndMp()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeFighter(world, db, "Hero", nation: 1);
        user.UserData!.Hp = 50;
        user.UserData.Mp = 50;

        user.HpTimeChange(world.Clock());

        // (int)((10*(1+10/60.0)+1)*0.2)+3 = 5.
        Assert.Equal(55, user.UserData.Hp);
        Assert.Equal(55, user.UserData.Mp);
    }
}
