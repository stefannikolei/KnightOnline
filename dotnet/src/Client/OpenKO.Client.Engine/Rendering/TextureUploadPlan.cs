using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Rendering;

/// <summary>
/// Pure upload plan for an <see cref="N3Texture"/>: chooses the MonoGame
/// SurfaceFormat and lays out one data blob per GPU mip level.
///
/// DXT levels upload raw (DesktopGL supports BC1-3; DXT2/4 use the DXT3/5
/// layout — premultiplied alpha, like D3D treated them outside blending).
/// Uncompressed D3D formats are CPU-converted to RGBA (SurfaceFormat.Color).
///
/// KO mip chains stop at 4x4 (CN3Texture::Create), but MonoGame allocates the
/// full chain down to 1x1 — the tail levels are synthesized (DXT: the first
/// block of the smallest real level; Color: its top-left pixel) so far-LOD
/// sampling never reads uninitialized memory.
/// </summary>
public sealed class TextureUploadPlan
{
    public required SurfaceFormat Format { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required bool MipMap { get; init; }

    /// <summary>One blob per GPU level, largest first (full chain when MipMap).</summary>
    public required IReadOnlyList<byte[]> Levels { get; init; }

    public static TextureUploadPlan FromTexture(N3Texture texture)
    {
        return N3Texture.IsCompressed(texture.Format)
            ? FromCompressed(texture)
            : FromUncompressed(texture);
    }

    /// <summary>Expected byte size of one GPU level (for validation/tests).</summary>
    public static int LevelSize(SurfaceFormat format, int width, int height) => format switch
    {
        SurfaceFormat.Dxt1 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8,
        SurfaceFormat.Dxt3 or SurfaceFormat.Dxt5 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
        SurfaceFormat.Color => width * height * 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Full GL mip chain length down to 1x1.</summary>
    public static int FullChainLength(int width, int height)
    {
        int count = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            count++;
        }

        return count;
    }

    private static TextureUploadPlan FromCompressed(N3Texture texture)
    {
        SurfaceFormat format = texture.Format switch
        {
            N3PixelFormat.Dxt1 => SurfaceFormat.Dxt1,
            N3PixelFormat.Dxt2 or N3PixelFormat.Dxt3 => SurfaceFormat.Dxt3,
            N3PixelFormat.Dxt4 or N3PixelFormat.Dxt5 => SurfaceFormat.Dxt5,
            _ => throw new NotSupportedException($"Unexpected compressed format {texture.Format}"),
        };
        int blockSize = format == SurfaceFormat.Dxt1 ? 8 : 16;

        if (!texture.HasMipMaps || texture.MipLevels.Count <= 1)
        {
            return new TextureUploadPlan
            {
                Format = format,
                Width = texture.Width,
                Height = texture.Height,
                MipMap = false,
                Levels = [texture.MipLevels[0]],
            };
        }

        var levels = new List<byte[]>(texture.MipLevels);
        byte[] smallest = levels[^1];
        int fullChain = FullChainLength(texture.Width, texture.Height);
        while (levels.Count < fullChain)
        {
            // Tail below the KO 4x4 cutoff: clone the smallest real level's
            // first block into however many blocks the level needs
            // (non-square tails like 8x2 still span several blocks).
            (int tw, int th) = LevelDims(texture.Width, texture.Height, levels.Count);
            int size = LevelSize(format, tw, th);
            var tail = new byte[size];
            for (int offset = 0; offset < size; offset += blockSize)
                smallest.AsSpan(0, blockSize).CopyTo(tail.AsSpan(offset));
            levels.Add(tail);
        }

        return new TextureUploadPlan
        {
            Format = format,
            Width = texture.Width,
            Height = texture.Height,
            MipMap = true,
            Levels = levels,
        };
    }

    private static TextureUploadPlan FromUncompressed(N3Texture texture)
    {
        var levels = new List<byte[]>(texture.MipLevels.Count);
        int w = texture.Width, h = texture.Height;
        foreach (byte[] raw in texture.MipLevels)
        {
            levels.Add(DxtDecoder.DecodeUncompressed(texture.Format, raw, w, h));
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        bool mipMap = texture.HasMipMaps && texture.MipLevels.Count > 1;
        if (mipMap)
        {
            byte[] smallest = levels[^1];
            int fullChain = FullChainLength(texture.Width, texture.Height);
            while (levels.Count < fullChain)
            {
                // Tail: repeat the smallest level's top-left pixel.
                int index = levels.Count;
                (int tw, int th) = LevelDims(texture.Width, texture.Height, index);
                var tail = new byte[tw * th * 4];
                for (int p = 0; p < tw * th; p++)
                    smallest.AsSpan(0, 4).CopyTo(tail.AsSpan(p * 4));
                levels.Add(tail);
            }
        }

        return new TextureUploadPlan
        {
            Format = SurfaceFormat.Color,
            Width = texture.Width,
            Height = texture.Height,
            MipMap = mipMap,
            Levels = levels,
        };
    }

    public static (int Width, int Height) LevelDims(int width, int height, int level)
    {
        for (int i = 0; i < level; i++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        return (width, height);
    }
}
