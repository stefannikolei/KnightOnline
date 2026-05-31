using OpenKO.IO;
using OpenKO.N3;
using Xunit;

namespace OpenKO.Tests;

public class N3TextureTests
{
    [Theory]
    [InlineData(256, 256, true, 7)]   // 256,128,64,32,16,8,4 => 7 levels
    [InlineData(256, 256, false, 1)]  // no mipmaps => single level
    [InlineData(8, 8, true, 2)]       // 8,4 => 2 levels
    [InlineData(4, 4, true, 1)]       // 4 => 1 level
    [InlineData(2, 2, true, 0)]       // below 4 => 0 levels
    public void ComputeMipCountMatchesOriginal(int w, int h, bool mip, int expected)
    {
        Assert.Equal(expected, N3Texture.ComputeMipCount(w, h, mip));
    }

    [Theory]
    [InlineData(N3PixelFormat.Dxt1, 16, 16, 128)]      // w*h/2
    [InlineData(N3PixelFormat.Dxt5, 16, 16, 256)]      // w*h
    [InlineData(N3PixelFormat.A1R5G5B5, 16, 16, 512)]  // w*h*2
    [InlineData(N3PixelFormat.A8R8G8B8, 16, 16, 1024)] // w*h*4
    [InlineData(N3PixelFormat.R8G8B8, 16, 16, 768)]    // w*h*3
    public void LevelSizeMatchesFormat(N3PixelFormat fmt, int w, int h, int expected)
    {
        Assert.Equal(expected, fmt.LevelSize(w, h));
    }

    [Fact]
    public void DxtFormatsAreClassifiedAsCompressed()
    {
        Assert.True(N3PixelFormat.Dxt1.IsCompressed());
        Assert.True(N3PixelFormat.Dxt5.IsCompressed());
        Assert.False(N3PixelFormat.A8R8G8B8.IsCompressed());
    }

    [Fact]
    public void SaveAndLoadRoundTripsUncompressedMipChain()
    {
        // 8x8 A8R8G8B8 with mipmaps => levels 8x8 (256 bytes) and 4x4 (64 bytes).
        var level0 = new byte[8 * 8 * 4];
        var level1 = new byte[4 * 4 * 4];
        for (int i = 0; i < level0.Length; i++) level0[i] = (byte)(i & 0xFF);
        for (int i = 0; i < level1.Length; i++) level1[i] = (byte)(255 - (i & 0xFF));

        var src = new N3Texture();
        src.SetData("brick", 8, 8, N3PixelFormat.A8R8G8B8, new[] { level0, level1 });

        string path = Path.Combine(Path.GetTempPath(), $"openko_tex_{Guid.NewGuid():N}.dxt");
        try
        {
            using (var writer = new FileWriter())
            {
                Assert.True(writer.Create(path));
                Assert.True(src.Save(writer));
            }

            var loaded = new N3Texture();
            using (var reader = new FileReader())
            {
                Assert.True(reader.OpenExisting(path));
                Assert.True(loaded.Load(reader));
            }

            Assert.Equal("brick", loaded.Name);
            Assert.Equal(8, loaded.TextureWidth);
            Assert.Equal(8, loaded.TextureHeight);
            Assert.Equal(N3PixelFormat.A8R8G8B8, loaded.Format);
            Assert.True(loaded.HasMipMap);
            Assert.Equal(2, loaded.MipMapCount);
            Assert.Equal(level0, loaded.Levels[0]);
            Assert.Equal(level1, loaded.Levels[1]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void RejectsNonNtfData()
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(0)); // empty resource name
        bytes.AddRange(new byte[] { (byte)'B', (byte)'A', (byte)'D', 1 });

        var reader = new FileReader();
        reader.OpenFromMemory(bytes.ToArray());
        Assert.False(new N3Texture().Load(reader));
    }
}
