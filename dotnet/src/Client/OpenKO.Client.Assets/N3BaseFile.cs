using OpenKO.Core.Text;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3BaseFileAccess</c> (Client/N3Base/N3BaseFileAccess.cpp): the
/// shared [int32 nameLen][name] header and the file-format version that the
/// caller passes in (it is NOT stored in the file).
/// </summary>
public abstract class N3BaseFile
{
    /// <summary>m_szFileName (lower-cased, base-path-relative in the C++).</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>m_iFileFormatVersion (N3FORMAT_VER_*).</summary>
    public uint FileFormatVersion { get; set; } = N3FormatVersion.Unknown;

    /// <summary>m_szName raw bytes (CP949; kept raw for byte-exact round trips).</summary>
    public byte[] NameBytes { get; set; } = [];

    /// <summary>m_szName decoded.</summary>
    public string Name
    {
        get => KoEncoding.Cp949.GetString(NameBytes);
        set => NameBytes = KoEncoding.Cp949.GetBytes(value);
    }

    /// <summary>CN3BaseFileAccess::Load — the name header.</summary>
    public virtual void Load(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length > 0)
        {
            NameBytes = reader.ReadBytes(length);
            if (NameBytes.Length != length)
                throw new EndOfStreamException("N3 name header is truncated");
        }
        else
        {
            NameBytes = [];
        }
    }

    /// <summary>CN3BaseFileAccess::Save — mirror of the header (for tests/tools).</summary>
    public virtual void Save(BinaryWriter writer)
    {
        writer.Write(NameBytes.Length);
        writer.Write(NameBytes);
    }

    /// <summary>CN3BaseFileAccess::LoadFromFile(szFileName, iVer).</summary>
    public void LoadFromFile(string path, uint version = N3FormatVersion.Default)
    {
        FileFormatVersion = version;
        // The C++ lower-cases and strips the base path; the port keeps the
        // given path (no global base-path state).
        FileName = path;

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        Load(reader);
    }
}
