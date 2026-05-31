using System.Buffers.Binary;
using System.IO.Compression;
using OpenKO.Common;

namespace OpenKO.Client.Rendering;

/// <summary>
/// Minimal 8-bit RGBA PNG encoder for headless screenshots. Uses .NET's <see cref="ZLibStream"/> for
/// the IDAT deflate stream and the project's own <see cref="Crc32"/> for chunk CRCs, so it pulls in no
/// image library and works identically across platforms.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Encode RGBA pixels given bottom-up (as <c>glReadPixels</c> returns them) to a PNG file. PNG
    /// scanlines are top-down, so rows are flipped during encoding.
    /// </summary>
    public static void WriteRgbaBottomUp(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(Signature);

        // IHDR
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type: RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        WriteChunk(fs, "IHDR", ihdr);

        // IDAT: each top-down scanline is prefixed with a filter byte (0 = none).
        int stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int srcRow = (height - 1 - y) * stride; // flip bottom-up -> top-down
            int dstRow = y * (stride + 1);
            raw[dstRow] = 0; // filter type
            rgba.Slice(srcRow, stride).CopyTo(raw.AsSpan(dstRow + 1));
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);

        WriteChunk(fs, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(fs, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        stream.Write(len);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
            typeBytes[i] = (byte)type[i];
        stream.Write(typeBytes);

        if (!data.IsEmpty)
            stream.Write(data);

        // CRC over chunk type + data; PNG uses standard CRC-32 (running register XOR 0xFFFFFFFF).
        uint crc = Crc32.Compute(typeBytes);
        if (!data.IsEmpty)
            crc = Crc32.Compute(data, crc);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }
}
