using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenKO.Client.Configuration;

namespace OpenKO.Client.Settings;

/// <summary>
/// The bindable model behind the settings window — a straight projection of
/// <see cref="GameSettings"/> onto the dialog controls (Option.exe's COptionDlg).
/// It loads from the given <see cref="GameSettings"/> and writes an
/// <see cref="GameSettings"/> back to <c>options.json</c> in <see cref="_directory"/>
/// on <see cref="Apply"/>.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly string _directory;

    public SettingsViewModel(GameSettings settings, string directory)
    {
        _directory = directory;

        // Build the resolution list from the standard set, adding the loaded one first
        // if it is a non-standard size so a custom resolution round-trips unchanged.
        var list = new List<Resolution>();
        var loaded = new Resolution(settings.Width, settings.Height);
        bool standard = false;
        foreach ((int w, int h) in GameSettings.StandardResolutions)
        {
            list.Add(new Resolution(w, h));
            if (w == settings.Width && h == settings.Height)
                standard = true;
        }

        if (!standard)
            list.Insert(0, loaded);

        Resolutions = list;
        _selectedResolution = list.Find(r => r.Width == settings.Width && r.Height == settings.Height) ?? list[0];

        _selectedColorDepth = settings.ColorDepth == 16 ? 16 : 32;
        _fullscreen = settings.Fullscreen;
        _vsync = settings.VSync;
        _shadows = settings.Shadows;
        _bgmEnabled = settings.BgmEnabled;
        _sfxEnabled = settings.SfxEnabled;
        _windowCursor = settings.WindowCursor;
        _viewDistance = settings.ViewDistance;
        _soundDistance = settings.SoundDistance;
        _bgmVolume = settings.BgmVolume;
        _sfxVolume = settings.SfxVolume;
        _texLodChrLow = settings.TexLodChr == 1;
        _texLodShapeLow = settings.TexLodShape == 1;
        _texLodTerrainLow = settings.TexLodTerrain == 1;
    }

    /// <summary>A selectable screen resolution (Option.exe's mode list / fallback list).</summary>
    public sealed record Resolution(int Width, int Height)
    {
        public override string ToString() => $"{Width} x {Height}";
    }

    public IReadOnlyList<Resolution> Resolutions { get; }

    public IReadOnlyList<int> ColorDepths { get; } = [16, 32];

    private Resolution _selectedResolution;
    public Resolution SelectedResolution
    {
        get => _selectedResolution;
        set => Set(ref _selectedResolution, value);
    }

    private int _selectedColorDepth;
    public int SelectedColorDepth
    {
        get => _selectedColorDepth;
        set => Set(ref _selectedColorDepth, value);
    }

    private bool _fullscreen;
    public bool Fullscreen { get => _fullscreen; set => Set(ref _fullscreen, value); }

    private bool _vsync;
    public bool VSync { get => _vsync; set => Set(ref _vsync, value); }

    private bool _shadows;
    public bool Shadows { get => _shadows; set => Set(ref _shadows, value); }

    private bool _bgmEnabled;
    public bool BgmEnabled { get => _bgmEnabled; set => Set(ref _bgmEnabled, value); }

    private bool _sfxEnabled;
    public bool SfxEnabled { get => _sfxEnabled; set => Set(ref _sfxEnabled, value); }

    private bool _windowCursor;
    public bool WindowCursor { get => _windowCursor; set => Set(ref _windowCursor, value); }

    private int _viewDistance;
    public int ViewDistance { get => _viewDistance; set => Set(ref _viewDistance, value); }

    private int _soundDistance;
    public int SoundDistance { get => _soundDistance; set => Set(ref _soundDistance, value); }

    // Sliders bind to double; GameSettings stores float 0..1.
    private double _bgmVolume;
    public double BgmVolume { get => _bgmVolume; set => Set(ref _bgmVolume, value); }

    private double _sfxVolume;
    public double SfxVolume { get => _sfxVolume; set => Set(ref _sfxVolume, value); }

    private bool _texLodChrLow;
    public bool TexLodChrLow { get => _texLodChrLow; set => Set(ref _texLodChrLow, value); }

    private bool _texLodShapeLow;
    public bool TexLodShapeLow { get => _texLodShapeLow; set => Set(ref _texLodShapeLow, value); }

    private bool _texLodTerrainLow;
    public bool TexLodTerrainLow { get => _texLodTerrainLow; set => Set(ref _texLodTerrainLow, value); }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>Projects the current control values back into a normalised <see cref="GameSettings"/>.</summary>
    public GameSettings ToSettings()
    {
        var s = new GameSettings
        {
            Width = SelectedResolution.Width,
            Height = SelectedResolution.Height,
            ColorDepth = SelectedColorDepth,
            Fullscreen = Fullscreen,
            VSync = VSync,
            Shadows = Shadows,
            BgmEnabled = BgmEnabled,
            SfxEnabled = SfxEnabled,
            WindowCursor = WindowCursor,
            ViewDistance = ViewDistance,
            SoundDistance = SoundDistance,
            BgmVolume = (float)BgmVolume,
            SfxVolume = (float)SfxVolume,
            TexLodChr = TexLodChrLow ? 1 : 0,
            TexLodShape = TexLodShapeLow ? 1 : 0,
            TexLodTerrain = TexLodTerrainLow ? 1 : 0,
        };
        s.Normalize();
        return s;
    }

    /// <summary>Writes the current settings to <c>options.json</c> (OK / Apply).</summary>
    public void Apply()
    {
        GameSettings s = ToSettings();
        GameSettingsStore.Save(s, _directory);
        Status = $"Saved to {GameSettingsStore.PathIn(_directory)}";
    }

    // ---- INotifyPropertyChanged ---------------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
