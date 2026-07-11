using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Config;
using OpenKO.Hosting;
using OpenKO.Servers.ItemManager.Transport;

namespace OpenKO.Servers.ItemManager;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = KoHost.CreateBuilder(args);

        // ItemManager.ini is optional (all keys have defaults), like the C++ where
        // the INI only configures logging.
        var ini = new IniFile();
        ini.Load(KoHost.ResolveConfigPath("ItemManager.ini"));

        string itemLogFile = ini.GetString("LOGGER", "ITEM_LOG_FILE", "logs/ItemLog.txt");
        string expLogFile = ini.GetString("LOGGER", "EXP_LOG_FILE", "logs/ExpLog.txt");
        // .NET port extension: the SHM queue is replaced by a TCP loopback listener.
        int port = ini.GetInt("ITEMLOG", "PORT", TcpItemLogSource.DefaultPort);

        builder.Services.AddSingleton(sp => new TcpItemLogSource(
            new IPEndPoint(IPAddress.Loopback, port),
            sp.GetRequiredService<ILogger<ItemManagerService>>()));
        builder.Services.AddSingleton<IItemLogSource>(sp => sp.GetRequiredService<TcpItemLogSource>());
        builder.Services.AddKeyedSingleton("item", (_, _) => new DailyFileLogger(itemLogFile, "ItemManagerItem"));
        builder.Services.AddKeyedSingleton("exp", (_, _) => new DailyFileLogger(expLogFile, "ItemManagerExp"));
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
