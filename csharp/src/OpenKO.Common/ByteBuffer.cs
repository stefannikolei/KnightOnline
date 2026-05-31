using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace OpenKO.Common;

/// <summary>
/// Cross-platform port of the C++ <c>ByteBuffer</c> (shared/ByteBuffer.cpp).
///
/// Preserves the original wire semantics:
///  - little-endian primitive layout (the original used raw <c>memcpy</c> on x86),
///  - separate read (<see cref="ReadPosition"/>) and write (<see cref="WritePosition"/>) cursors,
///  - KO's "hacky" string length prefix that is either a single byte or a double byte
///    depending on the <see cref="DoubleByte"/> flag (see <see cref="SByte"/>/<see cref="DByte"/>).
///
/// Reads past the end of the buffer return <c>default</c> rather than throwing, mirroring the
/// original's bounds-checked <c>read&lt;T&gt;</c>.
/// </summary>
public class ByteBuffer
{
    public const int DefaultSize = 32;

    /// <summary>
    /// Encoding used when reading/writing length-prefixed strings.
    /// Defaults to Latin1 so every byte round-trips 1:1, matching the original's raw byte handling.
    /// </summary>
    public static Encoding StringEncoding { get; set; } = Encoding.Latin1;

    /// <summary>When true, string length prefixes are 2 bytes; when false, 1 byte.</summary>
    public bool DoubleByte = true;

    private byte[] _storage;
    private int _size;
    private int _rpos;
    private int _wpos;

    public ByteBuffer()
    {
        _storage = new byte[DefaultSize];
    }

    public ByteBuffer(int reserve)
    {
        _storage = new byte[reserve <= 0 ? DefaultSize : reserve];
    }

    public ByteBuffer(ByteBuffer other)
    {
        _storage = new byte[other._storage.Length];
        Array.Copy(other._storage, _storage, other._size);
        _size = other._size;
        _rpos = other._rpos;
        _wpos = other._wpos;
        DoubleByte = other.DoubleByte;
    }

    public void Clear()
    {
        _size = 0;
        _rpos = 0;
        _wpos = 0;
    }

    /// <summary>Hacky KO string flag - single-byte length prefix.</summary>
    public void SByte() => DoubleByte = false;

    /// <summary>Hacky KO string flag - double-byte length prefix.</summary>
    public void DByte() => DoubleByte = true;

    public int ReadPosition
    {
        get => _rpos;
        set => _rpos = value;
    }

    public int WritePosition
    {
        get => _wpos;
        set => _wpos = value;
    }

    public int Size => _size;

    public byte this[int pos] => Read<byte>(pos);

    /// <summary>A view over the meaningful contents of the buffer (0.._size).</summary>
    public ReadOnlySpan<byte> Contents => _storage.AsSpan(0, _size);

    public byte[] ToArray() => _storage.AsSpan(0, _size).ToArray();

    // ---------------------------------------------------------------------
    // raw read
    // ---------------------------------------------------------------------

    public bool Read(int pos, Span<byte> dest)
    {
        if (pos + dest.Length > _size)
            return false;

        _storage.AsSpan(pos, dest.Length).CopyTo(dest);
        return true;
    }

    public bool Read(Span<byte> dest)
    {
        if (!Read(_rpos, dest))
            return false;

        _rpos += dest.Length;
        return true;
    }

    // ---------------------------------------------------------------------
    // typed read (little-endian); out-of-range returns default
    // ---------------------------------------------------------------------

    public T Read<T>(int pos) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        if (pos + size > _size)
            return default;

        ReadOnlySpan<byte> span = _storage.AsSpan(pos, size);
        return ReadLittleEndian<T>(span);
    }

    public T Read<T>() where T : unmanaged
    {
        T value = Read<T>(_rpos);
        _rpos += Unsafe.SizeOf<T>();
        return value;
    }

    // ---------------------------------------------------------------------
    // strings (KO length-prefixed)
    // ---------------------------------------------------------------------

    public bool ReadString(int pos, out string dest)
    {
        dest = string.Empty;

        int len;
        if (DoubleByte)
        {
            if (pos + 2 > _size) return false;
            len = BinaryPrimitives.ReadUInt16LittleEndian(_storage.AsSpan(pos, 2));
            pos += 2;
        }
        else
        {
            if (pos + 1 > _size) return false;
            len = _storage[pos];
            pos += 1;
        }

        if (pos + len > _size) return false;
        dest = StringEncoding.GetString(_storage, pos, len);
        return true;
    }

    public bool ReadString(out string dest)
    {
        int start = _rpos;
        if (!ReadString(start, out dest))
            return false;

        _rpos += (DoubleByte ? 2 : 1) + StringEncoding.GetByteCount(dest);
        return true;
    }

    public bool ReadString(int pos, int len, out string dest)
    {
        dest = string.Empty;
        if (pos + len > _size) return false;
        dest = StringEncoding.GetString(_storage, pos, len);
        return true;
    }

    /// <summary>Reads a fixed-length string at the current cursor and advances it.</summary>
    public string ReadStringFixed(int len)
    {
        ReadString(_rpos, len, out string dest);
        _rpos += len;
        return dest;
    }

    // ---------------------------------------------------------------------
    // append (write at the end / _wpos)
    // ---------------------------------------------------------------------

    public void Append(ReadOnlySpan<byte> src)
    {
        if (src.Length == 0)
            return;

        EnsureCapacity(_wpos + src.Length);
        src.CopyTo(_storage.AsSpan(_wpos));
        _wpos += src.Length;
        if (_wpos > _size)
            _size = _wpos;
    }

    public void Append<T>(T value) where T : unmanaged
    {
        Span<byte> tmp = stackalloc byte[Unsafe.SizeOf<T>()];
        WriteLittleEndian(tmp, value);
        Append(tmp);
    }

    public void Append(ByteBuffer buffer) => Append(buffer.Contents);

    /// <summary>Writes a KO length-prefixed string at the current write position.</summary>
    public void AppendString(string value)
    {
        byte[] bytes = StringEncoding.GetBytes(value);
        if (DoubleByte)
            Append((ushort)bytes.Length);
        else
            Append((byte)bytes.Length);
        Append(bytes);
    }

    // ---------------------------------------------------------------------
    // put (overwrite at an absolute position, no cursor movement)
    // ---------------------------------------------------------------------

    public void Put(int pos, ReadOnlySpan<byte> src)
    {
        EnsureCapacity(pos + src.Length);
        src.CopyTo(_storage.AsSpan(pos));
        if (pos + src.Length > _size)
            _size = pos + src.Length;
    }

    public void Put<T>(int pos, T value) where T : unmanaged
    {
        Span<byte> tmp = stackalloc byte[Unsafe.SizeOf<T>()];
        WriteLittleEndian(tmp, value);
        Put(pos, tmp);
    }

    // ---------------------------------------------------------------------
    // stream-like helpers
    // ---------------------------------------------------------------------

    /// <summary>Loads raw bytes and positions cursors for reading (port of <c>resize</c>+content set).</summary>
    public void SetContents(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_storage);
        _size = data.Length;
        SyncForRead();
    }

    public void SyncForRead()
    {
        _rpos = 0;
        _wpos = _size;
    }

    public void ReadFrom(ByteBuffer buffer, int len)
    {
        Append(buffer._storage.AsSpan(buffer._rpos, len));
        buffer._rpos += len;
    }

    // ---------------------------------------------------------------------
    // internals
    // ---------------------------------------------------------------------

    private void EnsureCapacity(int required)
    {
        if (_storage.Length >= required)
            return;

        int newCapacity = Math.Max(_storage.Length * 2, required);
        Array.Resize(ref _storage, newCapacity);
    }

    private static T ReadLittleEndian<T>(ReadOnlySpan<byte> span) where T : unmanaged
    {
        // BitConverter/Unsafe both read in machine byte order; normalise to little-endian
        // to stay wire-compatible with the original x86 client regardless of host endianness.
        if (BitConverter.IsLittleEndian)
            return Unsafe.ReadUnaligned<T>(ref Unsafe.AsRef(in span[0]));

        Span<byte> tmp = stackalloc byte[span.Length];
        span.CopyTo(tmp);
        tmp.Reverse();
        return Unsafe.ReadUnaligned<T>(ref tmp[0]);
    }

    private static void WriteLittleEndian<T>(Span<byte> dest, T value) where T : unmanaged
    {
        Unsafe.WriteUnaligned(ref dest[0], value);
        if (!BitConverter.IsLittleEndian)
            dest.Reverse();
    }
}
