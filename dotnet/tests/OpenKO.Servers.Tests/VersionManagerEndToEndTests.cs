using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data.Models;
using OpenKO.Servers.VersionManager;
using OpenKO.TestClient;
using Xunit;

namespace OpenKO.Servers.Tests;

public class VersionManagerEndToEndTests : IAsyncLifetime
{
    private readonly FakeVersionManagerDb _db = new();
    private VersionManagerService? _service;
    private CancellationTokenSource? _cts;

    public async Task InitializeAsync()
    {
        _db.VersionList = [new VersionRow(1298, "patch.zip", "patch.zip", 0)];
        _db.Accounts["tester"] = ("secret", 1);

        var state = new VersionManagerState
        {
            Servers =
            [
                new ServerInfo { ServerIP = "127.0.0.1"u8.ToArray(), ServerName = "TEST|Server 1"u8.ToArray(), ServerId = 1, UserLimit = 3000 },
            ],
            FtpUrl = "127.0.0.1"u8.ToArray(),
            FtpPath = "/"u8.ToArray(),
            News = [],
        };

        _service = new VersionManagerService(
            state, _db, NullLogger<VersionManagerService>.Instance, listenPort: 0, maxSessions: 10);
        _cts = new CancellationTokenSource();
        await _service.StartAsync(_cts.Token);
        await _service.Started.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async Task DisposeAsync()
    {
        if (_cts is not null)
            await _cts.CancelAsync();
        if (_service is not null)
            await _service.StopAsync(CancellationToken.None);
        _cts?.Dispose();
    }

    [Fact]
    public async Task FullSession_OverRealTcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new KoTestClient();
        await client.ConnectAsync(_service!.LocalEndPoint!, cts.Token);

        // LS_VERSION_REQ
        byte[] version = await client.RequestAsync([0x01], cts.Token);
        Assert.Equal(new byte[] { 0x01, 0x12, 0x05 }, version); // 1298 LE

        // LS_SERVERLIST
        byte[] serverList = await client.RequestAsync([0xF5], cts.Token);
        Assert.Equal(0xF5, serverList[0]);
        Assert.Equal(1, serverList[1]);

        // LS_LOGIN_REQ (ok, no premium → -1)
        byte[] login = await client.RequestAsync(
            [0xF3, 0x06, 0x00, .. "tester"u8.ToArray(), 0x06, 0x00, .. "secret"u8.ToArray()], cts.Token);
        Assert.Equal(new byte[] { 0xF3, 0x01, 0xFF, 0xFF }, login);

        // LS_NEWS (empty)
        byte[] news = await client.RequestAsync([0xF6], cts.Token);
        Assert.Equal(0xF6, news[0]);

        // Multiple requests on one connection worked → session/framer state is correct.
    }
}
