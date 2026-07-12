using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Config;
using OpenKO.Data;
using OpenKO.Hosting;
using OpenKO.Network.Tcp;
using OpenKO.Servers.AIServer.Ai;
using OpenKO.Servers.AIServer.Db;

namespace OpenKO.Servers.AIServer;

/// <summary>
/// AIServer host: reads the same server.ini as the C++ ([SERVER] ZONE, [ODBC]
/// GAME_DSN/UID/PWD), loads the GAME-DB tables and maps, spawns the NPCs and
/// listens on the zone-type port for Ebenezer's per-zone connections. All game
/// state runs on one single-writer loop (replacing the C++ NpcThread/
/// ZoneEventThread/timer + mutex model): inbound packets are queued and drained
/// between NPC ticks.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = KoHost.CreateBuilder(args);

        string configPath = KoHost.ResolveConfigPath("server.ini");
        var ini = new IniFile();
        ini.Load(configPath);

        int serverZoneType = ini.GetInt("SERVER", "ZONE", 1);
        string dsn = ini.GetString("ODBC", "GAME_DSN", "KN_online");
        string uid = ini.GetString("ODBC", "GAME_UID", "knight");
        string pwd = ini.GetString("ODBC", "GAME_PWD", "knight");
        string server = ini.GetString("ODBC", "SERVER", "");

        int listenPort = serverZoneType switch
        {
            0 or 1 => AiServerPorts.Karus,   // UNIFY_ZONE / KARUS_ZONE
            2 => AiServerPorts.Elmorad,      // ELMORAD_ZONE
            3 => AiServerPorts.Battle,       // BATTLE_ZONE
            _ => -1,
        };

        if (listenPort < 0)
        {
            Console.Error.WriteLine($"AIServer: invalid [SERVER] ZONE type: {serverZoneType}");
            return 1;
        }

        // The C++ loads maps from GetProgPath()/MAP; resolve next to server.ini.
        string mapDirectory = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", "MAP");

        builder.Services.AddSingleton(SqlConnectionFactory.FromOdbcConfig(
            dsn, uid, pwd, server.Length > 0 ? server : null));
        builder.Services.AddSingleton<AiServerDb>();
        builder.Services.AddSingleton(sp => new AiServerService(
            listenPort,
            serverZoneType,
            mapDirectory,
            sp.GetRequiredService<AiServerDb>(),
            sp.GetRequiredService<ILogger<AiServerService>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AiServerService>());

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}

public sealed class AiServerService(
    int listenPort,
    int serverZoneType,
    string mapDirectory,
    AiServerDb db,
    ILogger<AiServerService> logger) : BackgroundService
{
    private const double NpcTickInterval = 0.1;   // NpcThread 100ms cadence
    private const double RoomTickInterval = 1.0;  // ZoneEventThread 1s cadence
    private const double AliveInterval = 10.0;    // CheckAliveTest 10s timer

    private KoTcpServer? _server;

    public IPEndPoint? LocalEndPoint => _server?.LocalEndPoint;

    public AiWorld World { get; } = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var handlers = new GameSocketHandlers(World, logger);
        var app = new AiServerApp(World, handlers, serverZoneType, logger);

        if (!await app.StartupAsync(db, mapDirectory, stoppingToken))
        {
            logger.LogError("AIServer startup failed, closing server");
            return;
        }

        // Inbound work funnels through this queue into the single-writer loop.
        Channel<Func<ValueTask>> queue = Channel.CreateUnbounded<Func<ValueTask>>(
            new UnboundedChannelOptions { SingleReader = true });

        var links = new Dictionary<int, EbenezerLink>();

        _server = new KoTcpServer(
            new IPEndPoint(IPAddress.Any, listenPort),
            AiServerPorts.MaxSockets,
            (session, payload) =>
            {
                if (!links.TryGetValue(session.Id, out EbenezerLink? link))
                {
                    link = new EbenezerLink(session, logger);
                    link.ZoneConnected += (l, zone, reconnect) => app.OnZoneConnected(l, zone, reconnect);
                    handlers.Attach(link);
                    links[session.Id] = link;
                }

                queue.Writer.TryWrite(() => link.DispatchAsync(payload));
                return ValueTask.CompletedTask;
            },
            logger);

        _server.Start();
        logger.LogInformation("Listening on 0.0.0.0:{Port}", ((IPEndPoint)_server.LocalEndPoint!).Port);

        Task acceptLoop = _server.RunAsync(stoppingToken);
        Task gameLoop = RunGameLoopAsync(app, queue.Reader, stoppingToken);

        await Task.WhenAll(acceptLoop, gameLoop);
    }

    /// <summary>
    /// The single-writer game loop: drains queued packets, then runs the
    /// NpcThread / ZoneEventThread / CheckAliveTest cadences.
    /// </summary>
    private async Task RunGameLoopAsync(
        AiServerApp app,
        ChannelReader<Func<ValueTask>> queue,
        CancellationToken ct)
    {
        double lastNpcTick = 0.0;
        double lastRoomTick = 0.0;
        double lastAliveCheck = 0.0;

        while (!ct.IsCancellationRequested)
        {
            while (queue.TryRead(out Func<ValueTask>? work))
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "packet handler failed");
                }
            }

            double now = World.Clock();

            if (now - lastNpcTick >= NpcTickInterval)
            {
                lastNpcTick = now;
                foreach (Npc npc in World.Npcs.Values)
                {
                    try
                    {
                        npc.Tick(now);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "NPC tick failed [serial={Serial} npcId={NpcId}]", npc.Nid, npc.Sid);
                    }
                }
            }

            if (now - lastRoomTick >= RoomTickInterval)
            {
                lastRoomTick = now;
                World.TickRoomEvents(now);
            }

            if (now - lastAliveCheck >= AliveInterval)
            {
                lastAliveCheck = now;
                app.CheckAliveTest();
            }

            try
            {
                await Task.Delay(10, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_server is not null)
            await _server.DisposeAsync();
    }
}
