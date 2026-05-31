namespace OpenKO.N3;

/// <summary>How a texture's pixels should be handed to a GPU API (backend-neutral).</summary>
public enum GpuUploadKind
{
    /// <summary>Block-compressed (S3TC/DXT); upload whole levels verbatim.</summary>
    Compressed,

    /// <summary>Uncompressed; upload as rows of pixels of <see cref="GpuTextureLayout.BytesPerPixel"/>.</summary>
    Uncompressed,
}

/// <summary>
/// Backend-neutral description of how an <see cref="N3PixelFormat"/> maps onto a GPU texture upload.
/// This is the part of the render path that can be unit-tested without a GPU; the actual OpenGL
/// (Silk.NET) translation lives in the client and consumes this.
///
/// The original client relied on DirectX 9 consuming D3DFMT_* directly. On OpenGL we map:
///  - DXT1/3/5  -> the S3TC compressed internal formats (block-based, 4x4 texel blocks),
///  - A8R8G8B8/X8R8G8B8 -> BGRA8 (D3D stores these byte order B,G,R,A),
///  - R8G8B8    -> BGR8,
///  - A1R5G5B5 / A4R4G4B4 -> 16-bit packed formats.
/// </summary>
public readonly struct GpuTextureLayout
{
    public GpuUploadKind Kind { get; }

    /// <summary>For uncompressed formats, bytes per pixel; 0 for compressed.</summary>
    public int BytesPerPixel { get; }

    /// <summary>
    /// For compressed formats, the byte size of one 4x4 block (DXT1 = 8, DXT3/5 = 16); 0 otherwise.
    /// </summary>
    public int BlockBytes { get; }

    /// <summary>True if DXT — i.e. <see cref="Kind"/> is <see cref="GpuUploadKind.Compressed"/>.</summary>
    public bool IsCompressed => Kind == GpuUploadKind.Compressed;

    private GpuTextureLayout(GpuUploadKind kind, int bytesPerPixel, int blockBytes)
    {
        Kind = kind;
        BytesPerPixel = bytesPerPixel;
        BlockBytes = blockBytes;
    }

    public static GpuTextureLayout For(N3PixelFormat format) => format switch
    {
        N3PixelFormat.Dxt1 => new GpuTextureLayout(GpuUploadKind.Compressed, 0, 8),
        N3PixelFormat.Dxt2 or N3PixelFormat.Dxt3 => new GpuTextureLayout(GpuUploadKind.Compressed, 0, 16),
        N3PixelFormat.Dxt4 or N3PixelFormat.Dxt5 => new GpuTextureLayout(GpuUploadKind.Compressed, 0, 16),
        N3PixelFormat.A8R8G8B8 or N3PixelFormat.X8R8G8B8 => new GpuTextureLayout(GpuUploadKind.Uncompressed, 4, 0),
        N3PixelFormat.R8G8B8 => new GpuTextureLayout(GpuUploadKind.Uncompressed, 3, 0),
        N3PixelFormat.A1R5G5B5 or N3PixelFormat.A4R4G4B4 => new GpuTextureLayout(GpuUploadKind.Uncompressed, 2, 0),
        _ => throw new NotSupportedException($"Unsupported N3 pixel format for GPU upload: {format}"),
    };

    /// <summary>
    /// Expected byte size of one mip level at the given dimensions, computed from this layout.
    /// For compressed formats this rounds up to whole 4x4 blocks (DXT requirement); for uncompressed
    /// it is width*height*bpp. Should agree with <see cref="N3PixelFormatExtensions.LevelSize"/> for
    /// dimensions that are multiples of 4.
    /// </summary>
    public int LevelSize(int width, int height)
    {
        if (IsCompressed)
        {
            int blocksX = Math.Max(1, (width + 3) / 4);
            int blocksY = Math.Max(1, (height + 3) / 4);
            return blocksX * blocksY * BlockBytes;
        }

        return width * height * BytesPerPixel;
    }
}
