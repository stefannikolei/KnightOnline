namespace OpenKO.Client.Game.States;

/// <summary>
/// Port of <c>CGameProcedure</c>: one game state (login, nation select, char
/// select, in-game…). The driver calls <see cref="Init"/> when the state
/// becomes active and <see cref="Release"/> when it is left; <see cref="Tick"/>
/// and <see cref="Render"/> run each frame. <see cref="ProcessPacket"/> returns
/// true when it consumed the packet (the base/shared opcodes handled first, like
/// the C++ ProcessPacket chain).
/// </summary>
public abstract class GameState
{
    /// <summary>Set by the machine so states can request a transition.</summary>
    public GameStateMachine? Machine { get; internal set; }

    public abstract string Name { get; }

    /// <summary>CGameProcedure::Init — entered when this becomes the active state.</summary>
    public virtual void Init()
    {
    }

    /// <summary>CGameProcedure::Release — left for another state.</summary>
    public virtual void Release()
    {
    }

    /// <summary>CGameProcedure::Tick.</summary>
    public virtual void Tick()
    {
    }

    /// <summary>CGameProcedure::Render.</summary>
    public virtual void Render()
    {
    }

    /// <summary>
    /// CGameProcedure::ProcessPacket — dispatch one de-framed payload (opcode at
    /// byte 0). Returns true if handled.
    /// </summary>
    public virtual bool ProcessPacket(ReadOnlySpan<byte> payload) => false;

    /// <summary>Convenience: switch the machine to another state.</summary>
    protected void SetActive(GameState next) => Machine?.SetActive(next);
}
