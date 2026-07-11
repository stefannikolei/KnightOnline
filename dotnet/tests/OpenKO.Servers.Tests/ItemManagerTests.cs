using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Network;
using OpenKO.Servers.ItemManager;
using OpenKO.Servers.ItemManager.Transport;
using OpenKO.TestClient;
using Xunit;

namespace OpenKO.Servers.Tests;

public class ItemManagerTests
{
    private static byte[] ItemLogBody(string src, string tar, byte type, long serial, uint item, short count, short dure)
    {
        var buffer = new byte[256];
        var writer = new PacketWriter(buffer);
        writer.SetString2(Encoding.ASCII.GetBytes(src));
        writer.SetString2(Encoding.ASCII.GetBytes(tar));
        writer.SetByte(type);
        writer.SetInt64(serial);
        writer.SetDWord(item);
        writer.SetShort(count);
        writer.SetShort(dure);
        return writer.Written.ToArray();
    }

    private static byte[] ExpLogBody(string account, string charId, byte type, byte level, uint exp, uint loyalty, uint money)
    {
        var buffer = new byte[256];
        var writer = new PacketWriter(buffer);
        writer.SetString2(Encoding.ASCII.GetBytes(account));
        writer.SetString2(Encoding.ASCII.GetBytes(charId));
        writer.SetByte(type);
        writer.SetByte(level);
        writer.SetDWord(exp);
        writer.SetDWord(loyalty);
        writer.SetDWord(money);
        return writer.Written.ToArray();
    }

    [Fact]
    public void ParseItemLog_ProducesCppLogLine()
    {
        byte[] body = ItemLogBody("SellerChar", "BuyerChar", 3, 123456789012345L, 810123001, 5, 6000);

        string? line = ItemLogParser.ParseItemLog(body);

        // spdlog format: "{}, {}, {}, {}, {}, {}, {}"
        Assert.Equal("SellerChar, BuyerChar, 3, 123456789012345, 810123001, 5, 6000", line);
    }

    [Fact]
    public void ParseExpLog_ProducesCppLogLine()
    {
        byte[] body = ExpLogBody("account01", "KnightChar", 1, 60, 123456u, 5000u, 987654321u);

        string? line = ItemLogParser.ParseExpLog(body);

        Assert.Equal("account01, KnightChar, 1, 60, 123456, 5000, 987654321", line);
    }

    [Theory]
    [InlineData(0)]   // srclen 0
    [InlineData(21)]  // srclen > MAX_ID_SIZE
    public void ParseItemLog_RejectsInvalidNameLength(short srcLen)
    {
        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetShort(srcLen);
        writer.SetString(new byte[Math.Max((int)srcLen, 0) is var l && l <= 30 ? l : 30]);

        Assert.Null(ItemLogParser.ParseItemLog(writer.Written));
    }

    [Fact]
    public void ParseItemLog_RejectsTruncatedBody()
    {
        byte[] full = ItemLogBody("a", "b", 1, 2, 3, 4, 5);
        Assert.Null(ItemLogParser.ParseItemLog(full.AsSpan(0, full.Length - 4)));
    }

    [Fact]
    public async Task TcpTransport_DeliversQueueMessages()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var source = new TcpItemLogSource(
            new IPEndPoint(IPAddress.Loopback, 0), NullLogger.Instance);
        source.Start();
        _ = source.RunAsync(cts.Token);

        // message = [opcode][body], framed like every other KO packet
        byte[] message = [0x19, .. ItemLogBody("src", "tar", 1, 42L, 100u, 1, 2)];

        using var client = new KoTestClient();
        await client.ConnectAsync(source.LocalEndPoint!, cts.Token);
        await client.SendPayloadAsync(message, cts.Token);

        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(message, enumerator.Current);
    }

    [Fact]
    public void DailyFileLogger_WritesSpdlogStyleLine()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string basePath = Path.Combine(dir, "ItemLog.txt");

        using (var logger = new DailyFileLogger(basePath, "ItemManagerItem"))
        {
            logger.Info("a, b, 1, 2, 3, 4, 5");
        }

        string file = Directory.GetFiles(dir).Single();
        Assert.Matches(@"ItemLog_\d{4}-\d{2}-\d{2}\.txt$", file);

        string line = File.ReadAllLines(file).Single();
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[ItemManagerItem\] \[info\] a, b, 1, 2, 3, 4, 5$", line);
    }
}
