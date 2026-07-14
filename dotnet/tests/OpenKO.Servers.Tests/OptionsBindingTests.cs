using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenKO.Data;
using OpenKO.Servers.AIServer;
using OpenKO.Servers.Ebenezer;
using OpenKO.Servers.VersionManager;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Pins the modernized configuration: appsettings-shaped IConfiguration binds
/// into the per-server Options, DataAnnotations validation fires, and the DB
/// seam resolves the connection string.
/// </summary>
public class OptionsBindingTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>Binds + validates an options section the way each server's Program.cs does.</summary>
    private static T BindValidated<T>(IConfiguration config, string section) where T : class
    {
        var services = new ServiceCollection();
        services.AddOptions<T>().Bind(config.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
        using ServiceProvider sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<T>>().Value;
    }

    [Fact]
    public void VersionManagerOptions_BindAndCompileToWireConfig()
    {
        IConfiguration config = Config(new()
        {
            ["VersionManager:Download:Url"] = "patch.example.com",
            ["VersionManager:Download:Path"] = "/files",
            ["VersionManager:ServerList:0:Id"] = "7",
            ["VersionManager:ServerList:0:Ip"] = "10.0.0.1",
            ["VersionManager:ServerList:0:Name"] = "Zone|Alpha",
            ["VersionManager:ServerList:0:UserLimit"] = "1500",
            ["VersionManager:News:0:Title"] = "Hi",
            ["VersionManager:News:0:Message"] = "Welcome",
        });

        VersionManagerOptions options = BindValidated<VersionManagerOptions>(config, VersionManagerOptions.SectionName);
        Assert.Equal("patch.example.com", options.Download.Url);
        Assert.Single(options.ServerList);

        VersionManagerConfig compiled = VersionManagerConfig.FromOptions(options);
        Assert.Equal("patch.example.com", Encoding.Latin1.GetString(compiled.FtpUrl));
        Assert.Single(compiled.Servers);
        Assert.Equal((short)7, compiled.Servers[0].ServerId);
        Assert.Equal("10.0.0.1", Encoding.Latin1.GetString(compiled.Servers[0].ServerIP));
        Assert.Equal((short)1500, compiled.Servers[0].UserLimit);
        Assert.NotEmpty(compiled.News); // the news blob was assembled
    }

    [Fact]
    public void VersionManagerOptions_EmptyServerList_FailsValidation()
    {
        IConfiguration config = Config(new()
        {
            ["VersionManager:Download:Url"] = "x",
            ["VersionManager:Download:Path"] = "/",
        });

        Assert.Throws<OptionsValidationException>(() =>
            BindValidated<VersionManagerOptions>(config, VersionManagerOptions.SectionName));
    }

    [Fact]
    public void EbenezerOptions_BindsServerPeers()
    {
        IConfiguration config = Config(new()
        {
            ["Ebenezer:ServerNo"] = "2",
            ["Ebenezer:AiServerIp"] = "10.1.1.1",
            ["Ebenezer:Servers:0:No"] = "1",
            ["Ebenezer:Servers:0:Ip"] = "10.0.0.1",
            ["Ebenezer:Servers:1:No"] = "2",
            ["Ebenezer:Servers:1:Ip"] = "10.0.0.2",
        });

        EbenezerOptions options = BindValidated<EbenezerOptions>(config, EbenezerOptions.SectionName);
        Assert.Equal(2, options.ServerNo);
        Assert.Equal("10.1.1.1", options.AiServerIp);
        Assert.Equal(2, options.Servers.Count);
        Assert.Equal((short)2, options.Servers[1].No);
        Assert.Equal("10.0.0.2", options.Servers[1].Ip);
    }

    [Fact]
    public void AiServerOptions_ZoneOutOfRange_FailsValidation()
    {
        IConfiguration ok = Config(new() { ["AiServer:Zone"] = "3" });
        Assert.Equal(3, BindValidated<AiServerOptions>(ok, AiServerOptions.SectionName).Zone);

        IConfiguration bad = Config(new() { ["AiServer:Zone"] = "9" });
        Assert.Throws<OptionsValidationException>(() =>
            BindValidated<AiServerOptions>(bad, AiServerOptions.SectionName));
    }

    [Fact]
    public void AddGameDatabase_PrefersConnectionString()
    {
        IConfiguration config = Config(new()
        {
            ["ConnectionStrings:GameDb"] = "Server=db.example.com;Database=KN_online;User ID=u;Password=p;TrustServerCertificate=True",
            ["Database:Dsn"] = "IGNORED",
        });

        var services = new ServiceCollection();
        services.AddGameDatabase(config);
        using ServiceProvider sp = services.BuildServiceProvider();

        string cs = sp.GetRequiredService<SqlConnectionFactory>().ConnectionString;
        Assert.Contains("db.example.com", cs);
        Assert.DoesNotContain("IGNORED", cs);
    }

    [Fact]
    public void AddGameDatabase_FallsBackToDatabaseSection()
    {
        IConfiguration config = Config(new()
        {
            ["Database:Dsn"] = "KN_online",
            ["Database:Uid"] = "knight",
            ["Database:Pwd"] = "secret",
            ["Database:Server"] = "sql.internal",
        });

        var services = new ServiceCollection();
        services.AddGameDatabase(config);
        using ServiceProvider sp = services.BuildServiceProvider();

        string cs = sp.GetRequiredService<SqlConnectionFactory>().ConnectionString;
        Assert.Contains("sql.internal", cs);
        Assert.Contains("KN_online", cs);
    }
}
