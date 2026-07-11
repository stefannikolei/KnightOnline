using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Config;
using OpenKO.Data;
using OpenKO.Hosting;

namespace OpenKO.Servers.Aujard;

/// <summary>
/// Thin standalone host for the DB agent library. In the modernized topology the
/// agent primarily runs embedded in the (stage-4) C# Ebenezer process; this host
/// exists to validate configuration and database connectivity on its own, reading
/// the same Aujard.ini ([ODBC] GAME_DSN/ACCOUNT_DSN etc.) as the C++ binary.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = KoHost.CreateBuilder(args);

        var ini = new IniFile();
        ini.Load(KoHost.ResolveConfigPath("Aujard.ini"));

        // The C++ configures ACCOUNT and GAME datasources separately (both default
        // to KN_online); the stage-1/2 docker setup uses a single database.
        string dsn = ini.GetString("ODBC", "GAME_DSN", "KN_online");
        string uid = ini.GetString("ODBC", "GAME_UID", "knight");
        string pwd = ini.GetString("ODBC", "GAME_PWD", "knight");
        string server = ini.GetString("ODBC", "SERVER", "");

        builder.Services.AddSingleton(SqlConnectionFactory.FromOdbcConfig(
            dsn, uid, pwd, server.Length > 0 ? server : null));
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
