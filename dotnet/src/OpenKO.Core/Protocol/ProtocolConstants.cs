namespace OpenKO.Core.Protocol;

/// <summary>
/// Wire-protocol constants from the per-server <c>Define.h</c> headers and
/// <c>shared/globals.h</c> / <c>shared/packets.h</c>.
/// </summary>
public static class ProtocolConstants
{
    // Frame markers: [0xAA 0x55][int16 LE payload length][payload][0x55 0xAA]
    public const byte PacketStart1 = 0xAA;
    public const byte PacketStart2 = 0x55;
    public const byte PacketEnd1 = 0x55;
    public const byte PacketEnd2 = 0xAA;

    /// <summary>Header (2) + length (2) + trailer (2).</summary>
    public const int FrameOverhead = 6;

    public const int MaxPacketSize = 1024 * 8;
    public const int SocketBuffSize = 1024 * 16;

    public const int MaxIdSize = 20;
    public const int MaxPwSize = 12;

    // News blob markers (shared/packets.h). The client only cares about finding
    // the first and last '#'. These contain embedded NULs — the blob must be
    // assembled as raw bytes, never round-tripped through a string.
    public static ReadOnlySpan<byte> NewsMessageStart => new byte[] { (byte)'#', 0, (byte)'\n' };
    public static ReadOnlySpan<byte> NewsMessageEnd => new byte[] { 0, (byte)'\n', (byte)'#', 0, (byte)'\n', 0, (byte)'\n' };
    public const int MaxNewsCount = 3;
}
