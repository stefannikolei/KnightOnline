using OpenKO.Network.Framing;
using Xunit;

namespace OpenKO.Network.Tests;

public class AgCompressedCodecTests
{
    [Fact]
    public void RoundTrip()
    {
        byte[] payload = Enumerable.Range(0, 900).Select(i => (byte)(i % 7 + 60)).ToArray();

        byte[]? encoded = AgCompressedCodec.Encode(payload);
        Assert.NotNull(encoded);

        byte[]? decoded = AgCompressedCodec.Decode(encoded);
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void CorruptCrcIsRejected()
    {
        byte[] payload = "abcabcabcabcabcabcabcabc"u8.ToArray();
        byte[]? encoded = AgCompressedCodec.Encode(payload);
        Assert.NotNull(encoded);

        encoded[4] ^= 0xFF; // flip a CRC byte

        Assert.Null(AgCompressedCodec.Decode(encoded));
    }

    [Fact]
    public void TruncatedInputIsRejected()
    {
        byte[] payload = "abcabcabcabcabcabcabcabc"u8.ToArray();
        byte[]? encoded = AgCompressedCodec.Encode(payload);
        Assert.NotNull(encoded);

        Assert.Null(AgCompressedCodec.Decode(encoded.AsSpan(0, encoded.Length - 3).ToArray()));
        Assert.Null(AgCompressedCodec.Decode(new byte[5]));
    }
}
