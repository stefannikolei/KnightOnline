namespace OpenKO.Client.Game.States;

/// <summary>
/// Placeholder for CGameProcMain (the in-game state). Stage 7.1/7.2 transition
/// into it on WIZ_SEL_CHAR success; stage 7.3 fills in the zone load,
/// WIZ_GAMESTART handshake and the world/HUD.
/// </summary>
public sealed class InGameState(GameContext context) : GameState
{
    public override string Name => "InGame";

    /// <summary>True once the state has been entered (the char-select spawn is set).</summary>
    public bool Entered { get; private set; }

    public override void Init() => Entered = true;

    public override void Release() => Entered = false;

    /// <summary>The spawn zone/position carried over from char select.</summary>
    public Net.SelectCharResult Spawn => context.Spawn;
}
