using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenKO.IO;

/// <summary>
/// Read-only file access (port of FileIO/FileReader). The original memory-maps the file via llfio;
/// here we load it fully into memory, which is more than adequate for KO's individual asset files
/// and keeps the implementation portable.
///
/// In addition to the raw <see cref="IFile.Read"/> contract, this exposes typed little-endian
/// helpers (<see cref="Read{T}"/>, <see cref="ReadInt32"/>, …) matching the way the N3 loaders
/// consume raw structs from disk on the original x86 client.
/// </summary>
public sealed class FileReader : IFile
{
    private byte[] _data = Array.Empty<byte>();
    private long _offset;
    private bool _open;

    public string Path { get; private set; } = string.Empty;
    public long Offset => _offset;
    public long Size => _data.Length;
    public bool IsOpen => _open;

    /// <summary>The full file contents (valid while open).</summary>
    public ReadOnlySpan<byte> Memory => _data;

    public bool OpenExisting(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            _data = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return false;
        }

        Path = path;
        _offset = 0;
        _open = true;
        return true;
    }

    /// <summary>Opens an in-memory blob as if it were a file (useful for archives/tests).</summary>
    public bool OpenFromMemory(byte[] data, string name = "<memory>")
    {
        _data = data;
        Path = name;
        _offset = 0;
        _open = true;
        return true;
    }

    /// <summary>Not supported for a reader.</summary>
    public bool Create(string path) => false;

    public int Read(Span<byte> buffer)
    {
        if (!_open)
            return 0;

        long remaining = _data.Length - _offset;
        if (remaining <= 0)
            return 0;

        int toRead = (int)Math.Min(buffer.Length, remaining);
        _data.AsSpan((int)_offset, toRead).CopyTo(buffer);
        _offset += toRead;
        return toRead;
    }

    /// <summary>Reads a single little-endian value of an unmanaged type.</summary>
    public T Read<T>() where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        Span<byte> tmp = stackalloc byte[size];
        if (Read(tmp) != size)
            return default;

        if (!BitConverter.IsLittleEndian)
            tmp.Reverse();

        return Unsafe.ReadUnaligned<T>(ref tmp[0]);
    }

    public int ReadInt32() => Read<int>();
    public uint ReadUInt32() => Read<uint>();
    public short ReadInt16() => Read<short>();
    public ushort ReadUInt16() => Read<ushort>();
    public byte ReadByte() => Read<byte>();
    public float ReadSingle() => Read<float>();

    /// <summary>Reads an array of <paramref name="count"/> unmanaged values.</summary>
    public T[] ReadArray<T>(int count) where T : unmanaged
    {
        var result = new T[count];
        if (count == 0)
            return result;

        Read(MemoryMarshal.AsBytes(result.AsSpan()));
        if (!BitConverter.IsLittleEndian && Unsafe.SizeOf<T>() > 1)
        {
            // byte-swap each element on big-endian hosts
            for (int i = 0; i < count; i++)
                result[i] = Read<T>();
        }

        return result;
    }

    /// <summary>Reads <paramref name="length"/> raw bytes as a string using the given encoding.</summary>
    public string ReadFixedString(int length, Encoding? encoding = null)
    {
        if (length <= 0)
            return string.Empty;

        Span<byte> tmp = length <= 256 ? stackalloc byte[length] : new byte[length];
        Read(tmp);
        return (encoding ?? Encoding.Latin1).GetString(tmp);
    }

    public bool Write(ReadOnlySpan<byte> buffer) => false;

    public bool Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _offset + offset,
            SeekOrigin.End => _data.Length + offset,
            _ => -1
        };

        if (target < 0 || target > _data.Length)
            return false;

        _offset = target;
        return true;
    }

    public void Flush()
    {
    }

    public bool Close()
    {
        if (!_open)
            return false;

        _data = Array.Empty<byte>();
        _open = false;
        _offset = 0;
        return true;
    }

    public void Dispose() => Close();
}
