using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenKO.AssetDump;

/// <summary>
/// Minimal dependency-free PNG writer: 8-bit RGBA, no filtering, zlib via
/// System.IO.Compression. Good enough for asset inspection dumps.
/// </summary>
public static class PngWriter
{
    public static void Write(string path, ReadOnlySpan<byte> rgba, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        WriteChunk(output, "IHDR", ihdr);

        // Raw scanlines, each prefixed with filter byte 0.
        var raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
            rgba.Slice(y * width * 4, width * 4).CopyTo(raw.AsSpan(y * (1 + width * 4) + 1));

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);

        File.WriteAllBytes(path, output.ToArray());
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        for (int i = 0; i < 4; i++)
            header[4 + i] = (byte)type[i];

        output.Write(header);
        output.Write(data);

        uint crc = Crc32(header[4..]);
        crc = Crc32(data, crc ^ 0xFFFFFFFF);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> data, uint seed = 0xFFFFFFFF)
    {
        uint crc = seed;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(crc & 1));
        }

        return crc ^ 0xFFFFFFFF;
    }
}
