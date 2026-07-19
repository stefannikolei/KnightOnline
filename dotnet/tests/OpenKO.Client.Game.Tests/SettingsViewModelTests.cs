using OpenKO.Client.Configuration;
using OpenKO.Client.Settings;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-11.3 pins: the settings-tool ViewModel projects GameSettings both ways.</summary>
public class SettingsViewModelTests
{
    [Fact]
    public void Load_ProjectsAllFields_FromSettings()
    {
        var s = new GameSettings
        {
            Width = 1280, Height = 1024, ColorDepth = 16, Fullscreen = true, VSync = false,
            Shadows = false, BgmEnabled = false, SfxEnabled = true, WindowCursor = false,
            ViewDistance = 400, SoundDistance = 30, BgmVolume = 0.4f, SfxVolume = 0.7f,
            TexLodChr = 1, TexLodShape = 0, TexLodTerrain = 1,
        };

        var vm = new SettingsViewModel(s, "/tmp");

        Assert.Equal(1280, vm.SelectedResolution.Width);
        Assert.Equal(1024, vm.SelectedResolution.Height);
        Assert.Equal(16, vm.SelectedColorDepth);
        Assert.True(vm.Fullscreen);
        Assert.False(vm.VSync);
        Assert.False(vm.Shadows);
        Assert.False(vm.BgmEnabled);
        Assert.True(vm.SfxEnabled);
        Assert.False(vm.WindowCursor);
        Assert.Equal(400, vm.ViewDistance);
        Assert.Equal(30, vm.SoundDistance);
        Assert.Equal(0.4, vm.BgmVolume, 4);
        Assert.Equal(0.7, vm.SfxVolume, 4);
        Assert.True(vm.TexLodChrLow);
        Assert.False(vm.TexLodShapeLow);
        Assert.True(vm.TexLodTerrainLow);
    }

    [Fact]
    public void ToSettings_RoundTrips_AndNormalises()
    {
        var original = new GameSettings
        {
            Width = 1600, Height = 1200, ColorDepth = 32, Fullscreen = false, VSync = true,
            Shadows = true, BgmEnabled = true, SfxEnabled = false, WindowCursor = true,
            ViewDistance = 512, SoundDistance = 48, BgmVolume = 1f, SfxVolume = 0.5f,
            TexLodChr = 0, TexLodShape = 1, TexLodTerrain = 0,
        };

        var vm = new SettingsViewModel(original, "/tmp");
        GameSettings result = vm.ToSettings();

        Assert.Equal(1600, result.Width);
        Assert.Equal(1200, result.Height);
        Assert.Equal(32, result.ColorDepth);
        Assert.False(result.Fullscreen);
        Assert.True(result.VSync);
        Assert.True(result.Shadows);
        Assert.True(result.BgmEnabled);
        Assert.False(result.SfxEnabled);
        Assert.True(result.WindowCursor);
        Assert.Equal(512, result.ViewDistance);
        Assert.Equal(48, result.SoundDistance);
        Assert.Equal(1f, result.BgmVolume, 4);
        Assert.Equal(0.5f, result.SfxVolume, 4);
        Assert.Equal(0, result.TexLodChr);
        Assert.Equal(1, result.TexLodShape);
        Assert.Equal(0, result.TexLodTerrain);
    }

    [Fact]
    public void Resolutions_IncludeCustomSize_WhenNonStandard()
    {
        var s = new GameSettings { Width = 800, Height = 600 };
        var vm = new SettingsViewModel(s, "/tmp");

        // The non-standard 800x600 is kept (prepended) so it round-trips unchanged.
        Assert.Contains(vm.Resolutions, r => r.Width == 800 && r.Height == 600);
        Assert.Equal(800, vm.SelectedResolution.Width);
        Assert.Equal(800, vm.ToSettings().Width);
        Assert.Equal(600, vm.ToSettings().Height);
    }

    [Fact]
    public void SelectingResolution_ChangesBothWidthAndHeight()
    {
        var vm = new SettingsViewModel(new GameSettings { Width = 1024, Height = 768 }, "/tmp");
        vm.SelectedResolution = new SettingsViewModel.Resolution(1920, 1080);

        GameSettings result = vm.ToSettings();
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
    }
}
