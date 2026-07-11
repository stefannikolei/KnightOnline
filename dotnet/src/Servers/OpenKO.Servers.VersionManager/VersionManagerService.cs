using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Data.Models;
using OpenKO.Network.Tcp;

namespace OpenKO.Servers.VersionManager;

/// <summary>
/// Hosted service replacing VersionManagerApp::OnStart: connects to the DB,
/// loads the VERSION table (refusing to start when empty, like Load_ForbidEmpty),
/// then listens on port 15100 for the plaintext login protocol.
/// </summary>
public sealed class VersionManagerService(
    VersionManagerState state,
    IVersionManagerDb db,
    ILogger<VersionManagerService> logger,
    int listenPort = VersionManagerConfig.ListenPort,
    int maxSessions = VersionManagerConfig.MaxUser) : BackgroundService
{
    private KoTcpServer? _server;

    /// <summary>Bound endpoint (for tests using port 0).</summary>
    public IPEndPoint? LocalEndPoint => _server?.LocalEndPoint;

    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the listener is accepting connections (test hook).</summary>
    public Task Started => _started.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<VersionRow>? versionList = await db.LoadVersionListAsync(stoppingToken);
        if (versionList is null)
        {
            logger.LogError("Database Connection Fail!!");
            _started.TrySetException(new InvalidOperationException("database connection failed"));
            throw new InvalidOperationException("database connection failed");
        }

        if (versionList.Count == 0)
        {
            // recordset_loader Load_ForbidEmpty: an empty VERSION table is fatal.
            logger.LogError("Load Version List Fail!!");
            _started.TrySetException(new InvalidOperationException("VERSION table is empty"));
            throw new InvalidOperationException("VERSION table is empty");
        }

        state.SwapVersionList(versionList);
        logger.LogInformation("Latest Version: {Version}", state.LastVersion);

        var handler = new LoginPacketHandler(state, db);

        _server = new KoTcpServer(
            new IPEndPoint(IPAddress.Any, listenPort),
            maxSessions,
            async (session, payload) =>
            {
                byte[]? response = await handler.HandleAsync(payload, stoppingToken);
                if (response is not null)
                    session.Send(response);
            },
            logger);

        _server.Start();
        logger.LogInformation("Listening on 0.0.0.0:{Port}", ((IPEndPoint)_server.LocalEndPoint!).Port);
        _started.TrySetResult();

        await _server.RunAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_server is not null)
            await _server.DisposeAsync();
    }
}
