using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Servers.AIServer.Ai;
using Xunit;
using Npc = OpenKO.Servers.AIServer.Ai.Npc;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the CNpcMagicProcess port (NpcMagicProcessor): heal casts, moral
/// failures, the casting echo quirk and GetMagicDamage.
/// </summary>
public class NpcMagicProcessorTests
{
    private const int NpcBand = 10000;

    private const byte MagicCasting = 1;
    private const byte MagicEffecting = 3;
    private const byte MagicFail = 4;

    private static Magic MakeMagic(int id, byte type1, byte moral) => new()
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

    private static MagicType3 MakeType3(int id, short firstDamage) => new()
    {
        ID = id,
        Radius = 0,
        Angle = 0,
        DirectType = 1,
        FirstDamage = firstDamage,
        EndDamage = 0,
        TimeDamage = 0,
        Duration = 0,
        Attribute = 0, // NONE_R
    };

    private static Npc MakeNpc(AiWorld world, short nid, byte group, List<byte[]>? outbox = null)
    {
        var npc = new Npc
        {
            Nid = nid,
            World = world,
            Group = group,
            State = NpcState.Standing,
            MaxHP = 100,
            HP = 100,
        };
        if (outbox is not null)
        {
            npc.SendToZone = p =>
            {
                outbox.Add(p);
                return ValueTask.CompletedTask;
            };
        }

        world.Npcs[nid] = npc;
        return npc;
    }

    /// <summary>[command][magicid][sid][tid][data1..data6] wie die NPC-KI es baut.</summary>
    private static byte[] MakePayload(byte command, int magicId, short sid, short tid)
    {
        var buf = new byte[21];
        buf[0] = command;
        BitConverter.GetBytes(magicId).CopyTo(buf, 1);
        BitConverter.GetBytes(sid).CopyTo(buf, 5);
        BitConverter.GetBytes(tid).CopyTo(buf, 7);
        return buf;
    }

    [Fact]
    public void HealCast_Type3_HealsTargetAndBroadcastsResult()
    {
        var world = new AiWorld { Rand = (min, _) => min };
        world.MagicTable[500] = MakeMagic(500, type1: 3, moral: 2); // MORAL_FRIEND_WITHME
        world.MagicType3Table[500] = MakeType3(500, firstDamage: 200);

        var outbox = new List<byte[]>();
        Npc healer = MakeNpc(world, nid: 1, group: 1, outbox);
        Npc target = MakeNpc(world, nid: 5, group: 1, new List<byte[]>());
        target.HP = 50;

        healer.MagicProcess.MagicPacket(
            MakePayload(MagicEffecting, 500, (short)(1 + NpcBand), (short)(5 + NpcBand)));

        // GetMagicDamage: 200*20/170 = 23 → damage = (short)(0.7f*23) = 16.
        Assert.Equal(66, target.HP);

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(22, sent.Length);
        Assert.Equal(AiOpcode.AG_MAGIC_ATTACK_RESULT, sent[0]);
        Assert.Equal(MagicEffecting, sent[1]);
        Assert.Equal(500, BitConverter.ToInt32(sent, 2));
        Assert.Equal((short)(1 + NpcBand), BitConverter.ToInt16(sent, 6));
        Assert.Equal((short)(5 + NpcBand), BitConverter.ToInt16(sent, 8));
        Assert.Equal(1, BitConverter.ToInt16(sent, 12));  // result = SetHMagicDamage ok
        Assert.Equal(2, BitConverter.ToInt16(sent, 16));  // moral echoed
    }

    [Fact]
    public void DeadTarget_Type3_BroadcastsResultZero()
    {
        var world = new AiWorld();
        world.MagicTable[500] = MakeMagic(500, type1: 3, moral: 2);
        world.MagicType3Table[500] = MakeType3(500, firstDamage: 200);

        var outbox = new List<byte[]>();
        Npc healer = MakeNpc(world, nid: 1, group: 1, outbox);
        Npc target = MakeNpc(world, nid: 5, group: 1);
        target.HP = 0; // dead-ish: HP==0 → result 0, but moral check (State) still passes

        healer.MagicProcess.MagicPacket(
            MakePayload(MagicEffecting, 500, (short)(1 + NpcBand), (short)(5 + NpcBand)));

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(MagicEffecting, sent[1]);
        Assert.Equal(0, BitConverter.ToInt16(sent, 12)); // result = 0
    }

    [Fact]
    public void UnknownMagicId_Casting_SendsFailPacketWithMinus100()
    {
        var world = new AiWorld();
        var outbox = new List<byte[]>();
        Npc caster = MakeNpc(world, nid: 1, group: 1, outbox);
        caster.MagicProcess.MagicState = NpcMagicProcessor.StateCasting;

        caster.MagicProcess.MagicPacket(
            MakePayload(MagicCasting, 999, (short)(1 + NpcBand), 42));

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(22, sent.Length);
        Assert.Equal(AiOpcode.AG_MAGIC_ATTACK_RESULT, sent[0]);
        Assert.Equal(MagicFail, sent[1]);
        Assert.Equal(999, BitConverter.ToInt32(sent, 2));
        Assert.Equal((short)(1 + NpcBand), BitConverter.ToInt16(sent, 6));
        Assert.Equal(42, BitConverter.ToInt16(sent, 8));
        Assert.Equal(-100, BitConverter.ToInt16(sent, 10)); // MAGIC_CASTING marker
        Assert.Equal(NpcMagicProcessor.StateNone, caster.MagicProcess.MagicState);
    }

    [Fact]
    public void MoralEnemy_SameGroupTarget_Fails()
    {
        var world = new AiWorld();
        world.MagicTable[600] = MakeMagic(600, type1: 3, moral: 7); // MORAL_ENEMY

        var outbox = new List<byte[]>();
        Npc caster = MakeNpc(world, nid: 1, group: 1, outbox);
        MakeNpc(world, nid: 5, group: 1); // same group → enemy check fails

        caster.MagicProcess.MagicPacket(
            MakePayload(MagicEffecting, 600, (short)(1 + NpcBand), (short)(5 + NpcBand)));

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(MagicFail, sent[1]);
        Assert.Equal(0, BitConverter.ToInt16(sent, 10)); // not casting → 0, not -100
    }

    [Fact]
    public void Casting_EchoesPayloadDroppingLastByte()
    {
        var world = new AiWorld();
        world.MagicTable[700] = MakeMagic(700, type1: 3, moral: 7); // MORAL_ENEMY

        var outbox = new List<byte[]>();
        Npc caster = MakeNpc(world, nid: 1, group: 1, outbox);
        var user = new AiUser { Uid = 3, Nation = 2, Live = AiUser.UserLive };
        world.Users[3] = user;

        byte[] payload = MakePayload(MagicCasting, 700, (short)(1 + NpcBand), 3);
        payload[^1] = 0x77; // marker that must be dropped by the len-1 copy quirk
        caster.MagicProcess.MagicPacket(payload);

        byte[] sent = Assert.Single(outbox);
        Assert.Equal(payload.Length, sent.Length);
        Assert.Equal(AiOpcode.AG_MAGIC_ATTACK_RESULT, sent[0]);
        Assert.Equal(payload[..^1], sent[1..]);
    }

    [Fact]
    public void MagicFailCommand_OnlyResetsState()
    {
        var world = new AiWorld();
        var outbox = new List<byte[]>();
        Npc caster = MakeNpc(world, nid: 1, group: 1, outbox);
        caster.MagicProcess.MagicState = NpcMagicProcessor.StateCasting;

        caster.MagicProcess.MagicPacket([MagicFail]);

        Assert.Empty(outbox); // the C++ send is commented out
        Assert.Equal(NpcMagicProcessor.StateNone, caster.MagicProcess.MagicState);
    }

    [Fact]
    public void GetMagicDamage_GateLikeNpcAndBandChecks()
    {
        var world = new AiWorld();
        Npc caster = MakeNpc(world, nid: 1, group: 1);
        Npc gate = MakeNpc(world, nid: 6, group: 1);
        gate.NpcType = 51; // NPC_PHOENIX_GATE

        Assert.Equal(0, caster.MagicProcess.GetMagicDamage(6 + NpcBand, 200, 0, 0));
        Assert.Equal(0, caster.MagicProcess.GetMagicDamage(3, 200, 0, 0));      // below NPC_BAND
        Assert.Equal(0, caster.MagicProcess.GetMagicDamage(20001, 200, 0, 0));  // above INVALID_BAND
    }

    [Fact]
    public void GetMagicDamage_AppliesResistance()
    {
        var world = new AiWorld { Rand = (min, _) => min };
        Npc caster = MakeNpc(world, nid: 1, group: 1);
        Npc target = MakeNpc(world, nid: 5, group: 1);
        target.FireResist = 100;

        // totalHit = 200*20/170 = 23; damage = (short)(0.7f*(23 - 0.9f*23*100/200)) = (short)8.05 = 8.
        Assert.Equal(8, caster.MagicProcess.GetMagicDamage(5 + NpcBand, 200, 1, 0)); // FIRE_R
    }
}
