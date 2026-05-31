using OpenKO.N3;
using Xunit;

namespace OpenKO.Tests;

public class GpuTextureLayoutTests
{
    [Theory]
    [InlineData(N3PixelFormat.Dxt1, 8)]
    [InlineData(N3PixelFormat.Dxt3, 16)]
    [InlineData(N3PixelFormat.Dxt5, 16)]
    public void CompressedFormatsHaveBlockBytes(N3PixelFormat fmt, int blockBytes)
    {
        var layout = GpuTextureLayout.For(fmt);
        Assert.True(layout.IsCompressed);
        Assert.Equal(blockBytes, layout.BlockBytes);
        Assert.Equal(0, layout.BytesPerPixel);
    }

    [Theory]
    [InlineData(N3PixelFormat.A8R8G8B8, 4)]
    [InlineData(N3PixelFormat.X8R8G8B8, 4)]
    [InlineData(N3PixelFormat.R8G8B8, 3)]
    [InlineData(N3PixelFormat.A1R5G5B5, 2)]
    [InlineData(N3PixelFormat.A4R4G4B4, 2)]
    public void UncompressedFormatsHaveBytesPerPixel(N3PixelFormat fmt, int bpp)
    {
        var layout = GpuTextureLayout.For(fmt);
        Assert.False(layout.IsCompressed);
        Assert.Equal(bpp, layout.BytesPerPixel);
    }

    [Fact]
    public void UnknownFormatThrows()
    {
        Assert.Throws<NotSupportedException>(() => GpuTextureLayout.For(N3PixelFormat.Unknown));
    }

    [Theory]
    [InlineData(N3PixelFormat.Dxt1, 16, 16, 128)]   // 4x4 blocks * 8
    [InlineData(N3PixelFormat.Dxt5, 16, 16, 256)]   // 4x4 blocks * 16
    [InlineData(N3PixelFormat.A8R8G8B8, 16, 16, 1024)]
    public void LevelSizeAgreesWithPixelFormatForAlignedDimensions(N3PixelFormat fmt, int w, int h, int expected)
    {
        // For dimensions that are multiples of 4, the GPU layout size must match the on-disk size.
        Assert.Equal(expected, GpuTextureLayout.For(fmt).LevelSize(w, h));
        Assert.Equal(expected, fmt.LevelSize(w, h));
    }

    [Fact]
    public void CompressedLevelSizeRoundsUpToBlocks()
    {
        // A 2x2 DXT1 image still occupies one full 4x4 block (8 bytes).
        Assert.Equal(8, GpuTextureLayout.For(N3PixelFormat.Dxt1).LevelSize(2, 2));
    }
}
