using OpenKO.Core.IO;
using OpenKO.Core.Text;
using Xunit;

namespace OpenKO.Core.Tests;

public class ByteBufferTests
{
    [Fact]
    public void PrimitiveRoundTrip()
    {
        var buffer = new ByteBuffer();
        buffer.Append((byte)0xAB);
        buffer.Append((sbyte)-5);
        buffer.Append(true);
        buffer.Append((ushort)0xBEEF);
        buffer.Append((short)-1234);
        buffer.Append(0xDEADBEEFu);
        buffer.Append(-123456789);
        buffer.Append(0x1122334455667788UL);
        buffer.Append(-987654321012345678L);
        buffer.Append(3.5f);

        Assert.Equal(0xAB, buffer.ReadByte());
        Assert.Equal(-5, buffer.ReadSByte());
        Assert.True(buffer.ReadBool());
        Assert.Equal(0xBEEF, buffer.ReadUInt16());
        Assert.Equal(-1234, buffer.ReadInt16());
        Assert.Equal(0xDEADBEEFu, buffer.ReadUInt32());
        Assert.Equal(-123456789, buffer.ReadInt32());
        Assert.Equal(0x1122334455667788UL, buffer.ReadUInt64());
        Assert.Equal(-987654321012345678L, buffer.ReadInt64());
        Assert.Equal(3.5f, buffer.ReadSingle());
    }

    [Fact]
    public void IntegersAreLittleEndianOnTheWire()
    {
        var buffer = new ByteBuffer();
        buffer.Append((ushort)0x1234);
        buffer.Append(0x56789ABCu);

        Assert.Equal(new byte[] { 0x34, 0x12, 0xBC, 0x9A, 0x78, 0x56 }, buffer.Contents.ToArray());
    }

    [Fact]
    public void ReadPastEndReturnsDefault()
    {
        var buffer = new ByteBuffer();
        buffer.Append((byte)1);

        Assert.Equal(1, buffer.ReadByte());
        // Past the end: default values, no exception (C++ behavior).
        Assert.Equal(0, buffer.ReadByte());
        Assert.Equal(0, buffer.ReadUInt16());
        Assert.Equal(0u, buffer.ReadUInt32());
        Assert.Equal(0f, buffer.ReadSingle());
    }

    [Fact]
    public void StringRoundTrip_DoubleByteMode()
    {
        var buffer = new ByteBuffer();
        buffer.AppendString("knight"u8);

        // uint16 LE length prefix + raw bytes
        Assert.Equal(new byte[] { 6, 0, (byte)'k', (byte)'n', (byte)'i', (byte)'g', (byte)'h', (byte)'t' },
            buffer.Contents.ToArray());
        Assert.Equal("knight"u8.ToArray(), buffer.ReadStringBytes());
    }

    [Fact]
    public void StringRoundTrip_SingleByteMode()
    {
        var buffer = new ByteBuffer();
        buffer.SByte();
        buffer.AppendString("ko"u8);

        Assert.Equal(new byte[] { 2, (byte)'k', (byte)'o' }, buffer.Contents.ToArray());
        Assert.Equal("ko"u8.ToArray(), buffer.ReadStringBytes());
    }

    [Fact]
    public void StringWithEmbeddedNulSurvives()
    {
        byte[] payload = { (byte)'#', 0, (byte)'\n', (byte)'x', 0 };
        var buffer = new ByteBuffer();
        buffer.AppendString(payload);

        Assert.Equal(payload, buffer.ReadStringBytes());
    }

    [Fact]
    public void Cp949RoundTrip()
    {
        // "기사" (knight) in CP949
        string text = "기사";
        var buffer = new ByteBuffer();
        buffer.AppendString(text, KoEncoding.Cp949);

        Assert.Equal(text, buffer.ReadString(KoEncoding.Cp949));
    }

    [Fact]
    public void PacketCarriesOpcodeAsFirstByte()
    {
        var packet = new Packet(0x2C);
        packet.Append((ushort)42);

        Assert.Equal(0x2C, packet.Opcode);
        Assert.Equal(3, packet.Size);

        packet.Initialize(0x01);
        Assert.Equal(0x01, packet.Opcode);
        Assert.Equal(1, packet.Size);
    }
}
