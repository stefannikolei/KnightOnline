using System.Text;
using OpenKO.Data.Models;
using OpenKO.Servers.VersionManager;
using Xunit;

namespace OpenKO.Servers.Tests;

public class LoginPacketHandlerTests
{
    private static VersionManagerState MakeState(
        List<VersionRow>? versions = null,
        List<ServerInfo>? servers = null,
        byte[]? news = null)
    {
        var state = new VersionManagerState
        {
            Servers = servers ?? [],
            FtpUrl = "ftp.example.com"u8.ToArray(),
            FtpPath = "/patch/"u8.ToArray(),
            News = news ?? [],
        };
        state.SwapVersionList(versions ?? []);
        return state;
    }

    private static byte[] LoginRequest(string id, string pw)
    {
        var buffer = new List<byte> { 0xF3 };
        byte[] idBytes = Encoding.ASCII.GetBytes(id);
        byte[] pwBytes = Encoding.ASCII.GetBytes(pw);
        buffer.AddRange(BitConverter.GetBytes((short)idBytes.Length));
        buffer.AddRange(idBytes);
        buffer.AddRange(BitConverter.GetBytes((short)pwBytes.Length));
        buffer.AddRange(pwBytes);
        return buffer.ToArray();
    }

    [Fact]
    public async Task VersionReq_ReturnsLastVersion()
    {
        var state = MakeState([
            new VersionRow(1297, "a.zip", "a.zip", 0),
            new VersionRow(1299, "c.zip", "c.zip", 0),
            new VersionRow(1298, "b.zip", "b.zip", 0),
        ]);
        var handler = new LoginPacketHandler(state, new FakeVersionManagerDb());

        byte[]? response = await handler.HandleAsync([0x01]);

        // [LS_VERSION_REQ][int16 1299 LE]
        Assert.Equal(new byte[] { 0x01, 0x13, 0x05 }, response);
    }

    [Fact]
    public async Task DownloadInfo_FiltersDeduplicatesAndSorts()
    {
        var state = MakeState([
            new VersionRow(1290, "old.zip", "old.zip", 0),
            new VersionRow(1297, "b.zip", "zz.zip", 0),
            new VersionRow(1298, "c.zip", "aa.zip", 0),
            new VersionRow(1299, "d.zip", "zz.zip", 0), // duplicate compress name
        ]);
        var handler = new LoginPacketHandler(state, new FakeVersionManagerDb());

        // client version 1296 → files with Number > 1296: zz.zip, aa.zip, zz.zip → {aa.zip, zz.zip}
        byte[]? response = await handler.HandleAsync([0x02, 0x10, 0x05]); // 1296 LE

        var expected = new List<byte> { 0x02 };
        AddString2(expected, "ftp.example.com");
        AddString2(expected, "/patch/");
        expected.AddRange(BitConverter.GetBytes((short)2));
        AddString2(expected, "aa.zip");
        AddString2(expected, "zz.zip");

        Assert.Equal(expected.ToArray(), response);
    }

    [Fact]
    public async Task ServerList_RefreshesCountsAndMasksOverLimit()
    {
        var servers = new List<ServerInfo>
        {
            new() { ServerIP = "10.0.0.1"u8.ToArray(), ServerName = "Ares"u8.ToArray(), ServerId = 1, UserLimit = 100 },
            new() { ServerIP = "10.0.0.2"u8.ToArray(), ServerName = "Zeus"u8.ToArray(), ServerId = 2, UserLimit = 50 },
        };
        var db = new FakeVersionManagerDb
        {
            UserCounts =
            [
                new ConcurrentRow(1, 10, 20, 30),   // 60 <= 100 → sent as-is
                new ConcurrentRow(2, 40, 20, 0),    // 60 > 50   → sent as -1
                new ConcurrentRow(9, 1, 1, 1),      // out of range → ignored
            ],
        };
        var handler = new LoginPacketHandler(MakeState(servers: servers), db);

        byte[]? response = await handler.HandleAsync([0xF5]);

        var expected = new List<byte> { 0xF5, 0x02 };
        AddString2(expected, "10.0.0.1");
        AddString2(expected, "Ares");
        expected.AddRange(BitConverter.GetBytes((short)60));
        AddString2(expected, "10.0.0.2");
        AddString2(expected, "Zeus");
        expected.AddRange(BitConverter.GetBytes((short)-1));

        Assert.Equal(expected.ToArray(), response);
    }

    [Fact]
    public async Task Login_Ok_WithPremium()
    {
        var db = new FakeVersionManagerDb();
        db.Accounts["tester"] = ("secret", 1);
        db.PremiumDays["tester"] = 30;
        var handler = new LoginPacketHandler(MakeState(), db);

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", "secret"));

        // [0xF3][AUTH_OK][int16 30]
        Assert.Equal(new byte[] { 0xF3, 0x01, 30, 0 }, response);
    }

    [Fact]
    public async Task Login_Ok_WithoutPremium_SendsMinusOne()
    {
        var db = new FakeVersionManagerDb();
        db.Accounts["tester"] = ("secret", 1);
        var handler = new LoginPacketHandler(MakeState(), db);

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", "secret"));

        Assert.Equal(new byte[] { 0xF3, 0x01, 0xFF, 0xFF }, response);
    }

    [Fact]
    public async Task Login_AlreadyInGame_SendsServerInfo()
    {
        var db = new FakeVersionManagerDb();
        db.Accounts["tester"] = ("secret", 1);
        db.CurrentUsers["tester"] = new CurrentUser("tester", 3, "10.0.0.9");
        var handler = new LoginPacketHandler(MakeState(), db);

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", "secret"));

        var expected = new List<byte> { 0xF3, 0x05 };
        AddString2(expected, "10.0.0.9");
        expected.AddRange(BitConverter.GetBytes((short)3));
        Assert.Equal(expected.ToArray(), response);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsNotFound()
    {
        var db = new FakeVersionManagerDb();
        db.Accounts["tester"] = ("secret", 1);
        var handler = new LoginPacketHandler(MakeState(), db);

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", "wrong"));

        // Deliberately AUTH_NOT_FOUND, not AUTH_INVALID_PW.
        Assert.Equal(new byte[] { 0xF3, 0x02 }, response);
    }

    [Fact]
    public async Task Login_Banned()
    {
        var db = new FakeVersionManagerDb();
        db.Accounts["tester"] = ("secret", 255);
        var handler = new LoginPacketHandler(MakeState(), db);

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", "secret"));

        Assert.Equal(new byte[] { 0xF3, 0x04 }, response);
    }

    [Fact]
    public async Task Login_DbError_ReturnsAuthFailed()
    {
        var db = new FakeVersionManagerDb { FailLogin = true };
        var handler = new LoginPacketHandler(MakeState(), db);

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", "secret"));

        Assert.Equal(new byte[] { 0xF3, 0xFF }, response);
    }

    [Theory]
    [InlineData("")]                          // idlen 0
    [InlineData("123456789012345678901")]     // idlen 21 > 20
    public async Task Login_InvalidIdLength_FailsWithNotFound(string id)
    {
        var handler = new LoginPacketHandler(MakeState(), new FakeVersionManagerDb());

        byte[]? response = await handler.HandleAsync(LoginRequest(id, "pw"));

        Assert.Equal(new byte[] { 0xF3, 0x02 }, response);
    }

    [Fact]
    public async Task Login_TooLongPassword_FailsWithNotFound()
    {
        var handler = new LoginPacketHandler(MakeState(), new FakeVersionManagerDb());

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", "1234567890123")); // 13 > 12

        Assert.Equal(new byte[] { 0xF3, 0x02 }, response);
    }

    [Fact]
    public async Task Login_EmptyPasswordIsAllowedThroughValidation()
    {
        // pwdlen == 0 passes validation (pwdlen < 0 is the failure condition).
        var db = new FakeVersionManagerDb();
        db.Accounts["tester"] = ("", 1);
        var handler = new LoginPacketHandler(MakeState(), db);

        byte[]? response = await handler.HandleAsync(LoginRequest("tester", ""));

        Assert.Equal(new byte[] { 0xF3, 0x01, 0xFF, 0xFF }, response);
    }

    [Fact]
    public async Task Login_TruncatedPacket_FailsGracefully()
    {
        var handler = new LoginPacketHandler(MakeState(), new FakeVersionManagerDb());

        // Announces idlen 10 but carries only 2 bytes.
        byte[]? response = await handler.HandleAsync([0xF3, 0x0A, 0x00, (byte)'a', (byte)'b']);

        Assert.Equal(new byte[] { 0xF3, 0x02 }, response);
    }

    [Fact]
    public async Task News_WithContent_IncludesEmbeddedNuls()
    {
        // title + {'#',0,'\n'} + message + {0,'\n','#',0,'\n',0,'\n'}
        var blob = new List<byte>();
        blob.AddRange("Patch"u8.ToArray());
        blob.AddRange(new byte[] { (byte)'#', 0, (byte)'\n' });
        blob.AddRange("Servers back up"u8.ToArray());
        blob.AddRange(new byte[] { 0, (byte)'\n', (byte)'#', 0, (byte)'\n', 0, (byte)'\n' });

        var handler = new LoginPacketHandler(MakeState(news: blob.ToArray()), new FakeVersionManagerDb());

        byte[]? response = await handler.HandleAsync([0xF6]);

        var expected = new List<byte> { 0xF6 };
        AddString2(expected, "Login Notice");
        expected.AddRange(BitConverter.GetBytes((short)blob.Count));
        expected.AddRange(blob);

        Assert.Equal(expected.ToArray(), response);
    }

    [Fact]
    public async Task News_Empty_SendsEmptyMarker()
    {
        var handler = new LoginPacketHandler(MakeState(), new FakeVersionManagerDb());

        byte[]? response = await handler.HandleAsync([0xF6]);

        var expected = new List<byte> { 0xF6 };
        AddString2(expected, "Login Notice");
        AddString2(expected, "<empty>");

        Assert.Equal(expected.ToArray(), response);
    }

    [Fact]
    public async Task UnknownOpcode_IsIgnored()
    {
        var handler = new LoginPacketHandler(MakeState(), new FakeVersionManagerDb());

        Assert.Null(await handler.HandleAsync([0x7E, 0x01, 0x02]));
    }

    private static void AddString2(List<byte> buffer, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        buffer.AddRange(BitConverter.GetBytes((short)bytes.Length));
        buffer.AddRange(bytes);
    }
}
