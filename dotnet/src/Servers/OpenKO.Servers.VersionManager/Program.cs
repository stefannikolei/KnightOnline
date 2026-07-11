using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Config;
using OpenKO.Data;
using OpenKO.Hosting;

namespace OpenKO.Servers.VersionManager;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = KoHost.CreateBuilder(args);

        string configPath = KoHost.ResolveConfigPath("Version.ini");
        var ini = new IniFile();
        if (!ini.Load(configPath))
        {
            Console.Error.WriteLine($"VersionManager: config not found: {configPath}");
            return 1;
        }

        VersionManagerConfig? config = VersionManagerConfig.Load(ini, msg => Console.Error.WriteLine(msg));
        if (config is null)
            return 1;

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(VersionManagerState.FromConfig(config));
        builder.Services.AddSingleton(SqlConnectionFactory.FromOdbcConfig(
            config.DataSourceName, config.DataSourceUser, config.DataSourcePassword, config.DataSourceServer));
        builder.Services.AddSingleton<IVersionManagerDb, SqlVersionManagerDb>();
        builder.Services.AddSingleton<VersionManagerService>(sp => new VersionManagerService(
            sp.GetRequiredService<VersionManagerState>(),
            sp.GetRequiredService<IVersionManagerDb>(),
            sp.GetRequiredService<ILogger<VersionManagerService>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<VersionManagerService>());

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}
