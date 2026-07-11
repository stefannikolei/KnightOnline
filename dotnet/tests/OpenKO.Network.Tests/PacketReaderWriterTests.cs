using OpenKO.Network;
using Xunit;

namespace OpenKO.Network.Tests;

public class PacketReaderWriterTests
{
    [Fact]
    public void WriterProducesUtilitiesCppLayout()
    {
        Span<byte> buffer = stackalloc byte[64];
        var writer = new PacketWriter(buffer);

        writer.SetByte(0xF3);
        writer.SetShort(-2);
        writer.SetInt(0x11223344);
        writer.SetDWord(0xCAFEBABE);
        writer.SetInt64(0x0102030405060708);
        writer.SetString2("ab"u8);
        writer.SetString1("c"u8);

        byte[] expected =
        {
            0xF3,
            0xFE, 0xFF,                                     // int16 -2 LE
            0x44, 0x33, 0x22, 0x11,                         // int32 LE
            0xBE, 0xBA, 0xFE, 0xCA,                         // uint32 LE
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // int64 LE
            0x02, 0x00, (byte)'a', (byte)'b',               // SetString2
            0x01, (byte)'c'                                 // SetString1
        };

        Assert.Equal(expected, writer.Written.ToArray());
    }

    [Fact]
    public void ReaderRoundTripsWriter()
    {
        Span<byte> buffer = stackalloc byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(0x01);
        writer.SetShort(1298);
        writer.SetString2("knight"u8);
        writer.SetFloat(2.5f);

        var reader = new PacketReader(writer.Written);
        Assert.Equal(0x01, reader.GetByte());
        Assert.Equal(1298, reader.GetShort());
        Assert.True("knight"u8.SequenceEqual(reader.GetVarString(2)));
        Assert.Equal(2.5f, reader.GetFloat());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void TryGetVarString_EnforcesBounds()
    {
        Span<byte> buffer = stackalloc byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetString2("longer-than-max"u8);

        var reader = new PacketReader(writer.Written);
        Assert.False(reader.TryGetVarString(2, maxLength: 5, out _));

        // Zero length is invalid too (CheckGetVarString: nRet <= 0).
        var zeroReader = new PacketReader(new byte[] { 0x00, 0x00 });
        Assert.False(zeroReader.TryGetVarString(2, maxLength: 5, out _));

        // Length prefix exceeding the actual data must fail, not throw.
        var lyingReader = new PacketReader(new byte[] { 0x10, 0x00, (byte)'x' });
        Assert.False(lyingReader.TryGetVarString(2, maxLength: 32, out _));
    }
}
