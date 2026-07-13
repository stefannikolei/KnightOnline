namespace OpenKO.Client.Game.States;

/// <summary>
/// Port of the CGameProcedure driver (WarFareMain's TickActive/RenderActive +
/// ProcActiveSet): a single "active state" pointer with a deferred
/// Release()→Init() swap. A transition set via <see cref="SetActive"/> only
/// takes effect on the next <see cref="TickActive"/>, so the outgoing state is
/// released and the incoming one initialised before its first render — exactly
/// the C++ ordering.
/// </summary>
public sealed class GameStateMachine
{
    private GameState? _active;
    private GameState? _prev;

    /// <summary>The settled active state (after its Init has run).</summary>
    public GameState? Active => _active;

    /// <summary>True once the pending swap has been applied (active == prev).</summary>
    public bool Settled => ReferenceEquals(_active, _prev);

    /// <summary>ProcActiveSet — records the target; the swap is deferred to TickActive.</summary>
    public void SetActive(GameState next)
    {
        next.Machine = this;
        _active = next;
    }

    /// <summary>
    /// TickActive: apply a pending swap (Release old, Init new), then tick the
    /// active state.
    /// </summary>
    public void TickActive()
    {
        if (!ReferenceEquals(_active, _prev))
        {
            _prev?.Release();
            _active?.Init();
            _prev = _active;
        }

        _active?.Tick();
    }

    /// <summary>RenderActive: only render once the swap has settled (C++ guard).</summary>
    public void RenderActive()
    {
        if (ReferenceEquals(_active, _prev))
            _active?.Render();
    }

    /// <summary>Drains one packet to the active state's ProcessPacket chain.</summary>
    public bool DispatchPacket(ReadOnlySpan<byte> payload) => _active?.ProcessPacket(payload) ?? false;
}
