using System.Buffers.Binary;

namespace OpenKO.Network;

/// <summary>
/// Span equivalent of the Set* helpers in <c>Server/shared-server/utilities.cpp</c>.
/// All values are written little-endian at a moving cursor over a caller-provided buffer
/// (the C++ handlers build responses in a fixed char[] the same way).
/// </summary>
public ref struct PacketWriter(Span<byte> buffer)
{
    private readonly Span<byte> _buffer = buffer;

    public int Index { get; set; }

    public readonly ReadOnlySpan<byte> Written => _buffer[..Index];

    public void SetByte(byte value) => _buffer[Index++] = value;

    public void SetShort(int value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(_buffer[Index..], (short)value);
        Index += 2;
    }

    public void SetInt(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[Index..], value);
        Index += 4;
    }

    public void SetDWord(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[Index..], value);
        Index += 4;
    }

    public void SetFloat(float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(_buffer[Index..], value);
        Index += 4;
    }

    public void SetInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer[Index..], value);
        Index += 8;
    }

    public void SetString(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_buffer[Index..]);
        Index += value.Length;
    }

    /// <summary>SetString1: uint8 length prefix + raw bytes.</summary>
    public void SetString1(ReadOnlySpan<byte> value)
    {
        SetByte((byte)value.Length);
        SetString(value);
    }

    /// <summary>SetString2: int16 LE length prefix + raw bytes.</summary>
    public void SetString2(ReadOnlySpan<byte> value)
    {
        SetShort((short)value.Length);
        SetString(value);
    }
}
