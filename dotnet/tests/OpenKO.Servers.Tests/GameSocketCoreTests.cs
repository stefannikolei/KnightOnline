using System.Buffers.Binary;
using OpenKO.Core.Crypto;
using OpenKO.Servers.Ebenezer.Net;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the Ebenezer CUser socket layer (GameSocketCore): plain and
/// encrypted framing, the close-on-violation semantics (no resync, unlike the
/// login servers) and the sequence-number protection.
/// </summary>
public class GameSocketCoreTests
{
    /// <summary>Builds a client→server encrypted frame ([seq][payload][crc], ciphered).</summary>
    private static byte[] BuildClientFrame(JvCryption clientCryption, uint sequence, byte[] payload)
    {
        var body = new byte[4 + payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(body, sequence);
        payload.CopyTo(body, 4);
        uint crc = KoCrc32.Compute(body.AsSpan(0, 4 + payload.Length), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4 + payload.Length), crc);

        clientCryption.Transform(body, body);

        var frame = new byte[6 + body.Length];
        frame[0] = 0xAA;
        frame[1] = 0x55;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(2), (short)body.Length);
        body.CopyTo(frame, 4);
        frame[^2] = 0x55;
        frame[^1] = 0xAA;
        return frame;
    }

    private static JvCryption MakeClientCryption(GameSocketCore server)
    {
        var client = new JvCryption();
        client.SetPublicKey(server.Cryption.PublicKey);
        client.Init();
        return client;
    }

    [Fact]
    public void PlainRoundtrip()
    {
        var core = new GameSocketCore();
        byte[] payload = [0x2B, 1, 2, 3];

        byte[]? frame = core.BuildFrame(payload);
        Assert.NotNull(frame);
        Assert.Equal(0xAA, frame![0]);
        Assert.Equal(payload.Length, BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2)));

        core.Feed(frame);
        Assert.Equal(FrameResult.Packet, core.TryReadPacket(out byte[] packet));
        Assert.Equal(payload, packet);
        Assert.Equal(FrameResult.NeedMore, core.TryReadPacket(out _));
    }

    [Fact]
    public void PlainSplitAcrossFeeds()
    {
        var core = new GameSocketCore();
        byte[] frame = core.BuildFrame([0x10, 0xAB])!;

        core.Feed(frame.AsSpan(0, 3));
        Assert.Equal(FrameResult.NeedMore, core.TryReadPacket(out _));

        core.Feed(frame.AsSpan(3));
        Assert.Equal(FrameResult.Packet, core.TryReadPacket(out byte[] packet));
        Assert.Equal(new byte[] { 0x10, 0xAB }, packet);
    }

    [Fact]
    public void EncryptedClientToServerRoundtrip()
    {
        var server = new GameSocketCore { CryptionEnabled = true };
        JvCryption client = MakeClientCryption(server);

        byte[] payload = [0x01, 0x05, 0x00, (byte)'a'];
        server.Feed(BuildClientFrame(client, sequence: 1, payload));

        Assert.Equal(FrameResult.Packet, server.TryReadPacket(out byte[] packet));
        Assert.Equal(payload, packet);
    }

    [Fact]
    public void EncryptedSequenceMustNotGoBackwards()
    {
        var server = new GameSocketCore { CryptionEnabled = true };
        JvCryption client = MakeClientCryption(server);

        server.Feed(BuildClientFrame(client, sequence: 5, [0x01]));
        Assert.Equal(FrameResult.Packet, server.TryReadPacket(out _));

        server.Feed(BuildClientFrame(client, sequence: 4, [0x01]));
        Assert.Equal(FrameResult.Close, server.TryReadPacket(out _));
    }

    [Fact]
    public void EncryptedSequenceZeroResets()
    {
        var server = new GameSocketCore { CryptionEnabled = true };
        JvCryption client = MakeClientCryption(server);

        server.Feed(BuildClientFrame(client, sequence: 9, [0x01]));
        Assert.Equal(FrameResult.Packet, server.TryReadPacket(out _));

        // A wrap back to 0 is explicitly allowed by the C++.
        server.Feed(BuildClientFrame(client, sequence: 0, [0x02]));
        Assert.Equal(FrameResult.Packet, server.TryReadPacket(out byte[] packet));
        Assert.Equal(new byte[] { 0x02 }, packet);
    }

    [Fact]
    public void CorruptCipherTextCloses()
    {
        var server = new GameSocketCore { CryptionEnabled = true };
        JvCryption client = MakeClientCryption(server);

        byte[] frame = BuildClientFrame(client, sequence: 1, [0x01, 0x02]);
        frame[5] ^= 0xFF; // corrupt one ciphered byte → CRC mismatch
        server.Feed(frame);

        Assert.Equal(FrameResult.Close, server.TryReadPacket(out _));
    }

    [Fact]
    public void BadTrailerClosesInsteadOfResyncing()
    {
        var core = new GameSocketCore();
        byte[] frame = core.BuildFrame([0x11, 0x22])!;
        frame[^1] = 0x00;

        core.Feed(frame);
        Assert.Equal(FrameResult.Close, core.TryReadPacket(out _));
    }

    [Fact]
    public void HeaderCheckUsesTheLenientAndQuirk()
    {
        // The C++ condition is (b0 != 0xAA && b1 != 0x55): a frame whose first
        // byte is wrong but whose second byte is 0x55 passes the header check.
        var core = new GameSocketCore();
        byte[] frame = core.BuildFrame([0x33])!;
        frame[0] = 0x00; // b1 stays 0x55 → not closed

        core.Feed(frame);
        Assert.Equal(FrameResult.Packet, core.TryReadPacket(out byte[] packet));
        Assert.Equal(new byte[] { 0x33 }, packet);

        var core2 = new GameSocketCore();
        byte[] frame2 = core2.BuildFrame([0x33])!;
        frame2[0] = 0x00;
        frame2[1] = 0x00; // both wrong → close
        core2.Feed(frame2);
        Assert.Equal(FrameResult.Close, core2.TryReadPacket(out _));
    }

    [Fact]
    public void ServerToClientEncryptedFrameDecodes()
    {
        var server = new GameSocketCore { CryptionEnabled = true };
        JvCryption client = MakeClientCryption(server);

        byte[] payload = [0x2B, 0x12, 0x05];
        byte[] frame = server.BuildFrame(payload)!;

        // [AA 55][len = payload+5][cipher([fc][1e][seq3][payload])][55 AA]
        int len = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2));
        Assert.Equal(payload.Length + 5, len);

        var body = frame.AsSpan(4, len).ToArray();
        client.Transform(body, body);

        Assert.Equal(0xFC, body[0]);
        Assert.Equal(0x1E, body[1]);
        Assert.Equal(1u, (uint)(body[2] | body[3] << 8 | body[4] << 16)); // first send → sequence 1
        Assert.Equal(payload, body[5..]);
        Assert.Equal(0x55, frame[4 + len]);
        Assert.Equal(0xAA, frame[5 + len]);
    }

    [Fact]
    public void OversizedPayloadIsRejected()
    {
        var core = new GameSocketCore();
        Assert.Null(core.BuildFrame(new byte[8192 - 5])); // 6-byte header no longer fits
        Assert.NotNull(core.BuildFrame(new byte[8192 - 6]));

        core.CryptionEnabled = true;
        Assert.Null(core.BuildFrame(new byte[8192 - 10])); // 11 bytes of overhead now
        Assert.NotNull(core.BuildFrame(new byte[8192 - 11]));
    }
}
