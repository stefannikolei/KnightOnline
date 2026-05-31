using OpenKO.Common;

namespace OpenKO.Game;

/// <summary>
/// Cross-platform port of the C++ <c>CGameProcedure</c> (Client/WarFare/GameProcedure.cpp) — the
/// base of the client's game-state machine (login, nation-select, character-select, in-game, …).
///
/// The original is one monolithic class crammed with Windows-specific state (HCURSOR, HWND,
/// registry access, DirectX globals). This port keeps only the portable essence — the procedure
/// <b>lifecycle</b> (<see cref="Init"/>/<see cref="Release"/>/<see cref="Tick"/>/<see cref="Render"/>)
/// and packet dispatch (<see cref="ProcessPacket"/>) — and pushes the shared services
/// (sockets, session state) into <see cref="GameContext"/>. Concrete states (e.g. the login screen)
/// derive from this. Switching is driven by <see cref="GameProcedureManager"/>, which reproduces the
/// original's deferred Release-then-Init handover.
/// </summary>
public abstract class GameProcedure
{
    /// <summary>Shared services and session state, injected by <see cref="GameProcedureManager"/> before <see cref="Init"/>.</summary>
    protected GameContext Context { get; private set; } = null!;

    internal void Bind(GameContext context) => Context = context;

    /// <summary>Allocate resources / load UI when this procedure becomes active (port of <c>Init</c>).</summary>
    public virtual void Init() { }

    /// <summary>Free resources when leaving this procedure (port of <c>Release</c>).</summary>
    public virtual void Release() { }

    /// <summary>Per-frame logic update (port of <c>Tick</c>). <paramref name="deltaSeconds"/> replaces the global frame timer.</summary>
    public virtual void Tick(float deltaSeconds) { }

    /// <summary>Per-frame draw (port of <c>Render</c>).</summary>
    public virtual void Render() { }

    /// <summary>
    /// Handle one decoded packet (port of <c>ProcessPacket</c>). Return <c>true</c> if the packet was
    /// consumed, <c>false</c> to let it fall through. The default ignores everything.
    /// </summary>
    public virtual bool ProcessPacket(Packet packet) => false;
}
