using OpenKO.Client.Configuration;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-11.1 pins: the options.json model + store (Option.ini contract).</summary>
public class GameSettingsTests
{
    [Fact]
    public void Defaults_MatchTheWarFareMainReadDefaults()
    {
        var s = new GameSettings();
        Assert.Equal(1024, s.Width);
        Assert.Equal(768, s.Height);
        Assert.Equal(32, s.ColorDepth);
        Assert.Equal(512, s.ViewDistance);
        Assert.False(s.Fullscreen);
        Assert.True(s.VSync);
        Assert.True(s.Shadows);
        Assert.True(s.BgmEnabled);
        Assert.True(s.SfxEnabled);
        Assert.Equal(48, s.SoundDistance);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(9, 1)]
    public void Normalize_ClampsTextureLod(int raw, int expected)
    {
        var s = new GameSettings { TexLodChr = raw, TexLodShape = raw, TexLodTerrain = raw };
        s.Normalize();
        Assert.Equal(expected, s.TexLodChr);
        Assert.Equal(expected, s.TexLodShape);
        Assert.Equal(expected, s.TexLodTerrain);
    }

    [Theory]
    [InlineData(24, 32)] // unsupported → 32
    [InlineData(16, 16)]
    [InlineData(32, 32)]
    public void Normalize_ColorDepthTo16Or32(int raw, int expected)
    {
        var s = new GameSettings { ColorDepth = raw };
        s.Normalize();
        Assert.Equal(expected, s.ColorDepth);
    }

    [Theory]
    [InlineData(100, 256)]
    [InlineData(300, 300)]
    [InlineData(999, 512)]
    public void Normalize_ClampsViewDistance(int raw, int expected)
    {
        var s = new GameSettings { ViewDistance = raw };
        s.Normalize();
        Assert.Equal(expected, s.ViewDistance);
    }

    [Theory]
    [InlineData(10, 20)]
    [InlineData(30, 30)]
    [InlineData(60, 48)]
    public void Normalize_ClampsSoundDistance(int raw, int expected)
    {
        var s = new GameSettings { SoundDistance = raw };
        s.Normalize();
        Assert.Equal(expected, s.SoundDistance);
    }

    [Theory]
    [InlineData(1024, 768)]
    [InlineData(1280, 1024)]
    [InlineData(1366, 768)]
    [InlineData(1600, 1200)]
    [InlineData(1920, 1080)]
    public void Normalize_DerivesHeightFromKnownWidth(int width, int expectedHeight)
    {
        var s = new GameSettings { Width = width, Height = 1 };
        s.Normalize();
        Assert.Equal(expectedHeight, s.Height);
    }

    [Fact]
    public void Normalize_KeepsHeightForUnknownWidth()
    {
        var s = new GameSettings { Width = 800, Height = 600 };
        s.Normalize();
        Assert.Equal(600, s.Height);
    }

    [Fact]
    public void Normalize_ClampsVolumes()
    {
        var s = new GameSettings { BgmVolume = 2f, SfxVolume = -1f };
        s.Normalize();
        Assert.Equal(1f, s.BgmVolume);
        Assert.Equal(0f, s.SfxVolume);
    }

    [Fact]
    public void Store_RoundTripsThroughFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openko-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = new GameSettings
            {
                Width = 1280, ColorDepth = 16, Fullscreen = true, VSync = false,
                BgmEnabled = false, SfxEnabled = true, BgmVolume = 0.4f, SfxVolume = 0.7f,
                TexLodChr = 1, Shadows = false, ViewDistance = 400, SoundDistance = 30,
            };
            GameSettingsStore.Save(written, dir);

            Assert.True(File.Exists(GameSettingsStore.PathIn(dir)));

            GameSettings read = GameSettingsStore.Load(dir);
            Assert.Equal(1280, read.Width);
            Assert.Equal(1024, read.Height); // derived from 1280
            Assert.Equal(16, read.ColorDepth);
            Assert.True(read.Fullscreen);
            Assert.False(read.VSync);
            Assert.False(read.BgmEnabled);
            Assert.True(read.SfxEnabled);
            Assert.Equal(0.4f, read.BgmVolume);
            Assert.Equal(0.7f, read.SfxVolume);
            Assert.Equal(1, read.TexLodChr);
            Assert.False(read.Shadows);
            Assert.Equal(400, read.ViewDistance);
            Assert.Equal(30, read.SoundDistance);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openko-settings-missing-" + Guid.NewGuid().ToString("N"));
        GameSettings s = GameSettingsStore.Load(dir);
        Assert.Equal(1024, s.Width);
        Assert.True(s.VSync);
    }
}
