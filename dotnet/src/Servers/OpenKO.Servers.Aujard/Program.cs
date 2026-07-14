using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Data;
using OpenKO.Hosting;

namespace OpenKO.Servers.Aujard;

/// <summary>
/// Thin standalone host for the DB agent library. In the modernized topology the
/// agent primarily runs embedded in the (stage-4) C# Ebenezer process; this host
/// exists to validate configuration and database connectivity on its own. The
/// database is configured via appsettings.json (ConnectionStrings:GameDb, or the
/// Database section) — the C++'s separate ACCOUNT/GAME datasources collapse to
/// one, matching the docker setup.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = KoHost.CreateBuilder(args);

        builder.Services.AddGameDatabase(builder.Configuration);
        builder.Services.AddSingleton<IDbAgent, DbAgent>(sp => new DbAgent(
            sp.GetRequiredService<SqlConnectionFactory>(),
            sp.GetRequiredService<ILogger<DbAgent>>()));
        builder.Services.AddHostedService<DbAgentHostService>();

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}

public sealed class DbAgentHostService(IDbAgent agent, IHostApplicationLifetime lifetime, ILogger<DbAgentHostService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await agent.InitAsync(stoppingToken))
        {
            logger.LogError("Database Connection Fail!!");
            lifetime.StopApplication();
            return;
        }

        logger.LogInformation("Aujard DB agent ready ({Capacity} user slots)", agent.Users.Capacity);

        // Nothing to serve standalone yet — the agent is a library for Ebenezer.
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
    }
}
