namespace OpenKO.Client.Assets;

/// <summary>D3DFORMAT values the .dxt container uses (My_3DStruct / d3d9types).</summary>
public enum N3PixelFormat : uint
{
    Unknown = 0,
    R8G8B8 = 20,
    A8R8G8B8 = 21,
    X8R8G8B8 = 22,
    R5G6B5 = 23,
    A1R5G5B5 = 25,
    A4R4G4B4 = 26,
    Dxt1 = 0x31545844, // 'DXT1'
    Dxt2 = 0x32545844,
    Dxt3 = 0x33545844,
    Dxt4 = 0x34545844,
    Dxt5 = 0x35545844,
}

/// <summary>
/// Port of <c>CN3Texture</c> loading (Client/N3Base/N3Texture.cpp): the "NTF"
/// .dxt container with DXT1–5 or uncompressed levels, the 4x4-floor mip chain
/// and the 16-bpp fallback pyramid that old cards used. The port always takes
/// the "DXT supported, no LOD, no device caps" path and keeps the C++ reader's
/// stream positioning verbatim — including its non-mip under-skip (see below).
/// </summary>
public sealed class N3Texture : N3BaseFile
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    public N3PixelFormat Format { get; private set; }

    public bool HasMipMaps { get; private set; }

    /// <summary>The NTF version byte (szID[3]; 3 = plain, 7 = encrypted).</summary>
    public byte ContainerVersion { get; private set; }

    /// <summary>
    /// Raw level data, largest first. DXT levels hold the compressed blocks
    /// (with the C++ GetTextureSize quirk applied); uncompressed levels hold
    /// tightly packed rows (width * pixelSize per row).
    /// </summary>
    public List<byte[]> MipLevels { get; } = [];

    public static bool IsCompressed(N3PixelFormat format)
        => format is N3PixelFormat.Dxt1 or N3PixelFormat.Dxt2 or N3PixelFormat.Dxt3
            or N3PixelFormat.Dxt4 or N3PixelFormat.Dxt5;

    /// <summary>Pixel byte size of the uncompressed formats (CN3Texture::Load).</summary>
    public static int GetPixelSize(N3PixelFormat format) => format switch
    {
        N3PixelFormat.A1R5G5B5 or N3PixelFormat.A4R4G4B4 or N3PixelFormat.R5G6B5 => 2,
        N3PixelFormat.R8G8B8 => 3,
        N3PixelFormat.A8R8G8B8 or N3PixelFormat.X8R8G8B8 => 4,
        _ => throw new InvalidDataException($"Not a supported uncompressed texture format: {format}"),
    };

    /// <summary>
    /// GetTextureSize (N3Texture.cpp): DXT1 is width*height/2; everything else
    /// width*height with the low four bits masked off — kept verbatim.
    /// </summary>
    public static int GetLevelSize(int width, int height, N3PixelFormat format)
    {
        int size = width * height;
        if (format == N3PixelFormat.Dxt1)
            return size / 2;

        return size & ~0xF;
    }

    /// <summary>
    /// The mip level count CN3Texture::Create produces: one level per halving
    /// while BOTH dimensions stay >= 4.
    /// </summary>
    public static int CountMipLevels(int width, int height)
    {
        int count = 0;
        for (int w = width, h = height; w >= 4 && h >= 4; w /= 2, h /= 2)
            count++;

        return count;
    }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        // __DXT_HEADER: szID[4], nWidth, nHeight, Format, bMipMap.
        byte[] id = reader.ReadBytes(4);
        Width = reader.ReadInt32();
        Height = reader.ReadInt32();
        Format = (N3PixelFormat)reader.ReadUInt32();
        HasMipMaps = reader.ReadInt32() != 0;

        if (id.Length != 4 || id[0] != (byte)'N' || id[1] != (byte)'T' || id[2] != (byte)'F' || id[3] < 3)
        {
            // The C++ only logs a warning for old formats and reads on.
        }

        ContainerVersion = id.Length == 4 ? id[3] : (byte)0;
        if (ContainerVersion == 7)
        {
            // Version 7 is WinCrypt-encrypted; without the key file the C++
            // fails the load too.
            throw new NotSupportedException("Encrypted NTF7 textures are not supported");
        }

        if (Width <= 1 || Height <= 1 || Format == N3PixelFormat.Unknown)
            throw new InvalidDataException($"Invalid texture header {Width}x{Height} format {Format}");

        MipLevels.Clear();
        int levelCount = HasMipMaps ? CountMipLevels(Width, Height) : 1;

        if (IsCompressed(Format))
        {
            if (levelCount > 1)
            {
                int w = Width, h = Height;
                for (int i = 0; i < levelCount; i++, w /= 2, h /= 2)
                    MipLevels.Add(ReadExactly(reader, GetLevelSize(w, h, Format)));

                // Skip the 16-bpp fallback pyramid (half size down to 4x4).
                for (int fw = Width / 2, fh = Height / 2; fw >= 4 && fh >= 4; fw /= 2, fh /= 2)
                    reader.BaseStream.Seek(fw * fh * 2, SeekOrigin.Current);
            }
            else
            {
                MipLevels.Add(ReadExactly(reader, GetLevelSize(Width, Height, Format)));

                // C++ quirk kept as-is: the writer emits a half-size 16-bpp
                // fallback of width*height/2 bytes, but the reader only skips
                // width*height/4 — standalone loads never notice.
                reader.BaseStream.Seek(Width * Height / 4, SeekOrigin.Current);
                if (Width >= 1024)
                    reader.BaseStream.Seek(256 * 256 * 2, SeekOrigin.Current);
            }
        }
        else
        {
            int pixelSize = GetPixelSize(Format);

            if (levelCount > 1)
            {
                int w = Width, h = Height;
                for (int i = 0; i < levelCount; i++, w /= 2, h /= 2)
                    MipLevels.Add(ReadExactly(reader, w * h * pixelSize));
            }
            else
            {
                MipLevels.Add(ReadExactly(reader, Width * Height * pixelSize));

                if (Width >= 512 && Height >= 512)
                    reader.BaseStream.Seek(256 * 256 * 2, SeekOrigin.Current); // voodoo-card extra
            }
        }
    }

    /// <summary>
    /// Writes the container exactly as CN3Texture::Save lays it out (fallback
    /// pyramid as zero bytes) — used by the round-trip fixtures.
    /// </summary>
    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write((byte)'N');
        writer.Write((byte)'T');
        writer.Write((byte)'F');
        writer.Write((byte)3);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write((uint)Format);
        writer.Write(HasMipMaps ? 1 : 0);

        foreach (byte[] level in MipLevels)
            writer.Write(level);

        if (IsCompressed(Format))
        {
            if (HasMipMaps)
            {
                for (int fw = Width / 2, fh = Height / 2; fw >= 4 && fh >= 4; fw /= 2, fh /= 2)
                    writer.Write(new byte[fw * fh * 2]);
            }
            else
            {
                writer.Write(new byte[Width * Height / 2]);
                if (Width >= 1024)
                    writer.Write(new byte[256 * 256 * 2]);
            }
        }
        else if (!HasMipMaps && Width >= 512 && Height >= 512)
        {
            writer.Write(new byte[256 * 256 * 2]);
        }
    }

    /// <summary>Test/tool constructor helper.</summary>
    public void Initialize(int width, int height, N3PixelFormat format, bool mipMaps)
    {
        Width = width;
        Height = height;
        Format = format;
        HasMipMaps = mipMaps;
        ContainerVersion = 3;
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        byte[] data = reader.ReadBytes(count);
        if (data.Length != count)
            throw new EndOfStreamException($"Texture level is truncated ({data.Length}/{count} bytes)");

        return data;
    }
}
