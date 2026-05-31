using OpenKO.Common;
using OpenKO.Net;
using Xunit;

namespace OpenKO.Tests;

public class PacketFramingTests
{
    [Fact]
    public void FrameHasCorrectHeaderLengthAndTail()
    {
        var payload = new byte[] { (byte)GameOpcode.Move, 0x10, 0x20 };
        uint counter = 0;
        byte[] frame = PacketFraming.BuildFrame(payload, crypto: null, ref counter);

        Assert.Equal(payload.Length + 6, frame.Length);
        // header bytes (htons 0xAA55)
        Assert.Equal(0xAA, frame[0]);
        Assert.Equal(0x55, frame[1]);
        // length (little-endian)
        Assert.Equal(payload.Length, frame[2] | (frame[3] << 8));
        // tail bytes (htons 0x55AA)
        Assert.Equal(0x55, frame[^2]);
        Assert.Equal(0xAA, frame[^1]);
    }

    [Fact]
    public void UnencryptedFrameRoundTrips()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        uint counter = 0;
        byte[] frame = PacketFraming.BuildFrame(payload, crypto: null, ref counter);

        bool ok = PacketFraming.TryParseFrame(frame, crypto: null, out byte[] parsed, out int consumed);
        Assert.True(ok);
        Assert.Equal(frame.Length, consumed);
        Assert.Equal(payload, parsed);
    }

    [Fact]
    public void PartialFrameIsNotConsumed()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        uint counter = 0;
        byte[] frame = PacketFraming.BuildFrame(payload, crypto: null, ref counter);

        bool ok = PacketFraming.TryParseFrame(frame.AsSpan(0, frame.Length - 2), crypto: null, out _, out int consumed);
        Assert.False(ok);
        Assert.Equal(0, consumed);
    }
}
