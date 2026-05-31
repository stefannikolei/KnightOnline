using System.Runtime.CompilerServices;
using System.Text;

namespace OpenKO.IO;

/// <summary>Write access (port of FileIO/FileWriter), backed by a <see cref="FileStream"/>.</summary>
public sealed class FileWriter : IFile
{
    private FileStream? _stream;

    public string Path { get; private set; } = string.Empty;
    public long Offset => _stream?.Position ?? 0;
    public long Size => _stream?.Length ?? 0;
    public bool IsOpen => _stream != null;

    public bool OpenExisting(string path)
    {
        try
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            Path = path;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public bool Create(string path)
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            Path = path;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public int Read(Span<byte> buffer) => _stream?.Read(buffer) ?? 0;

    public bool Write(ReadOnlySpan<byte> buffer)
    {
        if (_stream == null)
            return false;

        _stream.Write(buffer);
        return true;
    }

    /// <summary>Writes a single little-endian value of an unmanaged type.</summary>
    public bool Write<T>(T value) where T : unmanaged
    {
        Span<byte> tmp = stackalloc byte[Unsafe.SizeOf<T>()];
        Unsafe.WriteUnaligned(ref tmp[0], value);
        if (!BitConverter.IsLittleEndian)
            tmp.Reverse();
        return Write(tmp);
    }

    public bool WriteFixedString(string value, Encoding? encoding = null)
        => Write((encoding ?? Encoding.Latin1).GetBytes(value));

    public bool Seek(long offset, SeekOrigin origin)
    {
        if (_stream == null)
            return false;

        _stream.Seek(offset, origin);
        return true;
    }

    public void Flush() => _stream?.Flush();

    public bool Close()
    {
        if (_stream == null)
            return false;

        _stream.Dispose();
        _stream = null;
        return true;
    }

    public void Dispose() => Close();
}
