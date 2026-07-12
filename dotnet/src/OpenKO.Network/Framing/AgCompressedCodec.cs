using OpenKO.Core.Compression;
using OpenKO.Core.Crypto;

namespace OpenKO.Network.Framing;

/// <summary>
/// Codec for the AG_COMPRESSED_DATA payload exchanged between Ebenezer and the
/// AIServer (see <c>CGameSocket::RecvCompressedData</c>):
/// <c>[int16 compLen][int16 origLen][uint32 crc32][int16 count][compressed bytes]</c>.
/// The CRC is the KO crc32 variant with start value 0 over the *decompressed* data.
/// The inner payload is itself a regular <c>[opcode][body]</c> packet.
/// </summary>
public static class AgCompressedCodec
{
    /// <summary>Decodes and validates; returns null on any inconsistency (like the C++ silently dropping).</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        if (body.Length < 10)
            return null;

        int compLen = reader.GetShort();
        int origLen = reader.GetShort();
        uint crc = reader.GetDWord();
        reader.GetShort(); // packet count, unused by the C++

        if (compLen < 0 || origLen < 0 || reader.Remaining < compLen)
            return null;

        var decompressed = new byte[origLen];
        int actualLen = Lzf.Decompress(reader.GetString(compLen), decompressed);
        if (actualLen != origLen)
            return null;

        if (KoCrc32.Compute(decompressed) != crc)
            return null;

        return decompressed;
    }

    /// <summary>Builds the AG_COMPRESSED_DATA body for a payload (sender side).</summary>
    public static byte[]? Encode(ReadOnlySpan<byte> payload)
    {
        var compressed = new byte[payload.Length * 2 + 64];
        int compLen = Lzf.Compress(payload, compressed);
        if (compLen == 0)
            return null;

        var body = new byte[10 + compLen];
        var writer = new PacketWriter(body);
        writer.SetShort(compLen);
        writer.SetShort(payload.Length);
        writer.SetDWord(KoCrc32.Compute(payload));
        writer.SetShort(1);
        writer.SetString(compressed.AsSpan(0, compLen));
        return body;
    }
}
