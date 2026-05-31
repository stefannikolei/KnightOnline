using OpenKO.N3;
using Silk.NET.OpenGL;

namespace OpenKO.Client.Rendering;

/// <summary>
/// Uploads an <see cref="N3Texture"/> (NTF/.dxt) into an OpenGL texture, choosing the GL internal
/// format from the backend-neutral <see cref="GpuTextureLayout"/>.
///
/// Compressed (DXT) levels are uploaded verbatim via glCompressedTexImage2D using the
/// EXT_texture_compression_s3tc internal formats. Uncompressed levels go through glTexImage2D;
/// note that D3D's A8R8G8B8/X8R8G8B8 byte order is B,G,R,A on little-endian, hence BGRA.
/// </summary>
public sealed class GpuTexture : IDisposable
{
    // S3TC internal formats (EXT_texture_compression_s3tc); not all are in Silk's enum, so use raw values.
    private const int GL_COMPRESSED_RGBA_S3TC_DXT1_EXT = 0x83F1;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT3_EXT = 0x83F2;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT5_EXT = 0x83F3;

    private readonly GL _gl;
    private readonly uint _handle;

    public uint Handle => _handle;

    public unsafe GpuTexture(GL gl, N3Texture texture)
    {
        _gl = gl;
        _handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _handle);

        GpuTextureLayout layout = GpuTextureLayout.For(texture.Format);

        int width = texture.TextureWidth;
        int height = texture.TextureHeight;

        for (int level = 0; level < texture.Levels.Count; level++)
        {
            byte[] data = texture.Levels[level];
            int w = Math.Max(1, width >> level);
            int h = Math.Max(1, height >> level);

            fixed (byte* p = data)
            {
                if (layout.IsCompressed)
                {
                    _gl.CompressedTexImage2D(
                        TextureTarget.Texture2D, level,
                        (InternalFormat)CompressedInternalFormat(texture.Format),
                        (uint)w, (uint)h, 0, (uint)data.Length, p);
                }
                else
                {
                    UploadUncompressed(texture.Format, level, w, h, p);
                }
            }
        }

        int levelCount = texture.Levels.Count;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, levelCount - 1);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)(levelCount > 1 ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear));
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static int CompressedInternalFormat(N3PixelFormat format) => format switch
    {
        N3PixelFormat.Dxt1 => GL_COMPRESSED_RGBA_S3TC_DXT1_EXT,
        N3PixelFormat.Dxt2 or N3PixelFormat.Dxt3 => GL_COMPRESSED_RGBA_S3TC_DXT3_EXT,
        N3PixelFormat.Dxt4 or N3PixelFormat.Dxt5 => GL_COMPRESSED_RGBA_S3TC_DXT5_EXT,
        _ => throw new NotSupportedException($"Not a compressed format: {format}"),
    };

    private unsafe void UploadUncompressed(N3PixelFormat format, int level, int w, int h, byte* p)
    {
        // D3D stores A8R8G8B8 as bytes B,G,R,A -> GL BGRA / UnsignedByte.
        (PixelFormat px, PixelType type, InternalFormat internalFmt) = format switch
        {
            N3PixelFormat.A8R8G8B8 or N3PixelFormat.X8R8G8B8
                => (PixelFormat.Bgra, PixelType.UnsignedByte, InternalFormat.Rgba8),
            N3PixelFormat.R8G8B8
                => (PixelFormat.Bgr, PixelType.UnsignedByte, InternalFormat.Rgb8),
            N3PixelFormat.A1R5G5B5
                => (PixelFormat.Bgra, PixelType.UnsignedShort1555Rev, InternalFormat.Rgb5A1),
            N3PixelFormat.A4R4G4B4
                => (PixelFormat.Bgra, PixelType.UnsignedShort4444Rev, InternalFormat.Rgba4),
            _ => throw new NotSupportedException($"Unsupported uncompressed format: {format}"),
        };

        _gl.TexImage2D(TextureTarget.Texture2D, level, internalFmt, (uint)w, (uint)h, 0, px, type, p);
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose() => _gl.DeleteTexture(_handle);
}
