using System.Buffers.Binary;
using OpenKO.Common;

namespace OpenKO.Net;

/// <summary>
/// KO packet framing (port of the framing logic in CAPISocket::Send / ReceiveProcess).
///
/// Wire frame: <c>[0xAA 0x55] [len:uint16 LE] [payload:len bytes] [0x55 0xAA]</c>
/// i.e. header and tail are the network-byte-order encodings of 0xAA55 / 0x55AA, while the length
/// field is stored in host (little-endian) order, exactly as the original client did.
///
/// When encryption is enabled the payload is wrapped/transformed by <see cref="JvCryption"/>:
///  - outgoing: <c>[sendCounter:uint32][payload][crc32:uint32]</c> then encrypted as a whole;
///  - incoming: decrypted, then <c>[sig:uint16=0x1EFC][sequence:uint16][reserved:byte][payload…]</c>.
/// </summary>
public static class PacketFraming
{
    public const ushort PacketHeader = 0xAA55;
    public const ushort PacketTail = 0x55AA;
    public const ushort CryptSignature = 0x1EFC;
    public const int ReceiveBufferSize = 262144;

    /// <summary>Builds a complete wire frame for <paramref name="payload"/>.</summary>
    public static byte[] BuildFrame(ReadOnlySpan<byte> payload, JvCryption? crypto, ref uint sendCounter)
    {
        byte[] body;
        if (crypto != null)
        {
            // [counter][payload][crc32], then encrypt the whole thing.
            int innerLen = payload.Length + 8;
            var plain = new byte[innerLen];
            BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(0, 4), ++sendCounter);
            payload.CopyTo(plain.AsSpan(4));
            uint crc = Crc32.Compute(plain.AsSpan(0, payload.Length + 4));
            BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(payload.Length + 4, 4), crc);

            body = new byte[innerLen];
            crypto.Encrypt(innerLen, plain, body);
        }
        else
        {
            body = payload.ToArray();
        }

        var frame = new byte[body.Length + 6];
        // header: htons(0xAA55) => bytes {0xAA, 0x55}
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), PacketHeader);
        // length: raw little-endian
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2, 2), (ushort)body.Length);
        body.CopyTo(frame.AsSpan(4));
        // tail: htons(0x55AA) => bytes {0x55, 0xAA}
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(body.Length + 4, 2), PacketTail);
        return frame;
    }

    /// <summary>
    /// Attempts to parse a single frame from the front of <paramref name="buffer"/>.
    /// On success returns the decoded application payload and the number of raw bytes consumed.
    /// </summary>
    public static bool TryParseFrame(
        ReadOnlySpan<byte> buffer,
        JvCryption? crypto,
        out byte[] payload,
        out int consumed)
    {
        payload = Array.Empty<byte>();
        consumed = 0;

        if (buffer.Length < 7)
            return false;

        if (BinaryPrimitives.ReadUInt16BigEndian(buffer[..2]) != PacketHeader)
        {
            // Broken header — caller decides how to resynchronise.
            return false;
        }

        ushort bodyLen = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2));
        int total = bodyLen + 6;
        if (buffer.Length < total)
            return false; // wait for more data

        if (BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(total - 2, 2)) != PacketTail)
            return false;

        ReadOnlySpan<byte> body = buffer.Slice(4, bodyLen);

        if (crypto != null)
        {
            var decrypted = new byte[bodyLen];
            crypto.Decrypt(bodyLen, body, decrypted);

            ushort sig = BinaryPrimitives.ReadUInt16LittleEndian(decrypted.AsSpan(0, 2));
            if (sig != CryptSignature)
                return false;

            // [sig:2][sequence:2][reserved:1][payload…]
            payload = decrypted.AsSpan(5, bodyLen - 5).ToArray();
        }
        else
        {
            payload = body.ToArray();
        }

        consumed = total;
        return true;
    }
}
