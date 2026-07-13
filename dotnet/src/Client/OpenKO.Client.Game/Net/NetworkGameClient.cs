using System.Collections.Concurrent;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The live <see cref="IGameClient"/> over a <see cref="KoClientConnection"/>.
/// Received packets are queued (like the C++ WM_SOCKETMSG → m_qRecvPkt) and
/// drained on the game-loop thread via <see cref="Pump"/>, so state handlers run
/// single-threaded. Endpoint switches (login → game server) are surfaced through
/// <see cref="ConnectRequested"/> for the host to service, mirroring the reused
/// <c>s_pSocket</c>.
/// </summary>
public sealed class NetworkGameClient : IGameClient
{
    private readonly ConcurrentQueue<byte[]> _incoming = new();
    private KoClientConnection _connection;

    public NetworkGameClient(KoClientConnection connection)
    {
        _connection = connection;
        _connection.OnPacket = (_, payload) =>
        {
            _incoming.Enqueue(payload);
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>Raised when a state asks to (re)connect to a server endpoint.</summary>
    public event Action<string, int>? ConnectRequested;

    public bool CryptionEnabled => _connection.CryptionEnabled;

    /// <summary>Swaps in a fresh link after a reconnect (crypto resets with it).</summary>
    public void AttachConnection(KoClientConnection connection)
    {
        _connection = connection;
        _connection.OnPacket = (_, payload) =>
        {
            _incoming.Enqueue(payload);
            return ValueTask.CompletedTask;
        };
    }

    public void Send(ReadOnlySpan<byte> payload) => _connection.Send(payload);

    public void Connect(string host, int port) => ConnectRequested?.Invoke(host, port);

    public void EnableCryption(ulong publicKey) => _connection.EnableCryption(publicKey);

    /// <summary>
    /// Drains queued packets into the state machine (call once per frame from the
    /// game loop — the CGameProcedure::Tick receive drain).
    /// </summary>
    public void Pump(GameStateMachine machine)
    {
        while (_incoming.TryDequeue(out byte[]? payload))
            machine.DispatchPacket(payload);
    }
}
