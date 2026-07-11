using System.Buffers.Binary;
using OpenKO.Core.Protocol;

namespace OpenKO.Network.Framing;

/// <summary>
/// Incremental decoder for the KO wire frame
/// <c>[0xAA 0x55][int16 LE payload length][payload][0x55 0xAA]</c>,
/// a faithful port of <c>CUser::PullOutCore</c> (Server/VersionManager/User.cpp and
/// the equivalents in Ebenezer/AIServer). The C++ quirks are load-bearing under
/// fragmentation and must be preserved:
/// <list type="bullet">
/// <item>garbage before the start marker is tolerated (scan for 0xAA 0x55),</item>
/// <item>the length is read as a *signed* int16,</item>
/// <item>a negative length, a length exceeding the buffered byte count, or an
///   incomplete frame consume nothing (wait for more data),</item>
/// <item>a wrong end marker advances the buffer head by exactly 3 bytes (resync),</item>
/// <item>a completed frame consumes <c>6 + length</c> bytes from the head —
///   regardless of any garbage offset the header was found at.</item>
/// </list>
/// </summary>
public sealed class PacketFramer
{
    private byte[] _buffer = new byte[ProtocolConstants.SocketBuffSize];
    private int _head;
    private int _tail;

    private int Count => _tail - _head;

    public void Feed(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        if (_tail + data.Length > _buffer.Length)
        {
            // Compact, then grow if still needed.
            Buffer.BlockCopy(_buffer, _head, _buffer, 0, Count);
            _tail -= _head;
            _head = 0;

            if (_tail + data.Length > _buffer.Length)
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _tail + data.Length));
        }

        data.CopyTo(_buffer.AsSpan(_tail));
        _tail += data.Length;
    }

    /// <summary>
    /// Port of PullOutCore: extracts at most one payload per call.
    /// Returns false when no complete frame is available (callers loop until false).
    /// </summary>
    public bool TryReadFrame(out byte[] payload)
    {
        payload = [];

        int len = Count;
        if (len <= 0)
            return false;

        ReadOnlySpan<byte> buf = _buffer.AsSpan(_head, len);

        for (int i = 0; i < len; i++)
        {
            if (i + 2 >= len)
                break;

            if (buf[i] != ProtocolConstants.PacketStart1 || buf[i + 1] != ProtocolConstants.PacketStart2)
                continue;

            int sPos = i + 2;

            // The C++ reads both length bytes unchecked; sPos+1 can only exceed the
            // buffer in a torn read, which we treat as "wait for more data".
            if (sPos + 2 > len)
                return false;

            int length = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(sPos, 2));

            if (length < 0)
                return false;

            if (length > len)
                return false;

            int ePos = sPos + length + 2;

            if (ePos + 2 > len)
                return false;

            if (buf[ePos] == ProtocolConstants.PacketEnd1 && buf[ePos + 1] == ProtocolConstants.PacketEnd2)
            {
                payload = buf.Slice(sPos + 2, length).ToArray();

                // 6: header 2 + length 2 + end 2 — consumed from the head, exactly
                // like the C++ (leading garbage shifts the consumption window).
                _head += 6 + length;
                if (_head >= _tail)
                    _head = _tail = 0;

                return true;
            }

            // Bad trailer: resync by advancing the head 3 bytes.
            _head += 3;
            if (_head >= _tail)
                _head = _tail = 0;

            return false;
        }

        return false;
    }

    /// <summary>
    /// Port of <c>CUser::Send</c>: wraps a payload in the wire frame.
    /// Returns false when <c>payload + 6 &gt; MAX_PACKET_SIZE</c> (the C++ returns -1).
    /// </summary>
    public static bool TryFrame(ReadOnlySpan<byte> payload, out byte[] frame)
    {
        frame = [];

        if (payload.Length + ProtocolConstants.FrameOverhead > ProtocolConstants.MaxPacketSize)
            return false;

        frame = new byte[payload.Length + ProtocolConstants.FrameOverhead];
        frame[0] = ProtocolConstants.PacketStart1;
        frame[1] = ProtocolConstants.PacketStart2;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(2), (short)payload.Length);
        payload.CopyTo(frame.AsSpan(4));
        frame[^2] = ProtocolConstants.PacketEnd1;
        frame[^1] = ProtocolConstants.PacketEnd2;
        return true;
    }
}
