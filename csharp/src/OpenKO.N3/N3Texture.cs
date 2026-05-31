using OpenKO.IO;

namespace OpenKO.N3;

/// <summary>
/// Cross-platform port of the C++ <c>CN3Texture</c> (Client/N3Base/N3Texture.cpp) — a loader for
/// KO's "NTF" (.dxt) texture files.
///
/// The original loaded straight into a Direct3D 9 texture and contained a lot of device-cap-specific
/// fallback logic (skipping uncompressed copies appended for cards lacking DXT support, "voodoo"
/// 256x256 copies, LOD skipping, etc.). This headless port instead reads the <b>primary</b> mip
/// chain in the pixel format stored in the file and exposes it as raw byte arrays, ready to be
/// uploaded by the OpenGL renderer later. Modern GPUs support S3TC/DXT and the 16-bit formats
/// directly, so the primary data is exactly what we want.
///
/// File layout (after the base resource-name header):
/// <code>
///   char  szID[4]      // "NTF" + version byte (>= 3)
///   int32 width
///   int32 height
///   int32 format       // D3DFORMAT code (see N3PixelFormat)
///   int32 mipMap       // BOOL: whether a mip chain follows
///   &lt;primary mip levels in 'format', from level 0 down to 4x4&gt;
///   &lt;trailing uncompressed/voodoo copies — ignored by this loader&gt;
/// </code>
/// </summary>
public class N3Texture : N3BaseFileAccess
{
    public int TextureWidth { get; private set; }
    public int TextureHeight { get; private set; }
    public N3PixelFormat Format { get; private set; }
    public bool HasMipMap { get; private set; }

    /// <summary>NTF format version (the 4th byte of the "NTF" id; modern files are 3, sometimes 7).</summary>
    public byte Version { get; private set; }

    /// <summary>Raw pixel data for each mip level (level 0 first), in <see cref="Format"/>.</summary>
    public IReadOnlyList<byte[]> Levels => _levels;

    private readonly List<byte[]> _levels = new();

    public int MipMapCount => _levels.Count;

    public override void Release()
    {
        base.Release();
        TextureWidth = 0;
        TextureHeight = 0;
        Format = N3PixelFormat.Unknown;
        HasMipMap = false;
        Version = 0;
        _levels.Clear();
    }

    /// <summary>
    /// Number of mip levels the original would generate for a texture of the given size,
    /// stepping down by half until either dimension drops below 4 (port of CN3Texture::Create).
    /// </summary>
    public static int ComputeMipCount(int width, int height, bool mipMap)
    {
        if (!mipMap)
            return 1;

        int count = 0;
        for (int w = width, h = height; w >= 4 && h >= 4; w /= 2, h /= 2)
            count++;
        return count;
    }

    public override bool Load(IFile file)
    {
        var reader = file as FileReader
            ?? throw new ArgumentException("N3Texture.Load requires a FileReader", nameof(file));

        Release();

        base.Load(file); // resource name header (must run after Release, which clears Name)

        // __DXT_HEADER: szID[4], int width, int height, int format, int mipMap
        Span<byte> id = stackalloc byte[4];
        if (reader.Read(id) != 4)
            return false;

        if (id[0] != 'N' || id[1] != 'T' || id[2] != 'F')
            return false; // not a Noah Texture File

        Version = id[3];

        TextureWidth = reader.ReadInt32();
        TextureHeight = reader.ReadInt32();
        Format = (N3PixelFormat)reader.ReadUInt32();
        HasMipMap = reader.ReadInt32() != 0;

        if (TextureWidth <= 0 || TextureHeight <= 0 || Format == N3PixelFormat.Unknown)
            return false;

        int mipCount = ComputeMipCount(TextureWidth, TextureHeight, HasMipMap);

        int levelWidth = TextureWidth;
        int levelHeight = TextureHeight;
        for (int i = 0; i < mipCount; i++)
        {
            int size = Format.LevelSize(levelWidth, levelHeight);
            if (size <= 0)
                break;

            var level = new byte[size];
            if (reader.Read(level) != size)
            {
                // Truncated/short file — keep whatever full levels we read.
                break;
            }

            _levels.Add(level);
            levelWidth /= 2;
            levelHeight /= 2;
        }

        return _levels.Count > 0;
    }

    public override bool Save(IFile file)
    {
        base.Save(file);

        var writer = file as FileWriter
            ?? throw new ArgumentException("N3Texture.Save requires a FileWriter", nameof(file));

        // Write a version-3 NTF header.
        Span<byte> id = stackalloc byte[4] { (byte)'N', (byte)'T', (byte)'F', 3 };
        writer.Write(id);
        writer.Write(TextureWidth);
        writer.Write(TextureHeight);
        writer.Write((uint)Format);
        writer.Write(HasMipMap ? 1 : 0);

        foreach (byte[] level in _levels)
            writer.Write(level.AsSpan());

        return true;
    }

    /// <summary>Populates the texture in-memory (used by tools/tests).</summary>
    public void SetData(string name, int width, int height, N3PixelFormat format, IEnumerable<byte[]> levels)
    {
        Name = name;
        TextureWidth = width;
        TextureHeight = height;
        Format = format;
        _levels.Clear();
        _levels.AddRange(levels);
        HasMipMap = _levels.Count > 1;
    }
}
