using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenKO.Hosting;
using OpenKO.Servers.ItemManager.Transport;

namespace OpenKO.Servers.ItemManager;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = KoHost.CreateBuilder(args);

        builder.Services.AddOptions<ItemManagerOptions>()
            .Bind(builder.Configuration.GetSection(ItemManagerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton(sp => new TcpItemLogSource(
            new IPEndPoint(IPAddress.Loopback, sp.GetRequiredService<IOptions<ItemManagerOptions>>().Value.Port),
            sp.GetRequiredService<ILogger<ItemManagerService>>()));
        builder.Services.AddSingleton<IItemLogSource>(sp => sp.GetRequiredService<TcpItemLogSource>());
        builder.Services.AddKeyedSingleton("item", (sp, _) =>
            new DailyFileLogger(sp.GetRequiredService<IOptions<ItemManagerOptions>>().Value.ItemLogFile, "ItemManagerItem"));
        builder.Services.AddKeyedSingleton("exp", (sp, _) =>
            new DailyFileLogger(sp.GetRequiredService<IOptions<ItemManagerOptions>>().Value.ExpLogFile, "ItemManagerExp"));
        builder.Services.AddSingleton(sp => new ItemManagerService(
            sp.GetRequiredService<IItemLogSource>(),
            sp.GetRequiredKeyedService<DailyFileLogger>("item"),
            sp.GetRequiredKeyedService<DailyFileLogger>("exp"),
            sp.GetRequiredService<ILogger<ItemManagerService>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ItemManagerService>());
        builder.Services.AddHostedService<TcpItemLogListenerService>();

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}

/// <summary>Runs the TCP listener alongside the consumer service.</summary>
public sealed class TcpItemLogListenerService(TcpItemLogSource source) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        source.Start();
        return source.RunAsync(stoppingToken);
    }
}
