namespace OpenKO.N3;

/// <summary>
/// Pixel formats used by N3 textures. The numeric values are the original Direct3D 9
/// <c>D3DFORMAT</c> codes, because those exact integers are what is stored in the ".dxt"
/// (NTF) file header — preserving them keeps the loader byte-compatible with the game assets.
///
/// The DXT* values are FourCC codes (e.g. 'D','X','T','1' little-endian => 0x31545844).
/// </summary>
public enum N3PixelFormat : uint
{
    Unknown = 0,

    R8G8B8 = 20,
    A8R8G8B8 = 21,
    X8R8G8B8 = 22,
    A1R5G5B5 = 25,
    A4R4G4B4 = 26,

    Dxt1 = 0x31545844, // 'DXT1'
    Dxt2 = 0x32545844, // 'DXT2'
    Dxt3 = 0x33545844, // 'DXT3'
    Dxt4 = 0x34545844, // 'DXT4'
    Dxt5 = 0x35545844, // 'DXT5'
}

public static class N3PixelFormatExtensions
{
    /// <summary>True for the block-compressed (S3TC/DXT) formats.</summary>
    public static bool IsCompressed(this N3PixelFormat format) => format switch
    {
        N3PixelFormat.Dxt1 or N3PixelFormat.Dxt2 or N3PixelFormat.Dxt3
            or N3PixelFormat.Dxt4 or N3PixelFormat.Dxt5 => true,
        _ => false,
    };

    /// <summary>Bytes per pixel for the uncompressed formats (0 for compressed/unknown).</summary>
    public static int BytesPerPixel(this N3PixelFormat format) => format switch
    {
        N3PixelFormat.A1R5G5B5 or N3PixelFormat.A4R4G4B4 => 2,
        N3PixelFormat.R8G8B8 => 3,
        N3PixelFormat.A8R8G8B8 or N3PixelFormat.X8R8G8B8 => 4,
        _ => 0,
    };

    /// <summary>
    /// Size in bytes of one mip level stored in this format, matching the original loader's
    /// per-level read sizes (CN3Texture::Load):
    ///  - DXT1: w*h/2, other DXT: w*h, uncompressed: w*h*bpp.
    /// </summary>
    public static int LevelSize(this N3PixelFormat format, int width, int height) => format switch
    {
        N3PixelFormat.Dxt1 => width * height / 2,
        N3PixelFormat.Dxt2 or N3PixelFormat.Dxt3
            or N3PixelFormat.Dxt4 or N3PixelFormat.Dxt5 => width * height,
        _ => width * height * format.BytesPerPixel(),
    };
}
