using System.Buffers.Binary;
using OpenKO.Core.Compression;
using OpenKO.Core.Crypto;
using OpenKO.Core.Protocol;

namespace OpenKO.Client.Game.Net;

/// <summary>Outcome of one <see cref="GameClientSocketCore.TryReadPacket"/> attempt.</summary>
public enum ClientFrameResult
{
    /// <summary>A packet was extracted.</summary>
    Packet,

    /// <summary>Not enough buffered data yet.</summary>
    NeedMore,

    /// <summary>Protocol violation — the C++ client drops the connection.</summary>
    Close,
}

/// <summary>
/// The client half of the CUser/CAPISocket byte layer (Client/WarFare/APISocket.cpp):
/// the mirror of the server's <c>GameSocketCore</c>. The same instance frames the
/// unencrypted login-server traffic and, after the WIZ_VERSION_CHECK reply calls
/// <see cref="InitCrypt"/>, the encrypted Ebenezer traffic.
///
/// Crypto directions (verified against the server core):
/// - <b>send</b> (client→server): inner = [sendVal u32][payload][crc32 u32], then
///   JvCryption over the whole block (matches the server's <c>DecryptWithCrc32</c>).
/// - <b>recv</b> (server→client): decrypt, then the block is [0xfc][0x1e][3 seq
///   bytes][payload] (matches the server's <c>BuildFrame</c>), payload starts at +5.
/// </summary>
public sealed class GameClientSocketCore
{
    private const int MaxPacketSize = 8192;
    private const int PacketHeaderSize = 6;
    private const int EncryptedHeaderSize = 5; // [0xfc][0x1e] + 3 sequence bytes

    private byte[] _buffer = new byte[MaxPacketSize];
    private int _length;
    private uint _sendValue;
    private uint _recvValue;

    /// <summary>s_JvCrypt — the client keys this from the server's public key.</summary>
    public JvCryption Cryption { get; } = new();

    /// <summary>s_bCryptionFlag — off for the login server, on after the version check.</summary>
    public bool CryptionEnabled { get; private set; }

    /// <summary>CAPISocket::InitCrypt(publicKey) — a zero key leaves cryption off.</summary>
    public void InitCrypt(ulong publicKey)
    {
        Cryption.SetPublicKey(publicKey);
        Cryption.Init();
        _sendValue = 0;
        _recvValue = 0;
        CryptionEnabled = publicKey != 0;
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        if (_length + data.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + data.Length));

        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>CAPISocket::ReceiveProcess. On Packet, <paramref name="packet"/> starts at the opcode.</summary>
    public ClientFrameResult TryReadPacket(out byte[] packet)
    {
        packet = [];

        if (_length < 7)
            return ClientFrameResult.NeedMore;

        if (_buffer[0] != 0xAA && _buffer[1] != 0x55)
            return ClientFrameResult.Close;

        const int startPos = 2;
        int length = BinaryPrimitives.ReadInt16LittleEndian(_buffer.AsSpan(startPos));
        if (length < 0)
            return ClientFrameResult.Close;

        int endPos = startPos + 2 + length;
        if (endPos + 2 > _length)
            return ClientFrameResult.NeedMore;

        if (_buffer[endPos] != 0x55 || _buffer[endPos + 1] != 0xAA)
            return ClientFrameResult.Close;

        if (CryptionEnabled)
        {
            if (length <= EncryptedHeaderSize)
                return ClientFrameResult.Close;

            var decrypted = new byte[length];
            Cryption.Transform(_buffer.AsSpan(startPos + 2, length), decrypted);

            // The server marks its blocks with the 0x1EFC signature (bytes fc 1e).
            if (decrypted[0] != 0xFC || decrypted[1] != 0x1E)
                return ClientFrameResult.Close;

            uint recvValue = (uint)(decrypted[2] | (decrypted[3] << 8) | (decrypted[4] << 16));
            if (recvValue != 0 && _recvValue > recvValue)
                return ClientFrameResult.Close;
            _recvValue = recvValue;

            int payloadLength = length - EncryptedHeaderSize;
            packet = decrypted.AsSpan(EncryptedHeaderSize, payloadLength).ToArray();
        }
        else
        {
            packet = _buffer.AsSpan(startPos + 2, length).ToArray();
        }

        Consume(PacketHeaderSize + length);
        return ClientFrameResult.Packet;
    }

    private void Consume(int count)
    {
        Buffer.BlockCopy(_buffer, count, _buffer, 0, _length - count);
        _length -= count;
    }

    /// <summary>CAPISocket::Send — frames (and, once keyed, encrypts) one payload.</summary>
    public byte[]? BuildFrame(ReadOnlySpan<byte> payload)
    {
        int length = payload.Length;

        if (CryptionEnabled)
        {
            // inner = counter(4) + payload + crc32(4)
            int innerLength = 4 + length + 4;
            if (innerLength + PacketHeaderSize > MaxPacketSize)
                return null;

            _sendValue = (_sendValue + 1) & 0x00FFFFFF;

            var inner = new byte[innerLength];
            BinaryPrimitives.WriteUInt32LittleEndian(inner, _sendValue);
            payload.CopyTo(inner.AsSpan(4));
            uint crc = KoCrc32.Compute(inner.AsSpan(0, 4 + length), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(inner.AsSpan(4 + length), crc);

            Cryption.Transform(inner, inner);

            var frame = new byte[PacketHeaderSize + innerLength];
            frame[0] = 0xAA;
            frame[1] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)innerLength);
            inner.CopyTo(frame.AsSpan(4));
            frame[4 + innerLength] = 0x55;
            frame[5 + innerLength] = 0xAA;
            return frame;
        }
        else
        {
            if (length + PacketHeaderSize > MaxPacketSize)
                return null;

            var frame = new byte[PacketHeaderSize + length];
            frame[0] = 0xAA;
            frame[1] = 0x55;
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(2), (short)length);
            payload.CopyTo(frame.AsSpan(4));
            frame[4 + length] = 0x55;
            frame[5 + length] = 0xAA;
            return frame;
        }
    }

    /// <summary>
    /// CGameProcedure::MsgRecv_CompressedPacket: a WIZ_COMPRESS_PACKET wraps
    /// [u16 compressed][u16 original][u32 crc][lzf bytes]; on match the decompressed
    /// inner packet is returned for re-dispatch. Returns false for any other opcode.
    /// </summary>
    public static bool TryDecompress(ReadOnlySpan<byte> packet, out byte[] inner)
    {
        inner = [];
        if (packet.Length < 9 || packet[0] != (byte)GameOpcode.WIZ_COMPRESS_PACKET)
            return false;

        int compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(packet[1..]);
        int originalLength = BinaryPrimitives.ReadUInt16LittleEndian(packet[3..]);
        // packet[5..9] is the original CRC32 (only checked in the C++ debug build).
        if (packet.Length < 9 + compressedLength)
            return false;

        var buffer = new byte[originalLength];
        int written = Lzf.Decompress(packet.Slice(9, compressedLength), buffer);
        if (written != originalLength)
            return false;

        inner = buffer;
        return true;
    }
}
