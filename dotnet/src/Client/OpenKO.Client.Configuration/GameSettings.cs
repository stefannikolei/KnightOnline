namespace OpenKO.Client.Configuration;

/// <summary>
/// The client graphics/sound settings the settings tool writes and the game reads
/// at startup — the C# model of the C++ <c>Option.ini</c> contract
/// (Client/Option/OptionDlg.cpp + the read/clamp logic in WarFareMain.cpp:44-118).
/// Persisted as <c>options.json</c> next to the executable (see
/// <see cref="GameSettingsStore"/>).
/// <para>
/// Fields whose C++ counterpart the engine port does not apply yet (texture LOD,
/// shadows, view/sound distance, colour depth, window cursor) are kept for the 1:1
/// Option.ini contract and future engine features — they are stored but currently
/// inert; <see cref="KnightOnlineGame"/> only applies resolution/fullscreen/vsync
/// and the sound gates/volume.
/// </para>
/// </summary>
public sealed class GameSettings
{
    // ---- ViewPort (resolution) --------------------------------------------
    /// <summary>ViewPort→Width (screen resolution width). Default 1024.</summary>
    public int Width { get; set; } = 1024;

    /// <summary>ViewPort→Height. Default 768; re-derived from <see cref="Width"/> for known widths.</summary>
    public int Height { get; set; } = 768;

    /// <summary>ViewPort→ColorDepth (16 or 32). Default 32 (WarFareMain re-defaults 16→32). Inert in MonoGame.</summary>
    public int ColorDepth { get; set; } = 32;

    /// <summary>ViewPort→Distance (draw distance, 256..512). Default 512. Inert until the engine gates far clip.</summary>
    public int ViewDistance { get; set; } = 512;

    // ---- Screen ------------------------------------------------------------
    /// <summary>
    /// Fullscreen vs windowed. The C++ stores Screen→WindowMode (true = windowed); this is its
    /// negation. The port defaults to <c>false</c> (windowed), matching the current client and a
    /// safe first run — the original release build defaulted to fullscreen.
    /// </summary>
    public bool Fullscreen { get; set; }

    /// <summary>Screen→VSyncEnabled. Default true.</summary>
    public bool VSync { get; set; } = true;

    // ---- Texture LOD (0 = high, 1 = low) ----------------------------------
    /// <summary>Texture→LOD_Chr (0 high / 1 low). Inert until the engine honours per-texture LOD.</summary>
    public int TexLodChr { get; set; }

    /// <summary>Texture→LOD_Shape (0 high / 1 low). Inert.</summary>
    public int TexLodShape { get; set; }

    /// <summary>Texture→LOD_Terrain (0 high / 1 low). Inert.</summary>
    public int TexLodTerrain { get; set; }

    // ---- Shadow ------------------------------------------------------------
    /// <summary>Shadow→Use. Default true. Inert until the engine renders shadows.</summary>
    public bool Shadows { get; set; } = true;

    // ---- Sound -------------------------------------------------------------
    /// <summary>Sound→Bgm (music on/off). Default true. Applied.</summary>
    public bool BgmEnabled { get; set; } = true;

    /// <summary>Sound→Effect (sfx on/off). Default true. Applied.</summary>
    public bool SfxEnabled { get; set; } = true;

    /// <summary>Sound→Distance (effect sound distance, 20..48). Default 48. Inert until 3D SFX gates by distance.</summary>
    public int SoundDistance { get; set; } = 48;

    /// <summary>
    /// BGM volume 0..1 — a port addition (the original only had on/off). Default 1. Applied.
    /// </summary>
    public float BgmVolume { get; set; } = 1f;

    /// <summary>SFX volume 0..1 — a port addition. Default 1. Applied.</summary>
    public float SfxVolume { get; set; } = 1f;

    // ---- Cursor ------------------------------------------------------------
    /// <summary>Cursor→WindowCursor (OS cursor vs software cursor). Default true. Inert (always OS cursor).</summary>
    public bool WindowCursor { get; set; } = true;

    /// <summary>
    /// Clamp/derive fields exactly like WarFareMain.cpp's read logic: texture LODs to 0/1,
    /// colour depth to 16/32, distances to their ranges, volumes to 0..1, and the height
    /// re-derived from the width for the known resolutions.
    /// </summary>
    public void Normalize()
    {
        TexLodChr = ClampLod(TexLodChr);
        TexLodShape = ClampLod(TexLodShape);
        TexLodTerrain = ClampLod(TexLodTerrain);

        if (ColorDepth != 16 && ColorDepth != 32)
            ColorDepth = 32;

        ViewDistance = Math.Clamp(ViewDistance, 256, 512);
        SoundDistance = Math.Clamp(SoundDistance, 20, 48);
        BgmVolume = Math.Clamp(BgmVolume, 0f, 1f);
        SfxVolume = Math.Clamp(SfxVolume, 0f, 1f);

        Height = HeightForWidth(Width, Height);
    }

    /// <summary>WarFareMain's LOD clamp: negatives → 0, ≥2 → 1.</summary>
    private static int ClampLod(int v) => v < 0 ? 0 : v >= 2 ? 1 : v;

    /// <summary>
    /// The height WarFareMain pairs with a width for the known resolutions
    /// (1024→768, 1280→1024, 1366→768, 1600→1200, 1920→1080); other widths keep the
    /// supplied height.
    /// </summary>
    public static int HeightForWidth(int width, int fallbackHeight) => width switch
    {
        1024 => 768,
        1280 => 1024,
        1366 => 768,
        1600 => 1200,
        1920 => 1080,
        _ => fallbackHeight,
    };

    /// <summary>The standard resolution list the settings tool offers (Option.exe fallback list).</summary>
    public static readonly (int Width, int Height)[] StandardResolutions =
    [
        (1024, 768), (1280, 1024), (1366, 768), (1600, 1200), (1920, 1080),
    ];
}
