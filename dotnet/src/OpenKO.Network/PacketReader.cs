using System.Buffers.Binary;

namespace OpenKO.Network;

/// <summary>
/// Span equivalent of the Get* helpers in <c>Server/shared-server/utilities.cpp</c>.
/// All values are little-endian; the cursor advances exactly like the C++ index.
/// Reads past the end throw (the C++ would read out of bounds) — callers must
/// validate lengths the same way the C++ handlers do.
/// </summary>
public ref struct PacketReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buffer = buffer;

    public int Index { get; set; }

    public readonly int Remaining => _buffer.Length - Index;

    public byte GetByte() => _buffer[Index++];

    /// <summary>GetShort: reads an int16 and widens to int (sign-extended), as in the C++.</summary>
    public short GetShort()
    {
        short v = BinaryPrimitives.ReadInt16LittleEndian(_buffer[Index..]);
        Index += 2;
        return v;
    }

    public int GetInt()
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(_buffer[Index..]);
        Index += 4;
        return v;
    }

    public uint GetDWord()
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[Index..]);
        Index += 4;
        return v;
    }

    public float GetFloat()
    {
        float v = BinaryPrimitives.ReadSingleLittleEndian(_buffer[Index..]);
        Index += 4;
        return v;
    }

    public long GetInt64()
    {
        long v = BinaryPrimitives.ReadInt64LittleEndian(_buffer[Index..]);
        Index += 8;
        return v;
    }

    public ReadOnlySpan<byte> GetString(int len)
    {
        var s = _buffer.Slice(Index, len);
        Index += len;
        return s;
    }

    /// <summary>
    /// GetVarString with a uint8 or int16 length prefix (prefixSize 1 or 2).
    /// </summary>
    public ReadOnlySpan<byte> GetVarString(int prefixSize)
    {
        int len = prefixSize == 1 ? GetByte() : GetShort();
        if (len < 0)
            throw new ArgumentOutOfRangeException(nameof(prefixSize), "negative string length");
        return GetString(len);
    }

    /// <summary>
    /// CheckGetVarString: reads a length-prefixed string and validates
    /// 0 &lt; length &lt;= maxLength. Returns false (without a valid value) otherwise.
    /// </summary>
    public bool TryGetVarString(int prefixSize, int maxLength, out ReadOnlySpan<byte> value)
    {
        value = default;

        int len = prefixSize == 1 ? GetByte() : GetShort();
        if (len <= 0 || len > maxLength || len > Remaining)
            return false;

        value = GetString(len);
        return true;
    }
}
