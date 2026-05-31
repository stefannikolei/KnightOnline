using OpenKO.Common;
using OpenKO.Game;
using Xunit;

namespace OpenKO.Tests;

public class GameProcedureTests
{
    /// <summary>Records the lifecycle calls a procedure receives, in order, for assertion.</summary>
    private sealed class RecordingProcedure : GameProcedure
    {
        public List<string> Log { get; }
        private readonly string _name;

        public RecordingProcedure(string name, List<string> sharedLog)
        {
            _name = name;
            Log = sharedLog;
        }

        public GameContext BoundContext => Context;
        public int LastPacketOpcode { get; private set; } = -1;

        public override void Init() => Log.Add($"{_name}.Init");
        public override void Release() => Log.Add($"{_name}.Release");
        public override void Tick(float dt) => Log.Add($"{_name}.Tick");
        public override void Render() => Log.Add($"{_name}.Render");

        public override bool ProcessPacket(Packet packet)
        {
            LastPacketOpcode = packet.Opcode;
            Log.Add($"{_name}.Packet({packet.Opcode})");
            return true;
        }
    }

    [Fact]
    public void FirstProcedureIsInitializedOnNextTick()
    {
        var log = new List<string>();
        var ctx = new GameContext();
        var login = new RecordingProcedure("login", log);

        ctx.Procedures.SetActive(login);
        // Nothing happens until TickActive runs the deferred handover.
        Assert.Empty(log);

        ctx.Procedures.TickActive(0.016f);
        Assert.Equal(new[] { "login.Init", "login.Tick" }, log);
    }

    [Fact]
    public void RenderIsSuppressedUntilProcedureInitialized()
    {
        var log = new List<string>();
        var ctx = new GameContext();
        var login = new RecordingProcedure("login", log);

        ctx.Procedures.SetActive(login);

        // Before the first tick, active != previous, so Render must not fire.
        ctx.Procedures.RenderActive();
        Assert.Empty(log);

        ctx.Procedures.TickActive(0.016f);
        ctx.Procedures.RenderActive();
        Assert.Equal(new[] { "login.Init", "login.Tick", "login.Render" }, log);
    }

    [Fact]
    public void SwitchingReleasesOldAndInitializesNewOnNextTick()
    {
        var log = new List<string>();
        var ctx = new GameContext();
        var login = new RecordingProcedure("login", log);
        var select = new RecordingProcedure("select", log);

        ctx.Procedures.SetActive(login);
        ctx.Procedures.TickActive(0.016f);
        log.Clear();

        // Request the switch; handover is deferred to the next tick.
        ctx.Procedures.SetActive(select);
        Assert.Empty(log);

        ctx.Procedures.TickActive(0.016f);
        Assert.Equal(new[] { "login.Release", "select.Init", "select.Tick" }, log);
    }

    [Fact]
    public void SetActiveIgnoresNullAndCurrentProcedure()
    {
        var log = new List<string>();
        var ctx = new GameContext();
        var login = new RecordingProcedure("login", log);

        ctx.Procedures.SetActive(login);
        ctx.Procedures.TickActive(0.016f);
        log.Clear();

        ctx.Procedures.SetActive(null);   // ignored
        ctx.Procedures.SetActive(login);  // already active — ignored
        ctx.Procedures.TickActive(0.016f);

        // No Release/Init churn — just another tick of the same procedure.
        Assert.Equal(new[] { "login.Tick" }, log);
    }

    [Fact]
    public void ContextIsBoundBeforeInit()
    {
        var log = new List<string>();
        var ctx = new GameContext();
        var login = new RecordingProcedure("login", log);

        ctx.Procedures.SetActive(login);
        ctx.Procedures.TickActive(0.016f);

        Assert.Same(ctx, login.BoundContext);
    }

    [Fact]
    public void DispatchPacketGoesToActiveProcedure()
    {
        var log = new List<string>();
        var ctx = new GameContext();
        var login = new RecordingProcedure("login", log);

        ctx.Procedures.SetActive(login);
        ctx.Procedures.TickActive(0.016f);

        var pkt = new Packet((byte)0x42);
        Assert.True(ctx.Procedures.DispatchPacket(pkt));
        Assert.Equal(0x42, login.LastPacketOpcode);
    }

    [Fact]
    public void DispatchPacketWithNoActiveProcedureReturnsFalse()
    {
        var ctx = new GameContext();
        Assert.False(ctx.Procedures.DispatchPacket(new Packet((byte)1)));
    }
}
