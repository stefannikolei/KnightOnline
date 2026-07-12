using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Config;
using OpenKO.Data;
using OpenKO.Hosting;
using OpenKO.Servers.Aujard;
using OpenKO.Servers.Ebenezer.Net;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Ebenezer host (stage-4.1 slice): reads the same server.ini as the C++
/// ([ZONE_INFO] MY_INFO → listen port 15000+n, [ODBC] GAME_DSN/UID/PWD) and
/// accepts game clients with the CUser socket layer (framing + WIZ_CRYPTION).
/// The Aujard DB agent runs embedded as a library — the KNIGHT_SEND/RECV
/// shared-memory queues of the C++ topology are replaced by direct calls.
/// </summary>
public static class Program
{
    public const int ListenPortBase = 15000; // _LISTEN_PORT

    public static async Task<int> Main(string[] args)
    {
        var builder = KoHost.CreateBuilder(args);

        var ini = new IniFile();
        ini.Load(KoHost.ResolveConfigPath("server.ini"));

        int serverNo = ini.GetInt("ZONE_INFO", "MY_INFO", 1);
        string dsn = ini.GetString("ODBC", "GAME_DSN", "KN_online");
        string uid = ini.GetString("ODBC", "GAME_UID", "knight");
        string pwd = ini.GetString("ODBC", "GAME_PWD", "knight");
        string server = ini.GetString("ODBC", "SERVER", "");

        int listenPort = ListenPortBase + serverNo;

        builder.Services.AddSingleton(SqlConnectionFactory.FromOdbcConfig(
            dsn, uid, pwd, server.Length > 0 ? server : null));
        builder.Services.AddSingleton<IDbAgent, DbAgent>(sp => new DbAgent(
            sp.GetRequiredService<SqlConnectionFactory>(),
            sp.GetRequiredService<ILogger<DbAgent>>()));
        builder.Services.AddSingleton(sp => new EbenezerService(
            listenPort,
            sp.GetRequiredService<IDbAgent>(),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<ILogger<EbenezerService>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EbenezerService>());

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}

/// <summary>
/// Accepts game-client connections and runs the single-writer game loop: every
/// received chunk is queued and de-framed/dispatched on one loop, preserving
/// the serialization the C++ enforced with its recursive mutexes.
/// </summary>
public sealed class EbenezerService(
    int listenPort,
    IDbAgent dbAgent,
    IHostApplicationLifetime lifetime,
    ILogger<EbenezerService> logger) : BackgroundService
{
    private sealed record SessionWork(GameSession Session, byte[]? Data);

    private TcpListener? _listener;

    public EbenezerWorld World { get; } = new();

    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.Server.LocalEndPoint;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await dbAgent.InitAsync(stoppingToken))
        {
            logger.LogError("Database Connection Fail!!");
            lifetime.StopApplication();
            return;
        }

        Channel<SessionWork> queue = Channel.CreateUnbounded<SessionWork>(
            new UnboundedChannelOptions { SingleReader = true });

        _listener = new TcpListener(IPAddress.Any, listenPort);
        _listener.Start(backlog: 512);
        logger.LogInformation("Listening on 0.0.0.0:{Port}", ((IPEndPoint)_listener.Server.LocalEndPoint!).Port);

        Task acceptLoop = AcceptLoopAsync(queue.Writer, stoppingToken);
        Task gameLoop = GameLoopAsync(queue.Reader, stoppingToken);

        await Task.WhenAll(acceptLoop, gameLoop);
    }

    private async Task AcceptLoopAsync(ChannelWriter<SessionWork> queue, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener!.AcceptSocketAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            short socketId = World.Register(id => new GameUser(id, World, dbAgent, logger));
            if (socketId < 0)
            {
                logger.LogWarning("server full, rejecting {Remote}", socket.RemoteEndPoint);
                socket.Dispose();
                continue;
            }

            var session = new GameSession(socket, World.Users[socketId]!, logger);
            logger.LogInformation("user {Id} connected from {Remote}", socketId, socket.RemoteEndPoint);

            _ = RunSessionAsync(session, queue, ct);
        }

        queue.TryComplete();
    }

    private async Task RunSessionAsync(GameSession session, ChannelWriter<SessionWork> queue, CancellationToken ct)
    {
        try
        {
            await session.ReceiveLoopAsync(
                data => queue.TryWrite(new SessionWork(session, data)), ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "user {Id}: receive loop ended", session.User.SocketId);
        }
        finally
        {
            // Close notification runs on the game loop too (CloseProcess ordering).
            queue.TryWrite(new SessionWork(session, null));
        }
    }

    private async Task GameLoopAsync(ChannelReader<SessionWork> queue, CancellationToken ct)
    {
        await foreach (SessionWork work in queue.ReadAllAsync(CancellationToken.None))
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                if (work.Data is null)
                {
                    World.Unregister(work.Session.User.SocketId);
                    logger.LogInformation("user {Id} disconnected", work.Session.User.SocketId);
                    work.Session.Dispose();
                }
                else
                {
                    await work.Session.ProcessReceivedAsync(work.Data);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "user {Id}: packet processing failed", work.Session.User.SocketId);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _listener?.Stop();
    }
}

/// <summary>
/// One accepted game-client socket: owns the receive loop and a serialized send
/// queue; framing/cryption and dispatch live in <see cref="GameUser"/> and run
/// on the service's single-writer game loop.
/// </summary>
public sealed class GameSession : IDisposable
{
    private readonly Socket _socket;
    private readonly ILogger _logger;
    private readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    public GameUser User { get; }

    public GameSession(Socket socket, GameUser user, ILogger logger)
    {
        _socket = socket;
        _logger = logger;
        User = user;

        user.Transmit = frame => _sendQueue.Writer.TryWrite(frame);
        user.Close = () => _cts.Cancel();
    }

    /// <summary>Reads raw chunks off the socket and hands them to the game loop.</summary>
    public async Task ReceiveLoopAsync(Action<byte[]> onData, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        Task sendLoop = SendLoopAsync(linked.Token);

        try
        {
            var buffer = new byte[8192];
            while (!linked.Token.IsCancellationRequested)
            {
                int received = await _socket.ReceiveAsync(buffer.AsMemory(), linked.Token);
                if (received == 0)
                    break;

                onData(buffer.AsSpan(0, received).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            _sendQueue.Writer.TryComplete();
            await sendLoop;
        }
    }

    /// <summary>Runs on the game loop: feed, de-frame (PullOutCore) and dispatch.</summary>
    public async ValueTask ProcessReceivedAsync(byte[] data)
    {
        User.Core.Feed(data);

        while (true)
        {
            FrameResult result = User.Core.TryReadPacket(out byte[] packet);
            if (result == FrameResult.NeedMore)
                break;

            if (result == FrameResult.Close)
            {
                _logger.LogWarning("user {Id}: protocol violation, closing", User.SocketId);
                _cts.Cancel();
                break;
            }

            if (packet.Length == 0)
                continue;

            await User.ParsingAsync(packet);
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (byte[] frame in _sendQueue.Reader.ReadAllAsync(ct))
                await _socket.SendAsync(frame.AsMemory(), SocketFlags.None, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Dispose();
        _cts.Dispose();
    }
}
