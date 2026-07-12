using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the Ebenezer combat slice (stage 4.6): WIZ_ATTACK, the PvP damage
/// path, exp/level changes, durability and the AISocket combat handlers.
/// </summary>
public class GameUserCombatTests
{
    private static Item MakeItem(int id, byte kind = 0, short damage = 0, short delay = 10,
        short range = 0, short durability = 5000, byte countable = 0) => new()
    {
        ID = id,
        Name = $"item{id}",
        Kind = kind,
        Slot = 0,
        Race = 0,
        ClassId = 0,
        Damage = damage,
        Delay = delay,
        Range = range,
        Weight = 10,
        Durability = durability,
        BuyPrice = 0,
        SellPrice = 0,
        Armor = 0,
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
        StrengthBonus = 0,
        StaminaBonus = 0,
        DexterityBonus = 0,
        IntelligenceBonus = 0,
        CharismaBonus = 0,
        MaxHpBonus = 0,
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
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 21, mapSize: 480f));
        world.Rand = (min, _) => min; // deterministic: always the low roll
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
        world.LevelUpTable[11] = 2000;
        world.ItemTable[810210000] = MakeItem(810210000, kind: 21, damage: 100); // sword
        world.ItemTable[900001000] = MakeItem(900001000, countable: 1);          // teleport scroll
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeFighter(
        EbenezerWorld world, FakeDbAgent db, string charId, byte nation, float x = 100, float z = 100,
        bool withSword = false)
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

        if (withSword)
        {
            data.Items[GameConstants.SlotRightHand].Num = 810210000;
            data.Items[GameConstants.SlotRightHand].Duration = 5000;
        }

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

    private static byte[] AttackPacket(short tid, short delay = 100, short distance = 0)
    {
        var packet = new byte[10];
        packet[0] = 0x08; // WIZ_ATTACK
        packet[1] = 0x01; // type
        packet[2] = 0x01; // result (client-claimed)
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(3), tid);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(5), delay);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(7), distance);
        return packet;
    }

    [Fact]
    public async Task Attack_Pvp_DealsDeterministicDamage()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser attacker, List<byte[]> attackerFrames) = MakeFighter(world, db, "Hero", nation: 1, withSword: true);
        (GameUser target, _) = MakeFighter(world, db, "Enemy", nation: 2, x: 110, z: 110);
        attackerFrames.Clear();

        await attacker.ParsingAsync(AttackPacket(target.SocketId));

        // Target TotalAc = 0.5*(10+0) = 5 → tempHitB = (408*100*200/100)/(5+240) = 333;
        // low rolls → GREAT_SUCCESS, damage = (short)(0.85*333)/3 = 283/3 = 94.
        Assert.Equal(6, target.UserData!.Hp);

        byte[] targetHp = Unframe(Assert.Single(attackerFrames, f => Unframe(f)[0] == 0x22)); // WIZ_TARGET_HP
        Assert.Equal(target.SocketId, BinaryPrimitives.ReadInt16LittleEndian(targetHp.AsSpan(1)));
        Assert.Equal(-94, BinaryPrimitives.ReadInt16LittleEndian(targetHp.AsSpan(12)));

        // The WIZ_ATTACK broadcast is buffered (bDirect false).
        byte[]? buffered = target.RegionPacketClear();
        Assert.NotNull(buffered);
        Assert.Contains((byte)0x08, buffered!.Skip(3));
    }

    [Fact]
    public async Task Attack_KillsTarget_TriggersDeathConsequences()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser attacker, _) = MakeFighter(world, db, "Hero", nation: 1, withSword: true);
        (GameUser target, List<byte[]> targetFrames) = MakeFighter(world, db, "Enemy", nation: 2, x: 110, z: 110);
        target.UserData!.Hp = 50;
        targetFrames.Clear();

        await attacker.ParsingAsync(AttackPacket(target.SocketId));

        Assert.Equal(0, target.UserData.Hp);
        Assert.Equal(3, target.ResHpType); // USER_DEAD
        Assert.Equal(attacker.SocketId, target.WhoKilledMe);

        byte[][] payloads = [.. targetFrames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x17); // WIZ_HP_CHANGE
        Assert.Contains(payloads, p => p[0] == 0x2A); // WIZ_LOYALTY_CHANGE
        // The extra direct dead packet: WIZ_ATTACK with result 0x02.
        Assert.Contains(payloads, p => p[0] == 0x08 && p[2] == 0x02);
    }

    [Fact]
    public async Task Attack_SameNation_ReportsMiss()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser attacker, _) = MakeFighter(world, db, "Hero", nation: 1, withSword: true);
        (GameUser friend, _) = MakeFighter(world, db, "Friend", nation: 1, x: 110, z: 110);

        await attacker.ParsingAsync(AttackPacket(friend.SocketId));

        Assert.Equal(100, friend.UserData!.Hp); // untouched
        byte[]? buffered = friend.RegionPacketClear();
        Assert.NotNull(buffered);
        Assert.Equal(0x00, buffered![3 + 2 + 2]); // result byte forced to 0
    }

    [Fact]
    public async Task Attack_TooFastSwing_IsIgnored()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser attacker, _) = MakeFighter(world, db, "Hero", nation: 1, withSword: true);
        (GameUser target, _) = MakeFighter(world, db, "Enemy", nation: 2, x: 110, z: 110);

        await attacker.ParsingAsync(AttackPacket(target.SocketId, delay: 5)); // < sword delay 10

        Assert.Equal(100, target.UserData!.Hp);
        Assert.Null(target.RegionPacketClear());
    }

    [Fact]
    public async Task Attack_LiveNpc_ForwardsToAiServer()
    {
        var world = MakeWorld();
        world.PointCheckFlag = true;
        world.Npcs[10005] = new GameNpc { Nid = 10005, HP = 500, NpcState = GameNpc.StateLive };
        var aiPackets = new List<(int Zone, byte[] Data)>();
        world.SendToAiServer = (zone, data) => aiPackets.Add((zone, data));

        var db = new FakeDbAgent();
        (GameUser attacker, _) = MakeFighter(world, db, "Hero", nation: 1, withSword: true);

        await attacker.ParsingAsync(AttackPacket(10005));

        (int aiZone, byte[] data) = Assert.Single(aiPackets);
        Assert.Equal(21, aiZone);
        Assert.Equal(AiOpcode.AG_ATTACK_REQ, data[0]);
        Assert.Equal(attacker.SocketId, BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(3)));
        Assert.Equal(10005, BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(5)));
        Assert.Equal(408, BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(7))); // TotalHit*100/100

        Assert.Null(attacker.RegionPacketClear()); // no client broadcast for the AI path
    }

    [Fact]
    public void ExpChange_LevelUp_GrantsPointsAndBroadcasts()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        user.UserData!.Exp = 900;
        frames.Clear();

        user.ExpChange(200); // 1100 >= MaxExp 1000

        Assert.Equal(11, user.UserData.Level);
        Assert.Equal(100, user.UserData.Exp);
        Assert.Equal(3, user.UserData.Points);
        Assert.Equal(2000, user.MaxExp);
        Assert.Equal(user.MaxHp, user.UserData.Hp); // full heal

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x1B); // WIZ_LEVEL_CHANGE (direct, own region)
    }

    [Fact]
    public void ExpChange_Penalty_SendsExpChangeAndTracksLostExp()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        user.UserData!.Exp = 500;
        frames.Clear();

        user.ExpChange(-50);

        Assert.Equal(450, user.UserData.Exp);
        Assert.Equal(50, user.LostExp);
        byte[] payload = Unframe(Assert.Single(frames));
        Assert.Equal(0x1A, payload[0]); // WIZ_EXP_CHANGE
        Assert.Equal(450u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(1)));
    }

    [Fact]
    public void ItemWoreOut_BreakingAnItem_HalvesItsStats()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1, withSword: true);
        user.UserData!.Items[GameConstants.SlotRightHand].Duration = 2;
        user.SetDetailData();
        frames.Clear();

        user.ItemWoreOut(GameUser.DurabilityTypeAttack, 1000); // wear rate 10 → breaks

        Assert.Equal(0, user.UserData.Items[GameConstants.SlotRightHand].Duration);
        Assert.Equal(50, user.ItemHit); // broken → half damage

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x38 && p[1] == GameConstants.SlotRightHand); // WIZ_DURATION
        Assert.Contains(payloads, p => p[0] == 0x1F); // WIZ_ITEM_MOVE stat refresh
    }

    [Fact]
    public void GiveItem_PutsItemIntoTheFirstFreeSlot()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        frames.Clear();

        Assert.True(user.GiveItem(900001000, 1));

        Assert.Equal(900001000, user.UserData!.Items[GameConstants.SlotMax].Num);
        Assert.Equal(1, user.UserData.Items[GameConstants.SlotMax].Count);

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x54); // WIZ_WEIGHT_CHANGE
        byte[] countChange = payloads.Single(p => p[0] == 0x3D); // WIZ_ITEM_COUNT_CHANGE
        Assert.Equal(900001000u, BinaryPrimitives.ReadUInt32LittleEndian(countChange.AsSpan(5)));
    }

    // ---- AISocket combat handlers ----

    private static (AiLink Link, List<byte[]> Sent) MakeLink(EbenezerWorld world)
    {
        var sent = new List<byte[]>();
        var link = new AiLink(0, world, NullLogger.Instance)
        {
            Transmit = p =>
            {
                sent.Add(p);
                return true;
            },
        };
        return (link, sent);
    }

    [Fact]
    public void RecvNpcAttack_UserKillsNpc_RemovesItAndRewardsScroll()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser attacker, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        var npc = new GameNpc
        {
            Nid = 10005, ZoneIndex = 0, CurZone = 21, RegionX = 2, RegionZ = 2,
            HP = 30, MaxHP = 500, NpcType = 2, // exit NPC → scroll reward
        };
        world.Npcs[10005] = npc;
        world.Zones[0].RegionNpcAdd(2, 2, 10005);
        world.PointCheckFlag = true;
        frames.Clear();
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_ATTACK_RESULT);
        writer.SetByte(0x01); // user -> npc
        writer.SetByte(0x02); // dead
        writer.SetShort(attacker.SocketId);
        writer.SetShort(10005);
        writer.SetShort(40);  // damage
        writer.SetDWord(0);
        writer.SetByte(1);    // direct attack
        link.Parsing(buffer.AsSpan(0, writer.Index));

        Assert.Equal(0, npc.HP);
        Assert.Equal(GameNpc.StateDead, npc.NpcState);
        Assert.Empty(world.Zones[0].Regions[2, 2].Npcs);
        Assert.Equal(0, npc.RegionX);

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x22); // WIZ_TARGET_HP to the killer
        Assert.Contains(payloads, p => p[0] == 0x3D); // scroll via WIZ_ITEM_COUNT_CHANGE
        Assert.Equal(900001000, attacker.UserData!.Items[GameConstants.SlotMax].Num);
    }

    [Fact]
    public void RecvNpcAttack_NpcKillsUser_AppliesExpPenalty()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser victim, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        victim.UserData!.Hp = 10;
        victim.UserData.Exp = 500;
        var npc = new GameNpc { Nid = 10005, ZoneIndex = 0, CurZone = 21, RegionX = 2, RegionZ = 2, NpcType = 0 };
        world.Npcs[10005] = npc;
        frames.Clear();
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_ATTACK_RESULT);
        writer.SetByte(0x02); // npc -> user
        writer.SetByte(0x02); // dead
        writer.SetShort(10005);
        writer.SetShort(victim.SocketId);
        writer.SetShort(50);
        writer.SetDWord(0);
        writer.SetByte(1);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        Assert.Equal(0, victim.UserData.Hp);
        Assert.Equal(3, victim.ResHpType); // USER_DEAD
        // Home zone (21, nation 1, not < 3): −MaxExp/20 = −50.
        Assert.Equal(450, victim.UserData.Exp);
        Assert.Equal(50, victim.LostExp);

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x08 && p[2] == 0x02); // direct dead packet
    }

    [Fact]
    public void RecvUserExp_GrantsExpAndLoyalty()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        user.UserData!.Exp = 100;
        frames.Clear();
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_EXP);
        writer.SetShort(user.SocketId);
        writer.SetShort(50);
        writer.SetShort(10);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        Assert.Equal(150, user.UserData.Exp);
        Assert.Equal(10, user.UserData.Loyalty);

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x1A); // WIZ_EXP_CHANGE
        Assert.Contains(payloads, p => p[0] == 0x2A); // WIZ_LOYALTY_CHANGE
    }

    [Fact]
    public void RecvSystemMsg_SendAll_BroadcastsChat()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (_, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        frames.Clear();
        (AiLink link, _) = MakeLink(world);

        byte[] message = "Guards down!"u8.ToArray();
        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_SYSTEM_MSG);
        writer.SetByte(8);     // WAR_SYSTEM_CHAT
        writer.SetShort(0x03); // SEND_ALL
        writer.SetShort(message.Length);
        writer.SetString(message);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        byte[] payload = Unframe(Assert.Single(frames));
        Assert.Equal(0x10, payload[0]); // WIZ_CHAT
        Assert.Equal(8, payload[1]);
        Assert.Equal(message.Length, BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(6)));
    }

    [Fact]
    public void RecvNpcGiveItem_DropsBundleAndNotifiesKiller()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser killer, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        frames.Clear();
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_NPC_GIVE_ITEM);
        writer.SetShort(killer.SocketId);
        writer.SetShort(10005);
        writer.SetShort(21);   // zone
        writer.SetShort(2);    // region x
        writer.SetShort(2);    // region z
        writer.SetFloat(110f);
        writer.SetFloat(112f);
        writer.SetFloat(0f);
        writer.SetByte(1);     // one stack
        writer.SetInt(810210000);
        writer.SetShort(1);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        ZoneItem bundle = Assert.Single(world.Zones[0].Regions[2, 2].Items).Value;
        Assert.Equal(810210000, bundle.ItemId[0]);
        Assert.Equal(2u, world.Zones[0].Bundle); // advanced from 1

        byte[] payload = Unframe(Assert.Single(frames));
        Assert.Equal(0x23, payload[0]); // WIZ_ITEM_DROP
        Assert.Equal(10005, BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(1)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(3)));
    }

    [Fact]
    public void RecvUserFail_DrainsHpAndBroadcastsTheKill()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeFighter(world, db, "Hero", nation: 1);
        frames.Clear();
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_FAIL);
        writer.SetShort(user.SocketId);
        writer.SetShort(10005);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        Assert.Equal(0, user.UserData!.Hp);

        byte[][] payloads = [.. frames.Select(Unframe)];
        // The user sees their own kill via the direct region broadcast.
        byte[] attack = payloads.Single(p => p[0] == 0x08);
        Assert.Equal(0x02, attack[2]);
        Assert.Equal(10005, BinaryPrimitives.ReadInt16LittleEndian(attack.AsSpan(3)));
        Assert.Equal(user.SocketId, BinaryPrimitives.ReadInt16LittleEndian(attack.AsSpan(5)));
    }
}
