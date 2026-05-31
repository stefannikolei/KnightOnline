using OpenKO.Common;
using Xunit;

namespace OpenKO.Tests;

public class ByteBufferTests
{
    [Fact]
    public void PrimitivesRoundTripLittleEndian()
    {
        var buf = new ByteBuffer();
        buf.Append((byte)0x12);
        buf.Append((ushort)0x3456);
        buf.Append(0x789ABCDEu);
        buf.Append(1.5f);

        // Verify the raw little-endian layout matches the original x86 memcpy behaviour.
        byte[] bytes = buf.ToArray();
        Assert.Equal(new byte[] { 0x12, 0x56, 0x34, 0xDE, 0xBC, 0x9A, 0x78 }, bytes[..7]);

        buf.SyncForRead();
        Assert.Equal((byte)0x12, buf.Read<byte>());
        Assert.Equal((ushort)0x3456, buf.Read<ushort>());
        Assert.Equal(0x789ABCDEu, buf.Read<uint>());
        Assert.Equal(1.5f, buf.Read<float>());
    }

    [Fact]
    public void DoubleByteStringRoundTrips()
    {
        var buf = new ByteBuffer();
        buf.DByte();
        buf.AppendString("KnightOnline");

        buf.SyncForRead();
        Assert.True(buf.ReadString(out string value));
        Assert.Equal("KnightOnline", value);
    }

    [Fact]
    public void SingleByteStringUsesOneByteLengthPrefix()
    {
        var buf = new ByteBuffer();
        buf.SByte();
        buf.AppendString("abc");

        byte[] bytes = buf.ToArray();
        Assert.Equal(3, bytes[0]); // single-byte length prefix
        Assert.Equal(4, bytes.Length);

        buf.SyncForRead();
        buf.SByte();
        Assert.True(buf.ReadString(out string value));
        Assert.Equal("abc", value);
    }

    [Fact]
    public void ReadPastEndReturnsDefault()
    {
        var buf = new ByteBuffer();
        buf.Append((byte)1);
        buf.SyncForRead();

        buf.Read<byte>();
        Assert.Equal(0u, buf.Read<uint>()); // out of range -> default
    }

    [Fact]
    public void PutOverwritesWithoutMovingCursor()
    {
        var buf = new ByteBuffer();
        buf.Append(0u);
        buf.Put(0, 0xABCDu);

        buf.SyncForRead();
        Assert.Equal((ushort)0xABCD, buf.Read<ushort>());
    }
}
