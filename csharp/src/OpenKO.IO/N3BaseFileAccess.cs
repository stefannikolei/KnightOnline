using System.Text;

namespace OpenKO.IO;

/// <summary>
/// Port of the C++ <c>CN3BaseFileAccess</c> (Client/N3Base/N3BaseFileAccess.cpp) — the common base
/// for all N3 file-backed resources (meshes, textures, scenes, …).
///
/// On disk each resource begins with its name: a 4-byte little-endian length followed by that many
/// raw bytes. Concrete loaders override <see cref="Load"/>/<see cref="Save"/> and call the base
/// first to read/write this header. <see cref="BasePath"/> mirrors the original static base path
/// (<c>s_szPath</c>) that local resource paths are resolved against.
/// </summary>
public class N3BaseFileAccess
{
    /// <summary>Base path that local resource file names are resolved against (port of <c>s_szPath</c>).</summary>
    public static string BasePath { get; set; } = string.Empty;

    /// <summary>Resource name as stored in the file header (port of <c>m_szName</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Local path (relative to <see cref="BasePath"/>) — port of <c>m_szFileName</c>.</summary>
    public string FileName { get; private set; } = string.Empty;

    public N3FormatVersion FileFormatVersion { get; set; } = N3FormatVersion.Unknown;

    /// <summary>LOD to use when loading (port of <c>m_iLOD</c>).</summary>
    public int Lod { get; set; }

    /// <summary>Sets the local file name, stripping the base path if present (port of <c>FileNameSet</c>).</summary>
    public void SetFileName(string fileName)
    {
        string lower = fileName.ToLowerInvariant();

        if (!string.IsNullOrEmpty(BasePath))
        {
            int pos = lower.IndexOf(BasePath, StringComparison.Ordinal);
            if (pos >= 0)
            {
                FileName = lower[(pos + BasePath.Length)..];
                return;
            }
        }

        FileName = lower;
    }

    public virtual void Release()
    {
        FileName = string.Empty;
        Lod = 0;
        Name = string.Empty;
    }

    /// <summary>Reads the resource name header. Concrete types override and call base first.</summary>
    public virtual bool Load(IFile file)
    {
        int len = 0;
        Span<byte> lenBuf = stackalloc byte[4];
        if (file.Read(lenBuf) == 4)
            len = BitConverter.ToInt32(lenBuf);

        if (len > 0)
            Name = ReadName(file, len);
        else
            Name = string.Empty;

        return true;
    }

    private static string ReadName(IFile file, int len)
    {
        Span<byte> buf = len <= 256 ? stackalloc byte[len] : new byte[len];
        file.Read(buf);
        return Encoding.Latin1.GetString(buf);
    }

    /// <summary>Resolves the full path from <see cref="FileName"/> and <see cref="BasePath"/>.</summary>
    public string ResolveFullPath()
    {
        // A name containing a drive/UNC/separator marker is treated as an absolute path.
        if (FileName.Contains(':') || FileName.Contains("\\\\") || FileName.Contains("//"))
            return FileName;

        return string.IsNullOrEmpty(BasePath) ? FileName : BasePath + FileName;
    }

    public bool LoadFromFile()
    {
        if (string.IsNullOrEmpty(FileName))
            return false;

        using var file = new FileReader();
        if (!file.OpenExisting(ResolveFullPath()))
            return false;

        return Load(file);
    }

    public bool LoadFromFile(string fileName, N3FormatVersion version)
    {
        FileFormatVersion = version;
        SetFileName(fileName);
        return LoadFromFile();
    }

    public bool LoadFromFile(string fileName) => LoadFromFile(fileName, N3Format.Default);

    /// <summary>Writes the resource name header. Concrete types override and call base first.</summary>
    public virtual bool Save(IFile file)
    {
        byte[] nameBytes = Encoding.Latin1.GetBytes(Name);
        Span<byte> lenBuf = stackalloc byte[4];
        BitConverter.TryWriteBytes(lenBuf, nameBytes.Length);
        file.Write(lenBuf);
        if (nameBytes.Length > 0)
            file.Write(nameBytes);
        return true;
    }

    public bool SaveToFile()
    {
        if (string.IsNullOrEmpty(FileName))
            return false;

        using var file = new FileWriter();
        if (!file.Create(ResolveFullPath()))
            return false;

        return Save(file);
    }

    public bool SaveToFile(string fileName)
    {
        SetFileName(fileName);
        return SaveToFile();
    }
}
