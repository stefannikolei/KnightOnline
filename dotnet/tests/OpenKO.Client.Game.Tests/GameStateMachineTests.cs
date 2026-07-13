using OpenKO.Client.Game.States;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-7.1 pins: the CGameProcedure deferred-swap semantics.</summary>
public class GameStateMachineTests
{
    private sealed class RecordingState(string name, List<string> log) : GameState
    {
        public override string Name => name;

        public override void Init() => log.Add($"{name}.Init");

        public override void Release() => log.Add($"{name}.Release");

        public override void Tick() => log.Add($"{name}.Tick");

        public override void Render() => log.Add($"{name}.Render");
    }

    [Fact]
    public void SetActive_DefersInitToNextTick()
    {
        var log = new List<string>();
        var machine = new GameStateMachine();
        var login = new RecordingState("Login", log);

        machine.SetActive(login);
        // Before TickActive the swap has not settled → no render.
        Assert.False(machine.Settled);
        machine.RenderActive();
        Assert.Empty(log);

        // First TickActive runs Init then Tick; render now allowed.
        machine.TickActive();
        machine.RenderActive();
        Assert.Equal(["Login.Init", "Login.Tick", "Login.Render"], log);
    }

    [Fact]
    public void Transition_ReleasesOldAndInitsNewBeforeRender()
    {
        var log = new List<string>();
        var machine = new GameStateMachine();
        var login = new RecordingState("Login", log);
        var charSelect = new RecordingState("CharSelect", log);

        machine.SetActive(login);
        machine.TickActive();
        log.Clear();

        // A state requests the next; the swap happens on the following TickActive.
        machine.SetActive(charSelect);
        Assert.False(machine.Settled);   // pending
        machine.RenderActive();          // guarded out during the swap
        machine.TickActive();
        machine.RenderActive();

        Assert.Equal(
            ["Login.Release", "CharSelect.Init", "CharSelect.Tick", "CharSelect.Render"], log);
    }

    [Fact]
    public void DispatchPacket_GoesToActiveState()
    {
        var machine = new GameStateMachine();
        byte[]? seen = null;
        machine.SetActive(new DelegateState(p => { seen = p.ToArray(); return true; }));
        machine.TickActive();

        Assert.True(machine.DispatchPacket([0x2B, 1, 2]));
        Assert.Equal([0x2B, 1, 2], seen);
    }

    private sealed class DelegateState(Func<byte[], bool> handler) : GameState
    {
        public override string Name => "Delegate";

        public override bool ProcessPacket(ReadOnlySpan<byte> payload) => handler(payload.ToArray());
    }
}
