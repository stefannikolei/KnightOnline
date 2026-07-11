using System.Buffers.Binary;
using System.Text;

namespace OpenKO.Core.IO;

/// <summary>
/// Port of <c>shared/ByteBuffer.{h,cpp}</c>.
/// All integers are little-endian (the C++ memcpy's raw POD values on x86).
/// Reads past the end return <c>default</c> without throwing, as in the C++.
/// Strings on the wire are a length prefix (uint16 LE by default, uint8 in
/// single-byte mode — see <see cref="SByte"/>/<see cref="DByte"/>) followed by
/// raw bytes with no terminator. They are exposed as <c>byte[]</c> first-class;
/// use <see cref="Text.KoEncoding.Cp949"/> only at logging/DB/UI boundaries.
/// </summary>
public class ByteBuffer
{
    public const int DefaultSize = 32;

    private byte[] _storage;
    private int _size;
    private int _rpos;
    private int _wpos;

    /// <summary>KO string-length prefix mode: true = uint16 prefix, false = uint8.</summary>
    public bool DoubleByte { get; set; } = true;

    public ByteBuffer()
        : this(DefaultSize)
    {
    }

    public ByteBuffer(int reserve)
    {
        _storage = new byte[reserve <= 0 ? DefaultSize : reserve];
    }

    public ByteBuffer(ByteBuffer other)
    {
        _storage = new byte[other._size < DefaultSize ? DefaultSize : other._size];
        Array.Copy(other._storage, _storage, other._size);
        _size = other._size;
        _rpos = other._rpos;
        _wpos = other._wpos;
    }

    public void Clear()
    {
        _size = 0;
        _rpos = _wpos = 0;
    }

    /// <summary>Hacky KO string flag: single-byte length prefix.</summary>
    public void SByte() => DoubleByte = false;

    /// <summary>Hacky KO string flag: double-byte length prefix.</summary>
    public void DByte() => DoubleByte = true;

    public byte this[int pos] => ReadByte(pos);

    public int ReadPos
    {
        get => _rpos;
        set => _rpos = value;
    }

    public int WritePos
    {
        get => _wpos;
        set => _wpos = value;
    }

    public int Size => _size;

    public ReadOnlySpan<byte> Contents => _storage.AsSpan(0, _size);

    // ---- positional reads (return default past the end, like the C++) ----

    private ReadOnlySpan<byte> Slice(int pos, int len)
        => pos + len > _size ? default : _storage.AsSpan(pos, len);

    public byte ReadByte(int pos) => pos + 1 > _size ? default : _storage[pos];

    public sbyte ReadSByte(int pos) => (sbyte)ReadByte(pos);

    public bool ReadBool(int pos) => ReadByte(pos) != 0;

    public ushort ReadUInt16(int pos)
    {
        var s = Slice(pos, 2);
        return s.IsEmpty ? default : BinaryPrimitives.ReadUInt16LittleEndian(s);
    }

    public short ReadInt16(int pos) => (short)ReadUInt16(pos);

    public uint ReadUInt32(int pos)
    {
        var s = Slice(pos, 4);
        return s.IsEmpty ? default : BinaryPrimitives.ReadUInt32LittleEndian(s);
    }

    public int ReadInt32(int pos) => (int)ReadUInt32(pos);

    public ulong ReadUInt64(int pos)
    {
        var s = Slice(pos, 8);
        return s.IsEmpty ? default : BinaryPrimitives.ReadUInt64LittleEndian(s);
    }

    public long ReadInt64(int pos) => (long)ReadUInt64(pos);

    public float ReadSingle(int pos)
    {
        var s = Slice(pos, 4);
        return s.IsEmpty ? default : BinaryPrimitives.ReadSingleLittleEndian(s);
    }

    // ---- sequential reads ----

    public byte ReadByte()
    {
        byte r = ReadByte(_rpos);
        _rpos += 1;
        return r;
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUInt16()
    {
        ushort r = ReadUInt16(_rpos);
        _rpos += 2;
        return r;
    }

    public short ReadInt16() => (short)ReadUInt16();

    public uint ReadUInt32()
    {
        uint r = ReadUInt32(_rpos);
        _rpos += 4;
        return r;
    }

    public int ReadInt32() => (int)ReadUInt32();

    public ulong ReadUInt64()
    {
        ulong r = ReadUInt64(_rpos);
        _rpos += 8;
        return r;
    }

    public long ReadInt64() => (long)ReadUInt64();

    public float ReadSingle()
    {
        float r = ReadSingle(_rpos);
        _rpos += 4;
        return r;
    }

    public bool Read(Span<byte> dest)
    {
        if (_rpos + dest.Length > _size)
            return false;

        _storage.AsSpan(_rpos, dest.Length).CopyTo(dest);
        _rpos += dest.Length;
        return true;
    }

    /// <summary>Reads a length-prefixed string as raw bytes; empty array on underrun.</summary>
    public byte[] ReadStringBytes()
    {
        int len;
        if (DoubleByte)
        {
            if (_rpos + 2 > _size)
                return [];
            len = ReadUInt16();
        }
        else
        {
            if (_rpos + 1 > _size)
                return [];
            len = ReadByte();
        }

        var dest = new byte[len];
        // Mirror the C++: the length prefix is consumed even if the payload underruns.
        Read(dest);
        return dest;
    }

    /// <summary>Reads a fixed-length string as raw bytes (no prefix).</summary>
    public byte[] ReadStringBytes(int len)
    {
        var dest = new byte[len];
        Read(dest);
        return dest;
    }

    public string ReadString(Encoding encoding) => encoding.GetString(ReadStringBytes());

    public string ReadString(Encoding encoding, int len) => encoding.GetString(ReadStringBytes(len));

    // ---- writes ----

    private void EnsureCapacity(int required)
    {
        if (_storage.Length >= required)
            return;

        int newCapacity = _storage.Length * 2;
        if (newCapacity < required)
            newCapacity = required;
        Array.Resize(ref _storage, newCapacity);
    }

    public void Append(ReadOnlySpan<byte> src)
    {
        if (src.IsEmpty)
            return;

        EnsureCapacity(_wpos + src.Length);
        src.CopyTo(_storage.AsSpan(_wpos));
        _wpos += src.Length;
        if (_wpos > _size)
            _size = _wpos;
    }

    public void Append(ByteBuffer buffer)
    {
        if (buffer.Size > 0)
            Append(buffer.Contents);
    }

    public void Append(byte value)
    {
        EnsureCapacity(_wpos + 1);
        _storage[_wpos] = value;
        _wpos += 1;
        if (_wpos > _size)
            _size = _wpos;
    }

    public void Append(sbyte value) => Append((byte)value);

    public void Append(bool value) => Append(value ? (byte)1 : (byte)0);

    public void Append(ushort value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
        Append(tmp);
    }

    public void Append(short value) => Append((ushort)value);

    public void Append(uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
        Append(tmp);
    }

    public void Append(int value) => Append((uint)value);

    public void Append(ulong value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
        Append(tmp);
    }

    public void Append(long value) => Append((ulong)value);

    public void Append(float value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(tmp, value);
        Append(tmp);
    }

    /// <summary>Appends a KO length-prefixed string (prefix width per <see cref="DoubleByte"/>).</summary>
    public void AppendString(ReadOnlySpan<byte> value)
    {
        if (DoubleByte)
            Append((ushort)value.Length);
        else
            Append((byte)value.Length);
        Append(value);
    }

    public void AppendString(string value, Encoding encoding) => AppendString(encoding.GetBytes(value));

    public void Put(int pos, ReadOnlySpan<byte> src)
    {
        if (pos + src.Length > _size)
            throw new ArgumentOutOfRangeException(nameof(pos));

        src.CopyTo(_storage.AsSpan(pos));
    }

    public void ReadFrom(ByteBuffer buffer, int len)
    {
        Append(buffer.Contents.Slice(buffer.ReadPos, len));
        buffer.ReadPos += len;
    }

    public void Resize(int newSize)
    {
        EnsureCapacity(newSize);
        if (newSize > _size)
            Array.Clear(_storage, _size, newSize - _size);
        _size = newSize;
        _rpos = 0;
        _wpos = _size;
    }

    public void SyncForRead()
    {
        _rpos = 0;
        _wpos = _size;
    }
}
