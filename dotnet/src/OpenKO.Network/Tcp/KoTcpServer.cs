using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace OpenKO.Network.Tcp;

/// <summary>
/// Accept loop with a fixed session cap, replacing the C++ SocketManager
/// (fixed socket array + asio acceptor). Connections beyond the cap are
/// closed immediately, matching the C++ behavior when no socket slot is free.
/// </summary>
public sealed class KoTcpServer : IAsyncDisposable
{
    private readonly IPEndPoint _endPoint;
    private readonly int _maxSessions;
    private readonly ILogger _logger;
    private readonly Func<KoSession, byte[], ValueTask> _onPacket;
    private readonly ConcurrentDictionary<int, KoSession> _sessions = new();

    private Socket? _listener;
    private int _nextSessionId;

    public KoTcpServer(
        IPEndPoint endPoint,
        int maxSessions,
        Func<KoSession, byte[], ValueTask> onPacket,
        ILogger logger)
    {
        _endPoint = endPoint;
        _maxSessions = maxSessions;
        _onPacket = onPacket;
        _logger = logger;
    }

    public int SessionCount => _sessions.Count;

    /// <summary>The actually bound endpoint (relevant when port 0 was requested in tests).</summary>
    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.LocalEndPoint;

    public void Start()
    {
        _listener = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Bind(_endPoint);
        _listener.Listen(backlog: 512);
        _logger.LogInformation("listening on {EndPoint}", _listener.LocalEndPoint);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
            throw new InvalidOperationException("call Start() first");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket client = await _listener.AcceptAsync(cancellationToken);

                if (_sessions.Count >= _maxSessions)
                {
                    _logger.LogWarning("session cap {Max} reached; rejecting {Remote}",
                        _maxSessions, client.RemoteEndPoint);
                    client.Dispose();
                    continue;
                }

                client.NoDelay = true;

                int id = Interlocked.Increment(ref _nextSessionId);
                var session = new KoSession(id, client, _logger) { OnPacket = _onPacket };
                session.Closed += s => _sessions.TryRemove(s.Id, out _);
                _sessions[id] = session;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await session.RunAsync(cancellationToken);
                    }
                    finally
                    {
                        await session.DisposeAsync();
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener?.Dispose();
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
        _sessions.Clear();
    }
}
