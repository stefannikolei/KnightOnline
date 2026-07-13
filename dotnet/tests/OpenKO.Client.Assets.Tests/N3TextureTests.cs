using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-5.2 pins: the .dxt (NTF) container reader and the CPU DXT decoder.</summary>
public class N3TextureTests
{
    [Theory]
    [InlineData(256, 256, N3PixelFormat.Dxt1, 32768)]     // w*h/2
    [InlineData(256, 256, N3PixelFormat.Dxt3, 65536)]     // w*h & ~0xF
    [InlineData(256, 256, N3PixelFormat.Dxt5, 65536)]
    [InlineData(4, 4, N3PixelFormat.Dxt1, 8)]
    [InlineData(4, 4, N3PixelFormat.Dxt3, 16)]
    [InlineData(2, 2, N3PixelFormat.Dxt3, 0)]             // & ~0xF quirk: 4 -> 0
    public void GetLevelSize_MatchesCppGetTextureSize(int w, int h, N3PixelFormat format, int expected)
    {
        Assert.Equal(expected, N3Texture.GetLevelSize(w, h, format));
    }

    [Theory]
    [InlineData(256, 256, 7)]  // 256..4
    [InlineData(512, 256, 7)]  // limited by the smaller dimension
    [InlineData(4, 4, 1)]
    [InlineData(2, 2, 0)]      // never >= 4
    [InlineData(1024, 1024, 9)]
    public void CountMipLevels_MatchesCppCreate(int w, int h, int expected)
    {
        Assert.Equal(expected, N3Texture.CountMipLevels(w, h));
    }

    private static N3Texture MakeTexture(int width, int height, N3PixelFormat format, bool mipMaps, byte fill)
    {
        var tex = new N3Texture { Name = "fixture.dxt" };
        tex.Initialize(width, height, format, mipMaps);

        int levels = mipMaps ? N3Texture.CountMipLevels(width, height) : 1;
        bool compressed = N3Texture.IsCompressed(format);
        for (int i = 0, w = width, h = height; i < levels; i++, w /= 2, h /= 2)
        {
            int size = compressed ? N3Texture.GetLevelSize(w, h, format) : w * h * N3Texture.GetPixelSize(format);
            var data = new byte[size];
            Array.Fill(data, (byte)(fill + i));
            tex.MipLevels.Add(data);
        }

        return tex;
    }

    private static (N3Texture Loaded, long Position, long Length) Roundtrip(N3Texture original)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        stream.Position = 0;
        var loaded = new N3Texture();
        using var reader = new BinaryReader(stream);
        loaded.Load(reader);
        return (loaded, stream.Position, stream.Length);
    }

    [Fact]
    public void Dxt1_WithMips_RoundTripsAndConsumesWholeFile()
    {
        N3Texture original = MakeTexture(64, 32, N3PixelFormat.Dxt1, mipMaps: true, fill: 10);
        (N3Texture loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal("fixture.dxt", loaded.Name);
        Assert.Equal(64, loaded.Width);
        Assert.Equal(32, loaded.Height);
        Assert.Equal(N3PixelFormat.Dxt1, loaded.Format);
        Assert.True(loaded.HasMipMaps);
        Assert.Equal(4, loaded.MipLevels.Count); // 64x32, 32x16, 16x8, 8x4
        for (int i = 0; i < loaded.MipLevels.Count; i++)
            Assert.Equal(original.MipLevels[i], loaded.MipLevels[i]);

        // Mip-mapped files are consumed exactly (levels + fallback pyramid).
        Assert.Equal(len, pos);
    }

    [Fact]
    public void Dxt5_NonMip_KeepsTheCppUnderSkipQuirk()
    {
        N3Texture original = MakeTexture(32, 32, N3PixelFormat.Dxt5, mipMaps: false, fill: 20);
        (N3Texture loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(original.MipLevels[0], Assert.Single(loaded.MipLevels));

        // The writer emits a W*H/2-byte 16bpp fallback but the reader only
        // skips W*H/4 — the preserved C++ quirk leaves exactly W*H/4 unread.
        Assert.Equal(32 * 32 / 4, len - pos);
    }

    [Fact]
    public void Uncompressed_WithMips_RoundTrips()
    {
        N3Texture original = MakeTexture(16, 16, N3PixelFormat.A8R8G8B8, mipMaps: true, fill: 30);
        (N3Texture loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(3, loaded.MipLevels.Count); // 16, 8, 4
        Assert.Equal(16 * 16 * 4, loaded.MipLevels[0].Length);
        Assert.Equal(original.MipLevels[2], loaded.MipLevels[2]);
        Assert.Equal(len, pos); // no fallback pyramid for uncompressed formats
    }

    [Fact]
    public void Uncompressed_NonMip_512_SkipsVoodooExtra()
    {
        N3Texture original = MakeTexture(512, 512, N3PixelFormat.R5G6B5, mipMaps: false, fill: 40);
        (N3Texture loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(512 * 512 * 2, Assert.Single(loaded.MipLevels).Length);
        Assert.Equal(len, pos); // 256*256*2 voodoo block written and skipped
    }

    [Fact]
    public void EncryptedNtf7_Throws()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            writer.Write(0);                  // empty name
            writer.Write((byte)'N');
            writer.Write((byte)'T');
            writer.Write((byte)'F');
            writer.Write((byte)7);            // encrypted container
            writer.Write(64);
            writer.Write(64);
            writer.Write((uint)N3PixelFormat.Dxt1);
            writer.Write(0);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.Throws<NotSupportedException>(() => new N3Texture().Load(reader));
    }

    // ---- DXT decoder pins (hand-built blocks) ----

    [Fact]
    public void Dxt1_FourColorMode_DecodesPaletteAndInterpolants()
    {
        // color0 = pure red (0xF800) > color1 = pure blue (0x001F) -> 4-color mode.
        byte[] block =
        [
            0x00, 0xF8, 0x1F, 0x00,
            // Row indices: row0 all c0, row1 all c1, row2 all c2, row3 all c3.
            0b00000000, 0b01010101, 0b10101010, 0b11111111,
        ];

        byte[] rgba = DxtDecoder.Decode(N3PixelFormat.Dxt1, block, 4, 4);

        // Row 0: red.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba[0..4]);
        // Row 1: blue.
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rgba[16..20]);
        // Row 2: (2*c0 + c1 + 1)/3.
        Assert.Equal((byte)((2 * 255 + 0 + 1) / 3), rgba[32]);
        Assert.Equal((byte)((0 + 255 + 1) / 3), rgba[34]);
        // Row 3: (c0 + 2*c1 + 1)/3, opaque.
        Assert.Equal((byte)((255 + 0 + 1) / 3), rgba[48]);
        Assert.Equal(255, rgba[51]);
    }

    [Fact]
    public void Dxt1_ThreeColorMode_Index3IsTransparentBlack()
    {
        // color0 (black) <= color1 (white) -> 3-color + 1-bit alpha mode.
        byte[] block =
        [
            0x00, 0x00, 0xFF, 0xFF,
            0b11111111, 0b10101010, 0b00000000, 0b01010101,
        ];

        byte[] rgba = DxtDecoder.Decode(N3PixelFormat.Dxt1, block, 4, 4);

        // Row 0 (index 3): transparent black.
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, rgba[0..4]);
        // Row 1 (index 2): (c0 + c1)/2 opaque.
        Assert.Equal(new byte[] { 127, 127, 127, 255 }, rgba[16..20]);
        // Row 2 (index 0): black opaque.
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, rgba[32..36]);
    }

    [Fact]
    public void Dxt3_ExplicitAlpha_ExpandsFourBits()
    {
        byte[] block = new byte[16];
        // Alpha row 0: pixels 0..3 = 0x0, 0x5, 0xA, 0xF.
        block[0] = 0x50; // pixel0 low nibble, pixel1 high nibble
        block[1] = 0xFA;
        // Rows 1-3 zero; color: c0=white > c1=black, all indices 0 (white).
        block[8] = 0xFF;
        block[9] = 0xFF;

        byte[] rgba = DxtDecoder.Decode(N3PixelFormat.Dxt3, block, 4, 4);

        Assert.Equal(0x00, rgba[3]);
        Assert.Equal(0x55, rgba[7]);
        Assert.Equal(0xAA, rgba[11]);
        Assert.Equal(0xFF, rgba[15]);
        Assert.Equal(255, rgba[0]); // color still white
    }

    [Fact]
    public void Dxt5_InterpolatedAlpha_EightStepRamp()
    {
        byte[] block = new byte[16];
        block[0] = 140; // a0 > a1 -> 8-step ramp
        block[1] = 0;
        // 3-bit indices: pixel0=0, pixel1=1, pixel2=2, others 0.
        block[2] = 0b10_001_000; // bits 0-2=0, 3-5=1, 6-7=low bits of 2
        block[3] = 0b0000_0000;  // bit 8 = high bit of pixel2's index (0)
        // Color: c0 > c1, indices 0.
        block[8] = 0xFF;
        block[9] = 0xFF;

        byte[] rgba = DxtDecoder.Decode(N3PixelFormat.Dxt5, block, 4, 4);

        Assert.Equal(140, rgba[3]);                          // ramp[0] = a0
        Assert.Equal(0, rgba[7]);                            // ramp[1] = a1
        Assert.Equal((byte)((6 * 140 + 0 + 3) / 7), rgba[11]); // ramp[2]
    }

    [Fact]
    public void Dxt5_SixStepRamp_Uses0And255Extremes()
    {
        byte[] block = new byte[16];
        block[0] = 10;  // a0 <= a1 -> 6-step ramp + 0/255
        block[1] = 20;
        // pixel0=6 (forced 0), pixel1=7 (forced 255).
        block[2] = 0b00_111_110;
        block[8] = 0xFF;
        block[9] = 0xFF;

        byte[] rgba = DxtDecoder.Decode(N3PixelFormat.Dxt5, block, 4, 4);

        Assert.Equal(0, rgba[3]);
        Assert.Equal(255, rgba[7]);
    }

    [Fact]
    public void DecodeUncompressed_ConvertsArgbOrderAndBitDepths()
    {
        // A8R8G8B8: memory order B,G,R,A.
        byte[] argb = [0x11, 0x22, 0x33, 0x44];
        byte[] rgba = DxtDecoder.DecodeUncompressed(N3PixelFormat.A8R8G8B8, argb, 1, 1);
        Assert.Equal(new byte[] { 0x33, 0x22, 0x11, 0x44 }, rgba);

        // X8R8G8B8 forces alpha 255.
        rgba = DxtDecoder.DecodeUncompressed(N3PixelFormat.X8R8G8B8, argb, 1, 1);
        Assert.Equal(255, rgba[3]);

        // A1R5G5B5: 0x8000 = alpha bit only.
        rgba = DxtDecoder.DecodeUncompressed(N3PixelFormat.A1R5G5B5, [0x00, 0x80], 1, 1);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, rgba);

        // A4R4G4B4: 0xF0F0 -> a=F r=0 g=F b=0.
        rgba = DxtDecoder.DecodeUncompressed(N3PixelFormat.A4R4G4B4, [0xF0, 0xF0], 1, 1);
        Assert.Equal(new byte[] { 0x00, 0xFF, 0x00, 0xFF }, rgba);

        // R5G6B5: pure green 0x07E0.
        rgba = DxtDecoder.DecodeUncompressed(N3PixelFormat.R5G6B5, [0xE0, 0x07], 1, 1);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, rgba);
    }
}
