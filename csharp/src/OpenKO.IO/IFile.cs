namespace OpenKO.IO;

/// <summary>
/// Cross-platform port of the C++ abstract <c>File</c> interface (FileIO/File.h).
/// Implementations provide buffered/streamed read or write access to a file.
/// </summary>
public interface IFile : IDisposable
{
    /// <summary>Path of the currently opened file.</summary>
    string Path { get; }

    /// <summary>Current read/write offset within the file.</summary>
    long Offset { get; }

    /// <summary>Current size of the file, in bytes.</summary>
    long Size { get; }

    /// <summary>Whether the file is currently open.</summary>
    bool IsOpen { get; }

    /// <summary>Opens an existing file.</summary>
    bool OpenExisting(string path);

    /// <summary>Creates a new file.</summary>
    bool Create(string path);

    /// <summary>Reads up to <paramref name="buffer"/>.Length bytes at the current offset.</summary>
    /// <returns>The number of bytes actually read (0 on failure / EOF).</returns>
    int Read(Span<byte> buffer);

    /// <summary>Writes <paramref name="buffer"/> at the current offset.</summary>
    /// <returns>true if all bytes were written.</returns>
    bool Write(ReadOnlySpan<byte> buffer);

    /// <summary>Changes the current offset.</summary>
    bool Seek(long offset, SeekOrigin origin);

    /// <summary>Flushes buffered data to disk.</summary>
    void Flush();

    /// <summary>Closes the file and releases resources.</summary>
    bool Close();
}
