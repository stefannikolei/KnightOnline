using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Pins for the general-attack packets (WIZ_ATTACK / WIZ_TARGET_HP).</summary>
public class CombatProtocolTests
{
    private sealed class CaptureClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public bool CryptionEnabled => true;

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public void EnableCryption(ulong publicKey) { }
    }

    [Fact]
    public void BuildAttack_MatchesMsgSendAttackLayout()
    {
        byte[] packet = CombatProtocol.BuildAttack(targetId: 350, interval: 1.0f, distance: 3.0f);
        var r = new PacketReader(packet);
        Assert.Equal((byte)GameOpcode.WIZ_ATTACK, r.GetByte());
        Assert.Equal(0x01, r.GetByte());              // type
        Assert.Equal(0x01, r.GetByte());              // success
        Assert.Equal((short)350, r.GetShort());       // target id
        Assert.Equal((short)110, r.GetShort());       // (1.0 + 0.1) * 100
        Assert.Equal((short)30, r.GetShort());        // 3.0 * 10
    }

    [Fact]
    public void SendAttack_GoesThroughInGameState()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        ctx.InGame.SendAttack(42, 1.2f, 2.5f);
        Assert.Equal((byte)GameOpcode.WIZ_ATTACK, client.Sent[^1][0]);
    }

    [Fact]
    public void ParseAttack_Broadcast_MarksTargetDeadOnDeathResult()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client)
        {
            Spawn = new SelectCharResult(1, 21, 6500, 5300, 120, 1),
        };
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();
        ctx.InGame.World.AddOrUpdateNpc(new NpcEntity { Id = 500 });

        AttackEvent? observed = null;
        ctx.InGame.AttackObserved = a => observed = a;

        // [WIZ_ATTACK][type 1][result 2 death][attacker 77][target 500]
        var buf = new byte[8];
        var w = new PacketWriter(buf);
        w.SetByte((byte)GameOpcode.WIZ_ATTACK);
        w.SetByte(1);
        w.SetByte(CombatProtocol.ResultDeath);
        w.SetShort(77);
        w.SetShort(500);
        ctx.Machine.DispatchPacket(w.Written.ToArray());

        Assert.NotNull(observed);
        Assert.Equal((short)77, observed!.Value.AttackerId);
        Assert.Equal((short)500, observed.Value.TargetId);
        Assert.True(ctx.InGame.World.TryGetNpc(500, out NpcEntity npc) && npc.IsDead);
    }

    [Fact]
    public void ParseTargetHp_ReadsHealthAndDamage()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        TargetHpUpdate? hp = null;
        ctx.InGame.TargetHpReceived = t => hp = t;

        var buf = new byte[16];
        var w = new PacketWriter(buf);
        w.SetByte((byte)GameOpcode.WIZ_TARGET_HP);
        w.SetShort(500);       // target id
        w.SetByte(1);          // echo
        w.SetDWord(3000);      // max hp
        w.SetDWord(2100);      // hp
        w.SetShort(900);       // damage
        ctx.Machine.DispatchPacket(w.Written.ToArray());

        Assert.NotNull(hp);
        Assert.Equal((short)500, hp!.Value.TargetId);
        Assert.Equal(3000, hp.Value.MaxHp);
        Assert.Equal(2100, hp.Value.Hp);
        Assert.Equal((short)900, hp.Value.Damage);
    }
}
