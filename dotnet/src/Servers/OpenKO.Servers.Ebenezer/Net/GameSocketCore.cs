using System.Buffers.Binary;
using OpenKO.Core.Crypto;

namespace OpenKO.Servers.Ebenezer.Net;

/// <summary>Outcome of one <see cref="GameSocketCore.TryReadPacket"/> attempt.</summary>
public enum FrameResult
{
    /// <summary>A packet was extracted.</summary>
    Packet,

    /// <summary>Not enough buffered data yet — wait for more.</summary>
    NeedMore,

    /// <summary>Protocol violation — the C++ closes the socket.</summary>
    Close,
}

/// <summary>
/// The CUser socket byte layer (Server/Ebenezer/User.cpp): <c>PullOutCore</c> and
/// <c>Send</c>, including the WIZ_CRYPTION mode. Unlike the login servers'
/// tolerant framer, Ebenezer CLOSES on a bad header or trailer instead of
/// scanning/resyncing. Encrypted frames carry [0xfc][0x1e][3-byte sequence]
/// before the payload; inbound ones end in a CRC32 the cipher validates.
/// </summary>
public sealed class GameSocketCore
{
    private const int MaxPacketSize = 8192;         // MAX_PACKET_SIZE
    private const int PacketHeaderSize = 6;
    private const int EncryptedPacketHeaderSize = 5;

    private byte[] _buffer = new byte[MaxPacketSize];
    private int _length;

    private uint _sendValue;
    private uint _recvValue;

    /// <summary>_jvCryption — the server generates its key pair on session start.</summary>
    public JvCryption Cryption { get; } = new();

    /// <summary>_jvCryptionEnabled: switched on right after the WIZ_VERSION_CHECK reply.</summary>
    public bool CryptionEnabled { get; set; }

    public GameSocketCore()
    {
        Cryption.GenerateKey();
    }

    /// <summary>Appends received bytes to the receive buffer (the circular buffer in the C++).</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        if (_length + data.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + data.Length));

        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>
    /// CUser::PullOutCore. On <see cref="FrameResult.Packet"/>, <paramref name="packet"/>
    /// holds the decrypted (when enabled) game payload starting at the opcode.
    /// </summary>
    public FrameResult TryReadPacket(out byte[] packet)
    {
        packet = [];

        // We expect at least 7 bytes (header, length, data [at least 1 byte], tail).
        if (_length < 7)
            return FrameResult.NeedMore;

        // Quirk kept: the C++ uses '&&', so only both start bytes being wrong closes.
        if (_buffer[0] != 0xAA && _buffer[1] != 0x55)
            return FrameResult.Close;

        const int startPos = 2;
        int length = BinaryPrimitives.ReadInt16LittleEndian(_buffer.AsSpan(startPos));
        int originalLength = length;

        if (length < 0)
            return FrameResult.Close;

        if (length > _length)
            return FrameResult.NeedMore;

        int endPos = startPos + 2 + length;
        if (endPos + 2 > _length)
            return FrameResult.NeedMore;

        if (_buffer[endPos] != 0x55 || _buffer[endPos + 1] != 0xAA)
            return FrameResult.Close;

        if (CryptionEnabled)
        {
            // Encrypted packets carry a CRC (4) + sequence number (4) and at
            // least one data byte.
            if (length <= 8)
                return FrameResult.Close;

            var decrypted = new byte[length];
            int decryptedLength = Cryption.DecryptWithCrc32(_buffer.AsSpan(startPos + 2, length), decrypted);
            if (decryptedLength < 0)
                return FrameResult.Close;

            uint recvValue = BinaryPrimitives.ReadUInt32LittleEndian(decrypted);

            // The sequence must not go backwards (0 resets after a wrap).
            if (recvValue != 0 && _recvValue > recvValue)
                return FrameResult.Close;

            _recvValue = recvValue;

            int payloadLength = decryptedLength - 4;
            if (payloadLength <= 0)
                return FrameResult.Close;

            packet = decrypted.AsSpan(4, payloadLength).ToArray();
        }
        else
        {
            packet = _buffer.AsSpan(startPos + 2, length).ToArray();
        }

        Consume(6 + originalLength);
        return FrameResult.Packet;
    }

    private void Consume(int count)
    {
        Buffer.BlockCopy(_buffer, count, _buffer, 0, _length - count);
        _length -= count;
    }

    /// <summary>
    /// CUser::Send — frames (and in cryption mode encrypts) one game payload.
    /// Returns null when the length is out of bounds (the C++ returns -1).
    /// </summary>
    public byte[]? BuildFrame(ReadOnlySpan<byte> payload)
    {
        int length = payload.Length;

        if (CryptionEnabled)
        {
            if (length + PacketHeaderSize + EncryptedPacketHeaderSize > MaxPacketSize)
                return null;

            ushort encryptedLength = (ushort)(length + EncryptedPacketHeaderSize);

            _sendValue++;
            _sendValue &= 0x00ffffff;

            var frame = new byte[PacketHeaderSize + encryptedLength];
            frame[0] = 0xAA;
            frame[1] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), encryptedLength);

            // [0xfc][0x1e] marker + 3 sequence bytes + payload, encrypted in place.
            Span<byte> body = frame.AsSpan(4, encryptedLength);
            body[0] = 0xfc;
            body[1] = 0x1e;
            Span<byte> seq = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(seq, _sendValue);
            seq[..3].CopyTo(body[2..]);
            payload.CopyTo(body[5..]);

            Cryption.Transform(body, body);

            frame[4 + encryptedLength] = 0x55;
            frame[5 + encryptedLength] = 0xAA;
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
}
