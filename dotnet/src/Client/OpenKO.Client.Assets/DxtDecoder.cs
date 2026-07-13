namespace OpenKO.Client.Assets;

/// <summary>
/// CPU decoder for the DXT1/2/3/4/5 (BC1–BC3) block formats to 32-bit RGBA —
/// used by the AssetDump tool and the loader tests; the renderer uploads the
/// raw blocks to the GPU instead. DXT2/4 carry premultiplied alpha but decode
/// with the same block layout as DXT3/5; no un-premultiply is applied (the
/// C++ never does either — D3D treated them identically outside blending).
/// </summary>
public static class DxtDecoder
{
    /// <summary>Decodes one level to tightly packed RGBA8 (R first in memory).</summary>
    public static byte[] Decode(N3PixelFormat format, ReadOnlySpan<byte> data, int width, int height)
    {
        if (!N3Texture.IsCompressed(format))
            throw new ArgumentException($"Not a DXT format: {format}", nameof(format));

        var rgba = new byte[width * height * 4];
        int blocksX = Math.Max(1, (width + 3) / 4);
        int blocksY = Math.Max(1, (height + 3) / 4);
        int blockSize = format == N3PixelFormat.Dxt1 ? 8 : 16;

        Span<byte> pixels = stackalloc byte[4 * 16]; // 4x4 RGBA block

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int offset = (by * blocksX + bx) * blockSize;
                if (offset + blockSize > data.Length)
                    throw new ArgumentException("DXT data too short for the given dimensions");

                ReadOnlySpan<byte> block = data.Slice(offset, blockSize);
                switch (format)
                {
                    case N3PixelFormat.Dxt1:
                        DecodeColorBlock(block, pixels, opaqueOnly: false);
                        break;
                    case N3PixelFormat.Dxt2:
                    case N3PixelFormat.Dxt3:
                        DecodeColorBlock(block[8..], pixels, opaqueOnly: true);
                        DecodeExplicitAlpha(block[..8], pixels);
                        break;
                    case N3PixelFormat.Dxt4:
                    case N3PixelFormat.Dxt5:
                        DecodeColorBlock(block[8..], pixels, opaqueOnly: true);
                        DecodeInterpolatedAlpha(block[..8], pixels);
                        break;
                }

                // Copy the 4x4 block into the output, clipping at the edges.
                for (int py = 0; py < 4; py++)
                {
                    int y = by * 4 + py;
                    if (y >= height)
                        break;

                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px;
                        if (x >= width)
                            break;

                        int src = (py * 4 + px) * 4;
                        int dst = (y * width + x) * 4;
                        pixels.Slice(src, 4).CopyTo(rgba.AsSpan(dst, 4));
                    }
                }
            }
        }

        return rgba;
    }

    /// <summary>
    /// Decodes the 8-byte color half of a block. For DXT1 (opaqueOnly=false)
    /// color0&lt;=color1 selects the 3-color + 1-bit-alpha mode; DXT3/5 color
    /// blocks always use the 4-color mode.
    /// </summary>
    private static void DecodeColorBlock(ReadOnlySpan<byte> block, Span<byte> pixels, bool opaqueOnly)
    {
        ushort c0 = (ushort)(block[0] | (block[1] << 8));
        ushort c1 = (ushort)(block[2] | (block[3] << 8));

        Span<byte> palette = stackalloc byte[4 * 4];
        Expand565(c0, palette[..4]);
        Expand565(c1, palette.Slice(4, 4));

        if (opaqueOnly || c0 > c1)
        {
            for (int i = 0; i < 3; i++)
            {
                palette[8 + i] = (byte)((2 * palette[i] + palette[4 + i] + 1) / 3);
                palette[12 + i] = (byte)((palette[i] + 2 * palette[4 + i] + 1) / 3);
            }

            palette[11] = 255;
            palette[15] = 255;
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                palette[8 + i] = (byte)((palette[i] + palette[4 + i]) / 2);
                palette[12 + i] = 0;
            }

            palette[11] = 255;
            palette[15] = 0; // transparent black
        }

        for (int py = 0; py < 4; py++)
        {
            byte row = block[4 + py];
            for (int px = 0; px < 4; px++)
            {
                int index = (row >> (px * 2)) & 0x3;
                palette.Slice(index * 4, 4).CopyTo(pixels.Slice((py * 4 + px) * 4, 4));
            }
        }
    }

    private static void Expand565(ushort color, Span<byte> rgba)
    {
        int r = (color >> 11) & 0x1F;
        int g = (color >> 5) & 0x3F;
        int b = color & 0x1F;
        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }

    /// <summary>DXT2/3: 4 bits of explicit alpha per pixel, row-major LE.</summary>
    private static void DecodeExplicitAlpha(ReadOnlySpan<byte> alpha, Span<byte> pixels)
    {
        for (int py = 0; py < 4; py++)
        {
            ushort row = (ushort)(alpha[py * 2] | (alpha[py * 2 + 1] << 8));
            for (int px = 0; px < 4; px++)
            {
                int a4 = (row >> (px * 4)) & 0xF;
                pixels[(py * 4 + px) * 4 + 3] = (byte)((a4 << 4) | a4);
            }
        }
    }

    /// <summary>DXT4/5: two alpha endpoints + 3-bit indices (8- or 6-step ramp).</summary>
    private static void DecodeInterpolatedAlpha(ReadOnlySpan<byte> alpha, Span<byte> pixels)
    {
        byte a0 = alpha[0];
        byte a1 = alpha[1];

        Span<byte> ramp = stackalloc byte[8];
        ramp[0] = a0;
        ramp[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i < 7; i++)
                ramp[1 + i] = (byte)(((7 - i) * a0 + i * a1 + 3) / 7);
        }
        else
        {
            for (int i = 1; i < 5; i++)
                ramp[1 + i] = (byte)(((5 - i) * a0 + i * a1 + 2) / 5);

            ramp[6] = 0;
            ramp[7] = 255;
        }

        // 48 bits of 3-bit indices, little-endian across the 6 bytes.
        ulong bits = 0;
        for (int i = 0; i < 6; i++)
            bits |= (ulong)alpha[2 + i] << (i * 8);

        for (int p = 0; p < 16; p++)
            pixels[p * 4 + 3] = ramp[(int)((bits >> (p * 3)) & 0x7)];
    }

    /// <summary>
    /// Decodes an uncompressed level (the non-DXT container formats) to RGBA8.
    /// </summary>
    public static byte[] DecodeUncompressed(N3PixelFormat format, ReadOnlySpan<byte> data, int width, int height)
    {
        int pixelSize = N3Texture.GetPixelSize(format);
        if (data.Length < width * height * pixelSize)
            throw new ArgumentException("Pixel data too short for the given dimensions");

        var rgba = new byte[width * height * 4];
        for (int p = 0; p < width * height; p++)
        {
            int src = p * pixelSize;
            int dst = p * 4;
            switch (format)
            {
                case N3PixelFormat.A8R8G8B8:
                case N3PixelFormat.X8R8G8B8:
                {
                    rgba[dst] = data[src + 2];
                    rgba[dst + 1] = data[src + 1];
                    rgba[dst + 2] = data[src];
                    rgba[dst + 3] = format == N3PixelFormat.A8R8G8B8 ? data[src + 3] : (byte)255;
                    break;
                }

                case N3PixelFormat.R8G8B8:
                {
                    rgba[dst] = data[src + 2];
                    rgba[dst + 1] = data[src + 1];
                    rgba[dst + 2] = data[src];
                    rgba[dst + 3] = 255;
                    break;
                }

                case N3PixelFormat.R5G6B5:
                {
                    ushort v = (ushort)(data[src] | (data[src + 1] << 8));
                    Expand565(v, rgba.AsSpan(dst, 4));
                    break;
                }

                case N3PixelFormat.A1R5G5B5:
                {
                    ushort v = (ushort)(data[src] | (data[src + 1] << 8));
                    int r = (v >> 10) & 0x1F;
                    int g = (v >> 5) & 0x1F;
                    int b = v & 0x1F;
                    rgba[dst] = (byte)((r << 3) | (r >> 2));
                    rgba[dst + 1] = (byte)((g << 3) | (g >> 2));
                    rgba[dst + 2] = (byte)((b << 3) | (b >> 2));
                    rgba[dst + 3] = (v & 0x8000) != 0 ? (byte)255 : (byte)0;
                    break;
                }

                case N3PixelFormat.A4R4G4B4:
                {
                    ushort v = (ushort)(data[src] | (data[src + 1] << 8));
                    int a = (v >> 12) & 0xF;
                    int r = (v >> 8) & 0xF;
                    int g = (v >> 4) & 0xF;
                    int b = v & 0xF;
                    rgba[dst] = (byte)((r << 4) | r);
                    rgba[dst + 1] = (byte)((g << 4) | g);
                    rgba[dst + 2] = (byte)((b << 4) | b);
                    rgba[dst + 3] = (byte)((a << 4) | a);
                    break;
                }

                default:
                    throw new ArgumentException($"Unsupported format: {format}", nameof(format));
            }
        }

        return rgba;
    }
}
