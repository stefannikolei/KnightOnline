using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenKO.Data;
using OpenKO.Hosting;

namespace OpenKO.Servers.VersionManager;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = KoHost.CreateBuilder(args);

        builder.Services.AddOptions<VersionManagerOptions>()
            .Bind(builder.Configuration.GetSection(VersionManagerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddGameDatabase(builder.Configuration);

        // The wire-ready config is compiled once from the validated options.
        builder.Services.AddSingleton(sp =>
            VersionManagerConfig.FromOptions(sp.GetRequiredService<IOptions<VersionManagerOptions>>().Value));
        builder.Services.AddSingleton(sp =>
            VersionManagerState.FromConfig(sp.GetRequiredService<VersionManagerConfig>()));
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
