using System.Text;
using OpenKO.Client.Game.Net;
using OpenKO.Core.Compression;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using OpenKO.Servers.Ebenezer.Net;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-7.1 pins: the client network codecs cross-checked against the real
/// server <see cref="GameSocketCore"/> (proving wire compatibility), plus the
/// login/char-select packet layouts.
/// </summary>
public class ClientNetworkTests
{
    private static byte[] FeedAndRead(GameSocketCore server, byte[] frame)
    {
        server.Feed(frame);
        FrameResult result = server.TryReadPacket(out byte[] packet);
        Assert.Equal(FrameResult.Packet, result);
        return packet;
    }

    private static byte[] FeedAndRead(GameClientSocketCore client, byte[] frame)
    {
        client.Feed(frame);
        ClientFrameResult result = client.TryReadPacket(out byte[] packet);
        Assert.Equal(ClientFrameResult.Packet, result);
        return packet;
    }

    [Fact]
    public void UnencryptedFrame_RoundTripsBetweenClientAndServerCores()
    {
        var client = new GameClientSocketCore();
        var server = new GameSocketCore();
        byte[] payload = [0xF5, 1, 2, 3, 4];

        // client -> server
        byte[]? clientFrame = client.BuildFrame(payload);
        Assert.NotNull(clientFrame);
        Assert.Equal(payload, FeedAndRead(server, clientFrame!));

        // server -> client
        byte[]? serverFrame = server.BuildFrame(payload);
        Assert.NotNull(serverFrame);
        Assert.Equal(payload, FeedAndRead(client, serverFrame!));
    }

    [Fact]
    public void EncryptedFrame_RoundTripsBothDirections()
    {
        var server = new GameSocketCore();          // generates its key pair
        server.CryptionEnabled = true;
        var client = new GameClientSocketCore();
        client.InitCrypt(server.Cryption.PublicKey); // the WIZ_VERSION_CHECK handshake

        byte[] payload = [(byte)GameOpcode.WIZ_LOGIN, 0x10, 0x20, 0x30];

        // client -> server (client encrypts with counter+crc, server validates crc)
        byte[]? up = client.BuildFrame(payload);
        Assert.NotNull(up);
        Assert.Equal(payload, FeedAndRead(server, up!));

        // server -> client (server writes the 0x1EFC-tagged block, client strips it)
        byte[]? down = server.BuildFrame(payload);
        Assert.NotNull(down);
        Assert.Equal(payload, FeedAndRead(client, down!));
    }

    [Fact]
    public void EncryptedFrame_WrongKeyIsRejected()
    {
        var server = new GameSocketCore();
        server.CryptionEnabled = true;
        var client = new GameClientSocketCore();
        client.InitCrypt(server.Cryption.PublicKey ^ 0xDEADBEEF); // mismatched key

        byte[]? down = server.BuildFrame([(byte)GameOpcode.WIZ_LOGIN, 1]);
        client.Feed(down!);
        // The signature check fails on a bad key → Close.
        Assert.Equal(ClientFrameResult.Close, client.TryReadPacket(out _));
    }

    [Fact]
    public void CompressedPacket_Unwraps()
    {
        // Build a WIZ_COMPRESS_PACKET around an inner payload.
        byte[] inner = Enumerable.Range(0, 200).Select(i => (byte)(i % 7)).ToArray();
        var compressed = new byte[inner.Length * 2];
        int clen = Lzf.Compress(inner, compressed);
        Assert.True(clen > 0);

        var buffer = new byte[9 + clen];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_COMPRESS_PACKET);
        w.SetShort(clen);
        w.SetShort(inner.Length);
        w.SetDWord(0); // crc (debug-only on the client)
        w.SetString(compressed.AsSpan(0, clen));

        Assert.True(GameClientSocketCore.TryDecompress(w.Written, out byte[] result));
        Assert.Equal(inner, result);
    }

    [Fact]
    public void LoginProtocol_ServerListRoundTrips()
    {
        // Build a server-style LS_SERVERLIST reply and parse it.
        var buffer = new byte[128];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)LoginOpcode.LS_SERVERLIST);
        w.SetByte(2);
        w.SetString2(Encoding.Latin1.GetBytes("127.0.0.1"));
        w.SetString2(Encoding.Latin1.GetBytes("Ronark"));
        w.SetShort(42);
        w.SetString2(Encoding.Latin1.GetBytes("10.0.0.9"));
        w.SetString2(Encoding.Latin1.GetBytes("Arena"));
        w.SetShort(7);

        IReadOnlyList<ServerListEntry> servers = LoginProtocol.ParseServerList(w.Written);
        Assert.Equal(2, servers.Count);
        Assert.Equal("127.0.0.1", servers[0].Ip);
        Assert.Equal("Ronark", servers[0].Name);
        Assert.Equal(42, servers[0].ConcurrentUsers);
        Assert.Equal("Arena", servers[1].Name);
    }

    [Fact]
    public void LoginProtocol_AccountLoginRequestLayout()
    {
        byte[] payload = LoginProtocol.BuildAccountLogin("acct", "pw");
        var r = new PacketReader(payload);
        Assert.Equal((byte)LoginOpcode.LS_LOGIN_REQ, r.GetByte());
        Assert.Equal("acct", Encoding.Latin1.GetString(r.GetVarString(2)));
        Assert.Equal("pw", Encoding.Latin1.GetString(r.GetVarString(2)));
    }

    [Fact]
    public void GameProtocol_VersionCheckReplyParses()
    {
        var buffer = new byte[16];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_VERSION_CHECK);
        w.SetShort(1298);
        w.SetInt64(unchecked((long)0x1122334455667788UL));

        VersionCheckResult result = GameProtocol.ParseVersionCheck(w.Written);
        Assert.Equal((short)1298, result.Version);
        Assert.Equal(0x1122334455667788UL, result.PublicKey);
    }

    [Fact]
    public void GameProtocol_AllCharInfoRoundTripsThreeSlots()
    {
        var buffer = new byte[1024];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ALLCHAR_INFO_REQ);
        w.SetByte(0x01);
        WriteSlot(ref w, "Hero", occupied: true);
        WriteSlot(ref w, "", occupied: false);
        WriteSlot(ref w, "Alt", occupied: true);

        AllCharInfoResult result = GameProtocol.ParseAllCharInfo(w.Written);
        Assert.Equal(1, result.Result);
        Assert.Equal(3, result.Slots.Count);
        Assert.Equal("Hero", result.Slots[0].CharId);
        Assert.False(result.Slots[0].IsEmpty);
        Assert.True(result.Slots[1].IsEmpty);
        Assert.Equal(GameProtocol.VisibleEquipment, result.Slots[0].Equipment.Count);
        Assert.Equal(100u, result.Slots[0].Equipment[0].ItemId);

        static void WriteSlot(ref PacketWriter w, string id, bool occupied)
        {
            w.SetString2(Encoding.Latin1.GetBytes(id));
            w.SetByte(1);        // race
            w.SetShort(101);     // class
            w.SetByte(60);       // level
            w.SetByte(0);        // face
            w.SetByte(1);        // hair
            w.SetByte(21);       // zone
            for (int i = 0; i < GameProtocol.VisibleEquipment; i++)
            {
                w.SetDWord(occupied ? (uint)(100 + i) : 0u);
                w.SetShort(occupied ? (short)15000 : (short)0);
            }
        }
    }

    [Fact]
    public void GameProtocol_SelectCharacterRequestLayout()
    {
        byte[] payload = GameProtocol.BuildSelectCharacter("acct", "Hero", zoneInit: 0x01, zoneCur: 21);
        var r = new PacketReader(payload);
        Assert.Equal((byte)GameOpcode.WIZ_SEL_CHAR, r.GetByte());
        Assert.Equal("acct", Encoding.Latin1.GetString(r.GetVarString(2)));
        Assert.Equal("Hero", Encoding.Latin1.GetString(r.GetVarString(2)));
        Assert.Equal(0x01, r.GetByte());
        Assert.Equal(21, r.GetByte());
    }

    [Fact]
    public void GameProtocol_SelectCharacterReplyParsesSpawn()
    {
        var buffer = new byte[16];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_SEL_CHAR);
        w.SetByte(1);         // success
        w.SetByte(21);        // zone
        w.SetShort(6500);     // x*10
        w.SetShort(5300);     // z*10
        w.SetShort(120);      // y*10
        w.SetByte(2);         // victory nation

        SelectCharResult result = GameProtocol.ParseSelectCharacter(w.Written);
        Assert.True(result.Success);
        Assert.Equal(21, result.Zone);
        Assert.Equal(6500, result.X);
        Assert.Equal(5300, result.Z);
        Assert.Equal((short)120, result.Y);
        Assert.Equal(2, result.VictoryNation);
    }
}
