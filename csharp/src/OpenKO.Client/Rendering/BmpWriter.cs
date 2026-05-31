using System.Buffers.Binary;

namespace OpenKO.Client.Rendering;

/// <summary>
/// Minimal 32-bit BMP encoder used for headless screenshots (the cross-platform stand-in for the
/// original <c>CaptureScreenAndSaveToFile</c>). BMP is chosen deliberately: it needs no image library
/// and no compression, so it works identically on every platform and in CI.
/// </summary>
internal static class BmpWriter
{
    /// <summary>
    /// Write RGBA pixels (as returned by <c>glReadPixels</c>, i.e. bottom-up rows) to a 32-bit BMP.
    /// BMP also stores rows bottom-up, so the data orientation is preserved; channels are swizzled
    /// from RGBA to the BGRA byte order BMP expects.
    /// </summary>
    public static void WriteRgbaBottomUp(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        const int headerSize = 54; // 14-byte file header + 40-byte info header
        int pixelBytes = width * height * 4;

        var file = new byte[headerSize + pixelBytes];
        Span<byte> b = file;

        // BITMAPFILEHEADER
        b[0] = (byte)'B';
        b[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(2), headerSize + pixelBytes); // total size
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(10), headerSize);             // pixel data offset

        // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(14), 40);        // header size
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(22), height);    // positive => bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(b.Slice(26), 1);         // planes
        BinaryPrimitives.WriteInt16LittleEndian(b.Slice(28), 32);        // bits per pixel
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(34), pixelBytes); // image size

        Span<byte> px = b.Slice(headerSize);
        for (int i = 0; i < width * height; i++)
        {
            int s = i * 4;
            px[s + 0] = rgba[s + 2]; // B
            px[s + 1] = rgba[s + 1]; // G
            px[s + 2] = rgba[s + 0]; // R
            px[s + 3] = rgba[s + 3]; // A
        }

        File.WriteAllBytes(path, file);
    }
}
