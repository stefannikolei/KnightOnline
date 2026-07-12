using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Config;
using OpenKO.Data;
using OpenKO.Hosting;
using OpenKO.Network.Tcp;

namespace OpenKO.Servers.AIServer;

/// <summary>
/// AIServer host (stage-3 skeleton): reads the same server.ini as the C++
/// ([SERVER] ZONE, [ODBC] GAME_DSN/UID/PWD), listens on the zone-type port and
/// accepts Ebenezer's per-zone connections with the AI_SERVER_CONNECT handshake
/// and AG_COMPRESSED_DATA handling. NPC/user game logic attaches to
/// <see cref="EbenezerLink.PacketReceived"/> as it is ported.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = KoHost.CreateBuilder(args);

        var ini = new IniFile();
        ini.Load(KoHost.ResolveConfigPath("server.ini"));

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

        builder.Services.AddSingleton(SqlConnectionFactory.FromOdbcConfig(
            dsn, uid, pwd, server.Length > 0 ? server : null));
        builder.Services.AddSingleton(sp => new AiServerService(
            listenPort,
            sp.GetRequiredService<ILogger<AiServerService>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AiServerService>());

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}

public sealed class AiServerService(int listenPort, ILogger<AiServerService> logger) : BackgroundService
{
    private KoTcpServer? _server;

    public IPEndPoint? LocalEndPoint => _server?.LocalEndPoint;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var links = new Dictionary<int, EbenezerLink>();

        _server = new KoTcpServer(
            new IPEndPoint(IPAddress.Any, listenPort),
            AiServerPorts.MaxSockets,
            async (session, payload) =>
            {
                if (!links.TryGetValue(session.Id, out EbenezerLink? link))
                {
                    link = new EbenezerLink(session, logger);
                    links[session.Id] = link;
                }

                await link.DispatchAsync(payload);
            },
            logger);

        _server.Start();
        logger.LogInformation("Listening on 0.0.0.0:{Port}", ((IPEndPoint)_server.LocalEndPoint!).Port);

        await _server.RunAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_server is not null)
            await _server.DisposeAsync();
    }
}
