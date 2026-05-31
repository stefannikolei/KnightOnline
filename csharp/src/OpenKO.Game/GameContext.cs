using OpenKO.Common;
using OpenKO.Game.Rendering;
using OpenKO.Net;

namespace OpenKO.Game;

/// <summary>
/// Holds the services and session state shared across every <see cref="GameProcedure"/> — the
/// portable counterpart of the dozens of <c>static</c> members on the C++ <c>CGameProcedure</c>
/// (sockets, the active account/server, the selected character index, …).
///
/// Keeping these here rather than as global statics makes the whole state machine constructible and
/// unit-testable in isolation (no DirectX, no window, no real socket required).
/// </summary>
public sealed class GameContext
{
    public GameContext()
    {
        Procedures = new GameProcedureManager(this);
    }

    /// <summary>The state machine that owns procedure switching for this context.</summary>
    public GameProcedureManager Procedures { get; }

    /// <summary>Primary connection — the login server, then the game server (port of <c>s_pSocket</c>).</summary>
    public ApiSocket? MainSocket { get; set; }

    /// <summary>Secondary connection used for side channels (port of <c>s_pSocketSub</c>).</summary>
    public ApiSocket? SubSocket { get; set; }

    /// <summary>The 2D UI drawing surface (port of the role of <c>s_pUIMgr</c>'s renderer). Set by the host.</summary>
    public IUiRenderer? UiRenderer { get; set; }

    // ---- session state (port of CGameProcedure::s_szAccount / s_szPassWord / s_szServer / ...) ----

    public string Account { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int CharacterSelectIndex { get; set; }

    /// <summary>
    /// Drain any packets queued by <see cref="MainSocket"/>'s receive loop and dispatch them to the
    /// active procedure. Call once per frame from the game loop so packet handling happens on the
    /// main thread (the socket decodes on a background thread).
    /// </summary>
    public void PumpNetwork()
    {
        if (MainSocket == null)
            return;

        Packet? packet;
        while ((packet = MainSocket.Receive()) != null)
            Procedures.DispatchPacket(packet);
    }
}
