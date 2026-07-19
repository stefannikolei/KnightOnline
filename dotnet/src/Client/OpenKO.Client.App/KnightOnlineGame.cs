using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Audio;
using OpenKO.Client.Assets.Zones;
using OpenKO.Client.Engine.Audio;
using OpenKO.Client.Engine.Fx;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Objects;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;
using OpenKO.Client.Engine.Sky;
using OpenKO.Client.Engine.Terrain;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Fx;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client;

/// <summary>
/// The runnable game client: a MonoGame game loop that owns the
/// <see cref="GameStateMachine"/>, drives the client network layer and renders
/// the world plus the real HUD. It launches straight into the interactive
/// login → char-select → in-game flow against the configured server. The debug/CLI
/// modes (offline zone, scripted auto-login, screenshot, text HUD) live in the
/// separate <c>OpenKO.Client.Dev</c> subclass via the protected seam below.
/// </summary>
public class KnightOnlineGame : Microsoft.Xna.Framework.Game
{
    private readonly ClientConfig _config;
    private readonly GraphicsDeviceManager _graphics;

    private SpriteBatch _spriteBatch = null!;
    private FontService _fonts = null!;
    private SoundManager _sound = null!;
    private GameContext _context = null!;
    private NetworkGameClient? _network;
    private KoClientConnection? _connection;
    private CancellationTokenSource? _netCts;

    // Offline / in-world rendering.
    private TerrainRenderer? _terrain;
    private N3Terrain? _terrainData;
    private SkyRenderer? _sky;

    /// <summary>The authoritative game clock (seeded by WIZ_TIME, advanced ~10× real).</summary>
    private readonly GameClock _gameClock = new();

    // Water + weather + FX (slice 9.11d): the already-built renderers wired into
    // the zone scene. _fx is the pure game-side manager (bundles + weather field);
    // _river/_weather/_fxRenderer are the device layers.
    private RiverRenderer? _river;
    private WeatherRenderer? _weather;
    private FxManager? _fx;
    private FxRenderer? _fxRenderer;
    private bool _fxHooksBound;

    // FX tables (slice 10.4): fx.tbl (FXID → .fxb + sound) and the skill table
    // (magic id → self/flying/target FX ids), loaded once via the resolver, plus the
    // resolved FX shape/pmesh caches for mesh-part rendering. All degrade to null
    // (no FX) when the table/asset is absent from the corpus.
    private OpenKO.Client.Assets.Effects.FxSourceTable? _fxTable;
    private OpenKO.Client.Assets.Player.SkillTableSet? _fxSkills;
    private OpenKO.Client.Engine.IO.KoPathResolver? _resolver;
    private readonly Dictionary<string, FxShapeInstance?> _fxShapeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OpenKO.Client.Assets.N3FXPMesh?> _fxPMeshCache =
        new(StringComparer.OrdinalIgnoreCase);

    // BGM (slice 9.11d): the current town/battle theme key and a re-select throttle.
    private string? _currentBgm;
    private float _bgmThrottle;
    private OpenKO.Client.Engine.Objects.ChrRenderer? _character;
    private BasicEffect? _characterEffect;
    private OpenKO.Client.Engine.Objects.ChrAssetCaches? _caches;
    private OpenKO.Client.Engine.Objects.CharacterFactory? _characterFactory;
    private RemotePlayerRenderer? _remotePlayers;
    private OpenKO.Client.Engine.Objects.ZoneObjectRenderer? _zoneObjects;
    private readonly OpenKO.Client.Engine.Scene.FrameTimer _timer = new();
    private readonly OpenKO.Client.Game.World.GameCamera _gameCamera = new();
    private OpenKO.Client.Game.World.PlayerController? _player;
    private System.Numerics.Vector3 _playerPos;
    private float _mapWorldSize;
    private float _cameraYaw;
    private float _moveThrottle;
    private bool _wasMoving;
    private System.Numerics.Matrix4x4 _lastView = System.Numerics.Matrix4x4.Identity;
    private System.Numerics.Matrix4x4 _lastProj = System.Numerics.Matrix4x4.Identity;

    // Input edge machine (CLocalInput) + interactive UI dispatch.
    private readonly OpenKO.Client.Engine.Input.InputState _input = new();
    private readonly bool[] _dikDown = new bool[OpenKO.Client.Engine.Input.InputState.NumKeys];
    private readonly UiManager _ui = new();
    private FrontendUi? _frontend;
    private InGameUi? _inGameUi;
    private TextureCache? _minimapTextures;
    private string? _minimapFile;
    private readonly List<OpenKO.Client.Engine.Ui.MinimapDot> _minimapDots = [];
    private int _prevScrollWheel;

    /// <summary>The manager receiving input/drawing this frame (frontend during login, HUD in-game).</summary>
    private UiManager ActiveUi => _frontend?.Manager ?? _inGameUi?.Manager ?? _ui;

    private string _selection = "none";
    private short? _targetId;
    private string _targetHp = "";
    private double _gameSeconds;

    private readonly List<string> _log = [];

    // ---- Protected seam for OpenKO.Client.Dev -------------------------------
    // The minimal surface the debug/CLI subclass needs to re-add the offline
    // zone, scripted auto-login, screenshot dump and text HUD. Kept as small as
    // possible; the clean game never touches any of this.

    /// <summary>Server endpoint + data path bound from configuration.</summary>
    protected ClientConfig Config => _config;

    /// <summary>The game context (state machine + network + world). Set by the online/offline start.</summary>
    protected GameContext Context
    {
        get => _context;
        set => _context = value;
    }

    /// <summary>The sprite batch (for the debug text HUD).</summary>
    protected SpriteBatch SpriteBatch => _spriteBatch;

    /// <summary>The UI font service (for the debug text HUD).</summary>
    protected FontService Fonts => _fonts;

    /// <summary>The loaded zone terrain, or null before a zone is entered.</summary>
    protected N3Terrain? TerrainData => _terrainData;

    /// <summary>The local player's world position (set by <see cref="BuildZoneScene"/>).</summary>
    protected System.Numerics.Vector3 PlayerPos => _playerPos;

    /// <summary>The zone's world size in units (set by <see cref="BuildZoneScene"/>).</summary>
    protected float MapWorldSize => _mapWorldSize;

    /// <summary>The zone minimap texture file (CUIStateBar::LoadMap); set before <see cref="EnsureInGameUi"/>.</summary>
    protected string? MinimapFile
    {
        get => _minimapFile;
        set => _minimapFile = value;
    }

    /// <summary>True once the real in-game HUD (needs --data) has been built.</summary>
    protected bool HasInGameHud => _inGameUi != null;

    /// <summary>The current pick selection text (debug HUD only).</summary>
    protected string Selection => _selection;

    /// <summary>The current target's HP text (debug HUD only).</summary>
    protected string TargetHp => _targetHp;

    /// <summary>The rolling log lines (debug HUD only).</summary>
    protected IReadOnlyList<string> LogLines => _log;

    public KnightOnlineGame(ClientConfig config)
    {
        _config = config;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1024,
            PreferredBackBufferHeight = 768,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "Knight Online — OpenKO C# Port";
        Window.TextInput += OnTextInput;
    }

    protected override void Initialize()
    {
        base.Initialize();

        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _fonts = FontService.FromBaseDirectory(AppContext.BaseDirectory);
        // Audio + streaming BGM: resolve sound.tbl (id → Snd\*.mp3) and a data-relative
        // file opener so PlayBgm can stream the MP3 tracks (CN3SndMgr CreateStreamObj).
        SoundTable? soundTable = null;
        Func<string, Stream?>? bgmOpener = null;
        if (Config.DataPath is { } dataPath)
        {
            var res = new KoPathResolver(dataPath);
            if (res.Resolve("Data\\sound.tbl") is { } tbl)
                soundTable = SoundTable.LoadFromFile(tbl);
            bgmOpener = fn => res.Resolve(fn) is { } p ? File.OpenRead(p) : null;
        }

        _sound = new SoundManager(new MonoGameAudioBackend(), soundTable, bgmOpener);
        Log(_sound.Backend.IsAvailable ? "Audio: OpenAL device ready." : "Audio: no device (silent).");

        OnStart();
    }

    /// <summary>
    /// Entry point after the device/audio setup. The clean game goes straight
    /// online into the interactive login screen; the dev subclass overrides this
    /// to add the offline zone / scripted auto-login modes.
    /// </summary>
    protected virtual void OnStart() => StartOnline();

    /// <summary>
    /// Loads a zone's terrain/sky and the player character, then places the
    /// player. Shared by the offline demo (centre spawn) and the online flow
    /// (server spawn). Requires --data for the asset corpus.
    /// </summary>
    protected void BuildZoneScene(
        string gtdPath, KoPathResolver resolver, bool useCentreSpawn, System.Numerics.Vector3 spawn)
    {
        var terrain = new N3Terrain();
        terrain.LoadFromFile(gtdPath);
        _terrain = new TerrainRenderer(GraphicsDevice, terrain, resolver, gtdPath);
        _terrainData = terrain;
        // Sun (3-part disk/glow/flare) + moon (phase strip) textures from misc\sky\*
        // (CN3SkyMng::InitToDefaultHardCoding); each is best-effort/null-safe.
        SkyBodyTextures sky = SkyBodyTextures.Load(GraphicsDevice, resolver);
        _sky = new SkyRenderer(GraphicsDevice, sunDisk: sky.SunDisk, sunGlow: sky.SunGlow,
            sunFlare: sky.SunFlare, moonTexture: sky.Moon);
        _mapWorldSize = terrain.MapSize * TerrainVertexBuilder.TileSize;

        float x = useCentreSpawn ? _mapWorldSize * 0.5f : spawn.X;
        float z = useCentreSpawn ? _mapWorldSize * 0.5f : spawn.Z;
        float y = OpenKO.Client.Game.World.TerrainCollision.GetHeight(terrain, x, z);
        if (y <= OpenKO.Client.Game.World.TerrainCollision.OutOfRange + 1f)
            y = useCentreSpawn ? 0f : spawn.Y;

        _playerPos = new System.Numerics.Vector3(x, y, z);
        _player = new PlayerController { Position = _playerPos };

        LoadDemoCharacter(resolver);
        LoadZoneObjects(gtdPath);
        BuildWaterWeatherFx(terrain, resolver);

        // Pick the initial (town) BGM for the newly entered zone (CGameProcMain::InitZone).
        SelectBgm();
    }

    /// <summary>
    /// Construct the water (rivers), the weather field renderer and the FX manager
    /// for the freshly loaded zone, disposing any previous zone's device layers
    /// first (guards against leaks on a zone change). Every asset is best-effort:
    /// missing caustic/snow textures or FX bundles just degrade to nothing.
    /// </summary>
    private void BuildWaterWeatherFx(N3Terrain terrain, KoPathResolver resolver)
    {
        // Dispose the previous zone's device layers before rebuilding.
        _river?.Dispose();
        _weather?.Dispose();
        _fxRenderer?.Dispose();
        _river = null;
        _weather = null;
        _fxRenderer = null;

        try
        {
            // Water: RiverRenderer reads terrain.Rivers itself and loads the caustic
            // frames (misc\river\caustNN.dxt) via the resolver, degrading if absent.
            _river = new RiverRenderer(GraphicsDevice, terrain, resolver);
            if (_river.RiverCount > 0)
                Log($"Water: {_river.RiverCount} river strip(s).");

            // Weather: the device layer for the global rain/snow field. The flake
            // texture (misc\Snow.DXT) is null-safe.
            _weather = new WeatherRenderer(GraphicsDevice)
            {
                SnowTexture = _caches?.Textures.Get("misc\\Snow.dxt"),
            };

            // FX manager + renderer over the client's world roster + asset caches.
            // fx.tbl / skill table drive the bundle + skill-FX resolution (slice 10.4).
            _resolver = resolver;
            EnsureFxTables(resolver);
            var locator = new ClientFxEntityLocator(this);
            var loader = new ClientFxBundleLoader(resolver, _fxTable);
            _fx = new FxManager(locator, loader);
            _fxRenderer = new FxRenderer(GraphicsDevice, ResolveFxTexture, ResolveFxShape);

            BindFxHooks();
        }
        catch (Exception ex)
        {
            Log($"Water/weather/FX init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// FX part frame texture resolver (N3FXPartBase::Load naming:
    /// <c>{TexName}{frame:0000}.dxt</c>). Degrades to null when the asset is absent.
    /// </summary>
    private Texture2D? ResolveFxTexture(OpenKO.Client.Assets.N3FXPartBase part, int frame)
    {
        if (_caches == null || string.IsNullOrEmpty(part.TexName))
            return null;
        return _caches.Textures.Get($"{part.TexName}{frame:0000}.dxt");
    }

    /// <summary>
    /// Bind the online → FX hooks once. The handlers read the live <see cref="_fx"/>
    /// field (not a captured instance), so a zone change that rebuilds the manager
    /// keeps working without re-subscribing or leaking delegates.
    /// Guarded for offline/protocol-only (no server) — the events simply never fire.
    /// </summary>
    private void BindFxHooks()
    {
        if (_fxHooksBound)
            return;
        _fxHooksBound = true;

        InGameState inGame = _context.InGame;

        // WIZ_WEATHER → (re)create the global weather field.
        inGame.WeatherChanged += w => _fx?.SetWeather((WeatherType)w.Type, w.Amount);

        // WIZ_TIME → anchor the authoritative game clock (hour/minute) + moon phase
        // (month*30+day), which drives the day-night sky (CN3SkyMng::SetGameTime).
        inGame.TimeChanged += t =>
        {
            _gameClock.SetFromServer(t.Hour, t.Minute);
            _sky?.SetMoonPhase(SkyBodies.MoonPhaseIndex(t.Month, t.Day));
        };

        // WIZ_MAGIC_PROCESS → the cast/fly/hit FX triggers. The magic → skill-FX
        // resolver reads the loaded skill table (fx.tbl ids); an absent table or
        // unknown id degrades to no FX.
        inGame.MagicReceived += packet =>
        {
            if (_fx is { } fx)
                FxTriggerBinding.Trigger(fx, packet, MagicFxResolve);
        };
    }

    /// <summary>
    /// magicId → the skill's fx.tbl ids (self/flying/target FX + parts) from the
    /// skill table (CMagicSkillMng reads <c>pSkill-&gt;iSelfFX1/iFlyingFX/iTargetFX</c>).
    /// Null when the table is absent or the id is unknown = no FX.
    /// </summary>
    private SkillFxInfo? MagicFxResolve(int magicId)
    {
        if (_fxSkills?.Find((uint)magicId) is not { } row)
            return null;

        return new SkillFxInfo(
            row.SelfFx1, row.SelfPart1, row.SelfFx2, row.SelfPart2,
            row.FlyingFx, row.TargetFx, row.TargetPart);
    }

    /// <summary>
    /// Load the FX effect table (Data\fx.tbl) and the skill table
    /// (Data\skill_magic_main_us.tbl) once, via the resolver. Both are best-effort:
    /// an absent table leaves the field null and the FX simply never resolves.
    /// </summary>
    private void EnsureFxTables(OpenKO.Client.Engine.IO.KoPathResolver resolver)
    {
        if (_fxTable == null)
        {
            try
            {
                string? path = resolver.Resolve("Data\\fx.tbl");
                if (path != null)
                    _fxTable = OpenKO.Client.Assets.Effects.FxSourceTable.LoadFromFile(path);
            }
            catch (Exception ex)
            {
                Log($"fx.tbl load failed: {ex.Message}");
            }
        }

        if (_fxSkills == null)
        {
            try
            {
                string? path = resolver.Resolve("Data\\skill_magic_main_us.tbl");
                if (path != null)
                    _fxSkills = OpenKO.Client.Assets.Player.SkillTableSet.LoadFromFile(path);
            }
            catch (Exception ex)
            {
                Log($"skill table load failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resolve (and cache by shape file name) the FX shape geometry a mesh part
    /// draws — the port of CN3FXPartMesh loading its CN3FXShape + FXPMesh parts.
    /// Null (skip) when the shape/mesh is absent from the corpus.
    /// </summary>
    private FxShapeInstance? ResolveFxShape(OpenKO.Client.Assets.N3FXPartMesh part)
    {
        string shapeFile = part.ShapeFileName;
        if (_resolver == null || _caches == null || string.IsNullOrWhiteSpace(shapeFile))
            return null;

        if (_fxShapeCache.TryGetValue(shapeFile, out FxShapeInstance? cached))
            return cached;

        FxShapeInstance? instance = null;
        try
        {
            string? path = _resolver.Resolve(shapeFile);
            if (path != null)
            {
                var shape = new OpenKO.Client.Assets.N3FXShape();
                shape.LoadFromFile(path);
                instance = new FxShapeInstance(shape, ResolveFxPMesh, ResolveFxShapeTexture);
            }
        }
        catch (Exception ex)
        {
            Log($"FX shape '{shapeFile}' load failed: {ex.Message}");
            instance = null;
        }

        _fxShapeCache[shapeFile] = instance;
        return instance;
    }

    /// <summary>FX shape-part mesh file name → the loaded FXPMesh (cached), or null.</summary>
    private OpenKO.Client.Assets.N3FXPMesh? ResolveFxPMesh(string meshName)
    {
        if (_resolver == null || string.IsNullOrWhiteSpace(meshName))
            return null;

        if (_fxPMeshCache.TryGetValue(meshName, out OpenKO.Client.Assets.N3FXPMesh? cached))
            return cached;

        OpenKO.Client.Assets.N3FXPMesh? mesh = null;
        try
        {
            string? path = _resolver.Resolve(meshName);
            if (path != null)
            {
                mesh = new OpenKO.Client.Assets.N3FXPMesh();
                mesh.LoadFromFile(path);
            }
        }
        catch (Exception ex)
        {
            Log($"FX pmesh '{meshName}' load failed: {ex.Message}");
            mesh = null;
        }

        _fxPMeshCache[meshName] = mesh;
        return mesh;
    }

    /// <summary>FX shape-part animation-frame texture → the corpus texture, or null.</summary>
    private Texture2D? ResolveFxShapeTexture(OpenKO.Client.Assets.N3FXShapePart part, int frame)
    {
        if (_caches == null || frame < 0 || frame >= part.TexNames.Count)
            return null;
        string name = part.TexNames[frame];
        return string.IsNullOrWhiteSpace(name) ? null : _caches.Textures.Get(name);
    }

    /// <summary>
    /// CGameProcMain town/battle BGM choice: pick the track for the local nation +
    /// the current battle state and, on a change, play a matching .wav if the corpus
    /// has one (only WAV is decodable — <see cref="WavAudio"/>), else just log the
    /// selection. MP3 BGM streaming stays deferred (no mpg123 decoder).
    /// </summary>
    private void SelectBgm()
    {
        if (_context == null)
            return;

        var nation = (BgmNation)_context.InGame.World.Local.Nation;
        BgmTrack track = BgmSelector.Select(nation, IsNearHostile(), _context.Spawn.Zone);
        if (track.Name == _currentBgm)
            return;

        _currentBgm = track.Name;
        // Stream the MP3 track (id → Snd\*.mp3 via sound.tbl; fall back to the track name),
        // looping with a short cross-fade (CGameProcMain::UpdateBGM town/battle switch).
        _sound.PlayBgm(_sound.ResolveBgm(track) ?? $"snd\\{track.Name}.mp3", loop: true);
    }

    /// <summary>
    /// UpdateBGM's battle trigger: any NPC within 12 units of the player. The C++
    /// checks hostility (IsHostileTarget); the client does not carry per-NPC
    /// hostility, so proximity to any NPC is used as the battle approximation.
    /// </summary>
    private bool IsNearHostile()
    {
        if (_context == null)
            return false;
        var here = new System.Numerics.Vector3(_playerPos.X, _playerPos.Y, _playerPos.Z);
        foreach (NpcEntity npc in _context.InGame.World.Npcs.Values)
        {
            var pos = new System.Numerics.Vector3(npc.X, npc.Y, npc.Z);
            if (System.Numerics.Vector3.Distance(here, pos) < 12.0f)
                return true;
        }

        return false;
    }


    /// <summary>Loads and prepares the zone's static objects (the sibling .opd of the .gtd).</summary>
    private void LoadZoneObjects(string gtdPath)
    {
        if (_caches == null)
            return;
        string opd = Path.ChangeExtension(gtdPath, ".opd");
        if (!File.Exists(opd))
            return;

        try
        {
            var set = OpenKO.Client.Assets.Zones.ZoneObjectSet.LoadFromFile(opd);
            _zoneObjects = new OpenKO.Client.Engine.Objects.ZoneObjectRenderer(
                set, _caches.Meshes, _caches.Textures);
            Log($"Zone objects: {_zoneObjects.Count}/{set.Objects.Count} shapes.");
        }
        catch (Exception ex)
        {
            Log($"Zone objects load failed: {ex.Message}");
        }
    }

    // ---- Online flow ---------------------------------------------------------

    private void StartOnline()
    {
        _connection = new KoClientConnection();
        _network = new NetworkGameClient(_connection);
        _network.ConnectRequested += OnConnectRequested;
        _context = new GameContext(_network);

        // The interactive frontend (real .uif dialogs) drives login → char-select.
        // The dev subclass overrides SetupLoginUi to script the --account login.
        SetupLoginUi();

        // The login/intro melody (CGameProcLogIn_1298::Init → Snd\Intro_Sound.mp3,
        // looping). Plays on the login screen; the zone's town/battle BGM hard-cuts
        // it on entry (SelectBgm). Best-effort: null-safe when the asset/device is absent.
        _sound.PlayBgm("Snd\\Intro_Sound.mp3", loop: true);
        Log(_sound.CurrentBgm != null
            ? "BGM: playing Snd\\Intro_Sound.mp3 (intro melody)."
            : "BGM: intro melody not started (no audio device or Snd\\Intro_Sound.mp3 missing).");

        _context.EnteredGame = spawn =>
        {
            Log($"Entered game — zone {spawn.Zone} at ({spawn.X / 10f}, {spawn.Z / 10f}).");
            LoadOnlineZone(spawn);
        };

        // Combat feedback: the target's HP + last damage after each hit (also drives the
        // HUD target bar). TargetHpReceived / EntityDied stay owned by the executable so the
        // HUD's TargetBar.Bind never clobbers them.
        _context.InGame.TargetHpReceived = t =>
        {
            _targetHp = $"HP {t.Hp}/{t.MaxHp}  (-{t.Damage})";
            _inGameUi?.TargetBar.UpdateHp(t.Hp, t.MaxHp);
        };
        _context.InGame.EntityDied = id =>
        {
            if (_targetId == id) { _targetHp = "dead"; _inGameUi?.TargetBar.Clear(); }
            if (id == _context.InGame.World.Local.SocketId)
                _inGameUi?.Dead.Show();
        };

        _netCts = new CancellationTokenSource();
        _ = ConnectAndRunAsync(Config.ServerHost, Config.ServerPort);
        _context.Machine.SetActive(_context.Login);
        Log($"Connecting to login server {Config.ServerHost}:{Config.ServerPort} …");
    }

    /// <summary>
    /// Build the login UI. The clean game shows the interactive frontend (real .uif
    /// dialogs) so the player logs in on screen. The dev subclass overrides this to
    /// script the <c>--account</c> auto-login instead. Requires the asset corpus
    /// (DataPath); without it there is no login screen.
    /// </summary>
    protected virtual void SetupLoginUi()
    {
        if (Config.DataPath == null)
        {
            Log("No data path — cannot show the login screen.");
            return;
        }

        try
        {
            _frontend = new FrontendUi(_context, GraphicsDevice, _fonts, Config.DataPath);
            _frontend.Log += Log;
            _frontend.QuitRequested += Exit;
            Log("Interactive frontend ready.");
        }
        catch (Exception ex)
        {
            Log($"Frontend UI unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// Online zone entry: resolve the spawn's zone id through Zones.tbl to the
    /// terrain .gtd and build the world scene at the server spawn position
    /// (CGameProcMain zone load). Requires --data; degrades to protocol-only.
    /// </summary>
    private void LoadOnlineZone(SelectCharResult spawn)
    {
        if (Config.DataPath == null)
        {
            Log("Zone render skipped — no data path (protocol only).");
            return;
        }

        try
        {
            var resolver = new KoPathResolver(Config.DataPath);
            string? tblPath = resolver.Resolve("Data\\Zones.tbl");
            ZoneRow? zone = tblPath != null ? ZoneTable.LoadFromFile(tblPath).Find(spawn.Zone) : null;

            // Resolve the .gtd from Zones.tbl, falling back to <zoneId>.gtd.
            string gtd = zone != null && !string.IsNullOrEmpty(zone.TerrainFileName)
                ? resolver.Resolve(zone.TerrainFileName) ?? resolver.Resolve($"Zones\\{zone.TerrainFileName}")
                    ?? Path.Combine(Config.DataPath, "Zones", zone.TerrainFileName)
                : Path.Combine(Config.DataPath, "Zones", $"{spawn.Zone}.gtd");

            // Zone minimap texture (CUIStateBar::LoadMap) from Zones.tbl col 07.
            MinimapFile = zone != null && !string.IsNullOrEmpty(zone.MiniMapFileName)
                ? zone.MiniMapFileName
                : $"Zones\\{spawn.Zone}.dxt";

            var spawnPos = new System.Numerics.Vector3(spawn.X / 10f, spawn.Y / 10f, spawn.Z / 10f);
            BuildZoneScene(gtd, resolver, useCentreSpawn: false, spawn: spawnPos);
            EnsureInGameUi();
            Log($"Zone '{zone?.Name ?? spawn.Zone.ToString()}' rendered at the spawn.");
        }
        catch (Exception ex)
        {
            Log($"Online zone load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Build the in-game HUD once the player is in the world. Requires the data path (the
    /// .uif corpus); when absent the HUD is simply not built. Binds only the hooks the
    /// executable does not own (MyInfo/HP/chat).
    /// </summary>
    protected void EnsureInGameUi()
    {
        if (_inGameUi != null || Config.DataPath == null)
            return;

        try
        {
            _inGameUi = new InGameUi(_context, GraphicsDevice, _fonts, Config.DataPath);
            _inGameUi.Log += Log;
            _inGameUi.Bind(_context.InGame);

            // Feed the game clock to the hotkey bar's drag-cast cooldown gate + enable the
            // cooldown-pie renderer (CUIHotKeyDlg::RenderCooldown).
            if (_inGameUi.HotKey is { } hk)
            {
                hk.NowSeconds = () => _gameSeconds;
                hk.EnableCooldownRendering(GraphicsDevice);
            }

            // Minimap (CUIStateBar::LoadMap): load the zone .dxt and reveal Group_MiniMap.
            EnableMinimap();

            // Populate the inventory from any MyInfo already received before the HUD was built.
            _inGameUi.Inventory?.Populate(_context.InGame.Inventory);

            // Entering the world retires the frontend dialogs so ActiveUi routes
            // input to the HUD (the interactive-login path keeps _frontend live
            // until here).
            _frontend?.Dispose();
            _frontend = null;

            Log("In-game HUD ready.");
        }
        catch (Exception ex)
        {
            Log($"In-game HUD unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// CUIStateBar::LoadMap — resolve the zone minimap .dxt to a texture and hand it, with the
    /// world map size, to the state bar's minimap. A missing texture leaves the group hidden.
    /// </summary>
    private void EnableMinimap()
    {
        if (_inGameUi == null || Config.DataPath == null || _minimapFile == null)
            return;

        try
        {
            _minimapTextures ??= new TextureCache(GraphicsDevice, new KoPathResolver(Config.DataPath));
            Texture2D? mapTex = _minimapTextures.Get(_minimapFile);
            _inGameUi.StateBar.EnableMinimap(GraphicsDevice, mapTex, _mapWorldSize, _mapWorldSize);
            Log(mapTex != null
                ? $"Minimap loaded ({_minimapFile})."
                : $"Minimap texture not found: {_minimapFile} (minimap hidden).");
        }
        catch (Exception ex)
        {
            Log($"Minimap load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Per-frame minimap feed (CUIStateBar::UpdatePosition + PositionInfoAdd): the local player's
    /// position/facing plus a dot per remote player (ally/enemy by nation) and NPC.
    /// </summary>
    private void UpdateMinimapFrame()
    {
        if (_inGameUi == null || _context == null)
            return;

        LocalPlayer l = _context.InGame.World.Local;
        float yaw = _player?.Facing ?? _cameraYaw;

        _minimapDots.Clear();
        foreach (RemotePlayer p in _context.InGame.World.Players.Values)
        {
            uint color = p.Nation == l.Nation ? 0xFF00FF00u : 0xFFFF0000u; // ally green / enemy red
            _minimapDots.Add(new OpenKO.Client.Engine.Ui.MinimapDot(
                new System.Numerics.Vector3(p.X, p.Y, p.Z), color));
        }

        foreach (NpcEntity npc in _context.InGame.World.Npcs.Values)
        {
            _minimapDots.Add(new OpenKO.Client.Engine.Ui.MinimapDot(
                new System.Numerics.Vector3(npc.X, npc.Y, npc.Z), 0xFFFFFF00u)); // yellow
        }

        _inGameUi.StateBar.UpdateMinimap(l.X, l.Z, yaw, _minimapDots);
        _inGameUi.StateBar.TickBuffs((float)_timer.SecPerFrame);
    }

    private async Task ConnectAndRunAsync(string host, int port)
    {
        try
        {
            await _connection!.ConnectAsync(new System.Net.DnsEndPoint(host, port), _netCts!.Token);
            Log("Connected. Requesting server list …");
            await _connection.RunAsync(_netCts.Token);
        }
        catch (Exception ex)
        {
            Log($"Connection error: {ex.Message}");
        }
    }

    private void OnConnectRequested(string host, int port)
    {
        // The login → game-server hop reuses the single link (s_pSocket).
        Log($"Reconnecting to game server {host}:{port} …");
        _netCts?.Cancel();
        var fresh = new KoClientConnection();
        try
        {
            fresh.ConnectAsync(new System.Net.DnsEndPoint(host, port)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log($"Game-server connect failed: {ex.Message}");
            return;
        }

        _connection = fresh;
        _network!.AttachConnection(fresh);
        _netCts = new CancellationTokenSource();
        _ = fresh.RunAsync(_netCts.Token);
    }

    // ---- Loop ---------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        _gameSeconds = gameTime.TotalGameTime.TotalSeconds;
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _gameClock.Advance(dt);
        _sound?.UpdateBgm(dt); // stream buffer top-up + fade ramps
        SampleInput(gameTime.TotalGameTime.TotalSeconds);

        if (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_ESCAPE))
            Exit();

        _timer.Tick(gameTime.ElapsedGameTime.TotalSeconds);
        _network?.Pump(_context.Machine);
        _context?.Machine.TickActive();
        _frontend?.Tick();
        _inGameUi?.Tick();
        UpdateMinimapFrame();

        // Advance the FX bundles + the global weather field (CN3FXMgr::Tick). The
        // camera XZ/Y the field recentres on is the last camera eye (RenderWorld).
        _fx?.Tick(_timer.SecPerFrame, _gameCamera.Eye);

        // Re-evaluate the town/battle BGM a couple of times a second (UpdateBGM).
        _bgmThrottle -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_terrainData != null && _bgmThrottle <= 0f)
        {
            _bgmThrottle = 0.5f;
            SelectBgm();
        }

        // I toggles the inventory window (CGameProcMain hotkey), when the HUD is up and no chat
        // edit is focused (so typing "i" in chat doesn't open it).
        if (_inGameUi is { } invHud && invHud.Manager.FocusedEdit == null
            && _input.IsKeyPress(OpenKO.Client.Engine.Input.KeyMap.DIK_I))
        {
            invHud.ToggleInventory();
        }

        // C toggles the character sheet (Various), G the clan browse/join window — HUD up, no chat edit.
        if (_inGameUi is { } sheetHud && sheetHud.Manager.FocusedEdit == null)
        {
            if (_input.IsKeyPress(OpenKO.Client.Engine.Input.KeyMap.DIK_C))
                sheetHud.ToggleVarious();
            if (_input.IsKeyPress(OpenKO.Client.Engine.Input.KeyMap.DIK_G))
                sheetHud.ToggleKnightsOperation();
        }

        // Number keys 1-8 trigger the hotkey bar slots (no chat edit focused).
        if (_inGameUi is { HotKey: not null } hkHud && hkHud.Manager.FocusedEdit == null)
        {
            for (int i = 0; i < OpenKO.Client.Game.Ui.HotKeyDialog.SlotCount; i++)
            {
                if (_input.IsKeyPress(OpenKO.Client.Engine.Input.KeyMap.DIK_1 + i))
                    hkHud.TriggerHotkey(i, _gameSeconds);
            }
        }

        // Interactive UI first; it consumes input (and text focus) before gameplay.
        UiManager ui = ActiveUi;
        ui.Tick();
        _inGameUi?.SetCursor(_input.MousePos.X, _input.MousePos.Y);
        bool uiHandled = ui.Dialogs.Count > 0
            && (UiInputBridge.Dispatch(ui, _input) & (UiMouseProc.DoneSomething | UiMouseProc.DialogFocus)) != 0;

        if (_input.IsKeyPress(OpenKO.Client.Engine.Input.KeyMap.DIK_RETURN))
        {
            if (_frontend?.OnReturnKey() == true)
                uiHandled = true;
            else if (_inGameUi is { } hud && hud.Manager.FocusedEdit != null)
            {
                // Chat edit focused: submit the line (idempotent with the TextInput path,
                // which already clears the edit on Enter).
                hud.SubmitChatReturn();
                uiHandled = true;
            }
        }

        if (!uiHandled)
            HandleInput((float)gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    /// <summary>Feed one device snapshot into the CLocalInput edge machine.</summary>
    private void SampleInput(double time)
    {
        KeyboardState kb = Keyboard.GetState();
        OpenKO.Client.Engine.Input.KeyMap.FillDikArray(kb.GetPressedKeys(), _dikDown);

        MouseState ms = Mouse.GetState();
        int wheel = ms.ScrollWheelValue - _prevScrollWheel;
        _prevScrollWheel = ms.ScrollWheelValue;

        var snapshot = new OpenKO.Client.Engine.Input.InputSnapshot(
            ms.X, ms.Y,
            ms.LeftButton == ButtonState.Pressed,
            ms.MiddleButton == ButtonState.Pressed,
            ms.RightButton == ButtonState.Pressed,
            wheel);
        _input.Tick(_dikDown, snapshot, time);
    }

    /// <summary>Route text/edit keys to the focused edit box (MonoGame Window.TextInput).</summary>
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (ActiveUi.FocusedEdit is not { } edit)
            return;

        switch (e.Key)
        {
            case Keys.Back: edit.Backspace(); return;
            case Keys.Delete: edit.DeleteForward(); return;
            case Keys.Enter: edit.SubmitReturn(); return;
            case Keys.Left: edit.MoveCaret(-1); return;
            case Keys.Right: edit.MoveCaret(1); return;
            case Keys.Home: edit.CaretHome(); return;
            case Keys.End: edit.CaretEnd(); return;
        }

        if (!char.IsControl(e.Character))
            edit.InsertChar(e.Character);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(18, 22, 32));

        if (_terrain != null)
            RenderWorld();

        // The real HUD, once it exists (needs the data path for the .uif corpus).
        if (_inGameUi != null)
        {
            _inGameUi.Draw(gameTime.TotalGameTime.TotalSeconds);
            // Minimap map/dots/arrow + skill cooldown pies draw on top of the HUD frame
            // (CUIStateBar::Render / CUIHotKeyDlg::RenderCooldown draw over the base UI).
            _inGameUi.StateBar.DrawMinimap();
            _inGameUi.HotKey?.DrawCooldowns(gameTime.TotalGameTime.TotalSeconds);
        }
        _frontend?.Draw(gameTime.TotalGameTime.TotalSeconds);

        base.Draw(gameTime);

        // Debug overlay / screenshot dump hook (no-op in the clean game).
        OnAfterDraw(gameTime);
    }

    /// <summary>
    /// Called after the frame is drawn (before Present). The clean game does nothing
    /// here; the dev subclass draws the immediate-mode debug HUD and the screenshot dump.
    /// </summary>
    protected virtual void OnAfterDraw(GameTime gameTime)
    {
    }

    /// <summary>WASD movement (camera-relative) + Left/Right camera orbit + wheel zoom.</summary>
    private void HandleInput(float dt)
    {
        if (_player == null || _terrainData == null)
            return;

        if (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_LEFT))
            _cameraYaw -= dt * 1.6f;
        if (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_RIGHT))
            _cameraYaw += dt * 1.6f;

        float forwardInput = (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_W) ? 1f : 0f)
            - (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_S) ? 1f : 0f);
        float strafeInput = (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_D) ? 1f : 0f)
            - (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_A) ? 1f : 0f);

        // Camera-relative basis (forward points away from the camera).
        var forward = new System.Numerics.Vector3(-MathF.Sin(_cameraYaw), 0f, -MathF.Cos(_cameraYaw));
        var right = new System.Numerics.Vector3(-MathF.Cos(_cameraYaw), 0f, MathF.Sin(_cameraYaw));
        System.Numerics.Vector3 dir = forward * forwardInput + right * strafeInput;

        bool moved = _player.MoveBy(dir, dt, _terrainData);
        _playerPos = _player.Position;

        HandlePick();

        // Stream movement to the server (online), throttled; send a stop on release.
        _moveThrottle -= dt;
        if (_network != null && _context.Machine.Active == _context.InGame)
        {
            if (moved && _moveThrottle <= 0f)
            {
                byte flag = (byte)(WorldProtocol.MoveFlagMoving | WorldProtocol.MoveFlagContinuous);
                _context.InGame.SendMove(_playerPos.X, _playerPos.Y, _playerPos.Z, _player.RunSpeed, flag);
                _context.InGame.SendRotation(_player.Facing);
                _moveThrottle = 0.2f;
            }
            else if (!moved && _wasMoving)
            {
                _context.InGame.SendMove(_playerPos.X, _playerPos.Y, _playerPos.Z, 0f, 0);
            }
        }

        _wasMoving = moved;
    }

    /// <summary>Left-click ray picking against the region entities (CGameProcMain::PickUPC).</summary>
    private void HandlePick()
    {
        bool leftClick = (_input.Mouse & OpenKO.Client.Engine.Input.MouseFlags.LbClick) != 0;
        if (leftClick && _context != null)
        {
            var ray = OpenKO.Client.Game.World.Picking.ScreenPointToRay(
                _lastView, _lastProj, _input.MousePos.X, _input.MousePos.Y,
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            OpenKO.Client.Game.World.WorldPicker.Pick? pick =
                OpenKO.Client.Game.World.WorldPicker.PickNearest(ray, _context.InGame.World);

            if (pick is { } p)
            {
                string name = p.IsNpc && _context.InGame.World.TryGetNpc(p.Id, out var npc) ? npc.Name
                    : !p.IsNpc && _context.InGame.World.TryGet(p.Id, out var pl) ? pl.Name
                    : $"#{p.Id}";
                _selection = $"{(p.IsNpc ? "NPC" : "Player")} {name} (id {p.Id}, {p.Distance:F0}m)";
                _targetId = p.Id;
                _targetHp = "";

                // Show the HUD target bar and ask the server for its HP.
                _inGameUi?.TargetBar.SetTarget(name);
                if (_network != null && _context.Machine.Active == _context.InGame)
                    _context.InGame.SendTargetHpRequest(p.Id);
            }
            else
            {
                _selection = "none";
                _targetId = null;
                _inGameUi?.TargetBar.Clear();
            }
        }

        // Feed the current target to the hotkey cast pipeline (CGameBase::s_pPlayer->m_iIDTarget).
        _inGameUi?.SetTarget(_targetId);

        // Space attacks the selected target (CGameProcMain::MsgSend_Attack).
        if (_input.IsKeyPress(OpenKO.Client.Engine.Input.KeyMap.DIK_SPACE) && _targetId is { } tid
            && _network != null && _context != null && _context.Machine.Active == _context.InGame)
        {
            _context.InGame.SendAttack(tid, interval: 1.0f, distance: 3.0f);
        }
    }

    private void RenderWorld()
    {
        _gameCamera.Target = _playerPos + new System.Numerics.Vector3(0f, 1.6f, 0f);
        _gameCamera.Yaw = _cameraYaw;
        var camera = new N3EngineCamera
        {
            Eye = _gameCamera.Eye,
            At = _gameCamera.At,
            Fov = N3EngineCamera.GameFov,
            Aspect = GraphicsDevice.Viewport.AspectRatio,
            NearPlane = 0.3f,
            FarPlane = MathF.Max(_mapWorldSize * 2f, 1024f),
        };
        camera.Update();
        _lastView = camera.View;
        _lastProj = camera.Projection;

        // 3D audio listener follows the camera (CN3SndObj::SetListener*).
        System.Numerics.Vector3 forward = System.Numerics.Vector3.Normalize(camera.At - camera.Eye);
        _sound.SetListener(camera.Eye, forward, System.Numerics.Vector3.UnitY);

        // Day-night: drive the simulated sun/moon/star arc + fog tint from the game
        // clock before drawing the sky, then scroll the clouds.
        // Once the server clock (WIZ_TIME) has arrived it is authoritative; offline
        // (or pre-first-packet) falls back to the free-running frame clock.
        float dayFraction = _gameClock.HasServerTime
            ? _gameClock.DayFraction
            : DayNightCycle.DayFractionFromSeconds((float)_gameSeconds);
        _sky?.SetTimeOfDay(dayFraction);
        _sky?.Tick(_timer.SecPerFrame);
        _sky?.Render(GraphicsDevice, camera);
        _terrain!.Render(GraphicsDevice, camera);

        // Static zone objects (trees, buildings, gates) from the .opd.
        if (_zoneObjects != null && _characterEffect != null)
        {
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            _characterEffect.View = camera.View.ToXna();
            _characterEffect.Projection = camera.Projection.ToXna();
            _zoneObjects.Tick(camera, _timer);
            _zoneObjects.Render(GraphicsDevice, _characterEffect, camera);
        }

        if (_character != null && _characterEffect != null)
        {
            // Place + face the character from the controller state.
            _character.Chr.Position = _playerPos;
            _character.Chr.Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(
                System.Numerics.Vector3.UnitY, _player?.Facing ?? 0f);

            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            _characterEffect.View = camera.View.ToXna();
            _characterEffect.Projection = camera.Projection.ToXna();
            _character.Tick(camera, _timer);
            _character.Render(GraphicsDevice, _characterEffect);
        }

        // Region-visible remote players (CPlayerOtherMgr::Tick/Render) — assembled
        // on demand from the WIZ_USER_INOUT roster and glided to their move targets.
        if (_remotePlayers != null && _characterEffect != null && _context != null)
        {
            _characterEffect.View = camera.View.ToXna();
            _characterEffect.Projection = camera.Projection.ToXna();
            _remotePlayers.SyncAndRender(
                GraphicsDevice, _characterEffect, camera, _timer,
                _context.InGame.World, _timer.SecPerFrame);
        }

        // Transparent layers over the opaque world, in the C++ order: water (CN3River),
        // then the effect bundles (CN3FXMgr::Render, additive/alpha, no Z write), then
        // the global weather field (CN3GERain/Snow) as the front-most overlay.
        if (_river != null)
        {
            _river.Tick(camera, _timer.SecPerFrame);
            _river.Render(GraphicsDevice, camera);
        }

        if (_fx != null && _fxRenderer != null)
        {
            foreach (FxBundleGame bundle in _fx.Bundles)
                _fxRenderer.Render(bundle.Simulator, camera);
        }

        if (_weather != null && _fx != null)
            _weather.Render(_fx.Weather, camera);
    }

    private void LoadDemoCharacter(KoPathResolver resolver)
    {
        var textures = new TextureCache(GraphicsDevice, resolver);
        var meshes = new PMeshCache(resolver);
        _caches = new ChrAssetCaches(resolver, textures, meshes);
        _characterEffect = new BasicEffect(GraphicsDevice);
        _characterEffect.EnableDefaultLighting();

        // Faithful path: assemble the character at runtime from the looks + item
        // tables (CPlayerOther::Init), exactly like the live client.
        CharacterFactory? factory = CharacterFactory.TryLoad(resolver, _caches);
        _characterFactory = factory;
        if (factory != null)
            _remotePlayers = new RemotePlayerRenderer(factory);
        if (factory != null)
        {
            ChrRenderer? assembled = factory.CreatePlayer(
                OpenKO.Client.Assets.Player.KoRace.Man, face: 1, hair: 1, new uint[8]);
            if (assembled is { HasSkeleton: true })
            {
                assembled.Chr.Position = _playerPos;
                _character = assembled;
                Log("Player model: runtime-assembled El Morad (looks/item tables)");
                return;
            }
        }

        // Fallback: a baked corpus .n3chr when the tables are unavailable.
        string? chrPath = FindRenderableCharacter(resolver);
        if (chrPath == null)
        {
            Log("No renderable character found — terrain only.");
            return;
        }

        try
        {
            var chr = new N3Chr();
            chr.LoadFromFile(chrPath);
            chr.Position = _playerPos;
            _character = new ChrRenderer(chr, _caches);
            Log($"Player model: {Path.GetFileName(chrPath)} (baked)");
        }
        catch (Exception ex)
        {
            Log($"Character load failed: {ex.Message}");
        }
    }

    private string? FindRenderableCharacter(KoPathResolver resolver)
    {
        var opts = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        foreach ((string dir, string pattern) in new[] { ("ChrSelect", "upc_*.n3chr"), ("Chr", "*.n3chr") })
        {
            string full = Path.Combine(Config.DataPath!, dir);
            if (!Directory.Exists(full))
                continue;
            foreach (string path in Directory.EnumerateFiles(full, pattern, opts).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (IsRenderableCharacter(path, resolver))
                    return path;
            }
        }

        return null;
    }

    private static bool IsRenderableCharacter(string chrPath, KoPathResolver resolver)
    {
        try
        {
            var chr = new N3Chr();
            chr.LoadFromFile(chrPath);
            if (!chr.PartFileNames.Any(p => p.Length > 0))
                return false;
            string? jointPath = resolver.Resolve(chr.JointFileName);
            if (jointPath == null)
                return false;
            using FileStream stream = File.OpenRead(jointPath);
            var joint = new N3Joint();
            joint.Load(new BinaryReader(stream));
            return stream.Position == stream.Length && joint.NodeCount() >= 8;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Append a line to the console + the rolling on-screen log buffer.</summary>
    protected void Log(string message)
    {
        Console.WriteLine(message);
        _log.Add(message);
        if (_log.Count > 12)
            _log.RemoveAt(0);
    }

    protected override void UnloadContent()
    {
        _netCts?.Cancel();
        _inGameUi?.Dispose();
        _frontend?.Dispose();
        _terrain?.Dispose();
        _sky?.Dispose();
        _river?.Dispose();
        _weather?.Dispose();
        _fxRenderer?.Dispose();
        _caches?.Textures.Dispose();
        _characterEffect?.Dispose();
        _fonts.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
    }

    /// <summary>
    /// CGameProcMain::CharacterGetByID + CPlayerBase::JointPosGet over the client
    /// world roster: the local player (<see cref="_playerPos"/>), the region-visible
    /// remote players and the NPCs. When <paramref name="joint"/> is non-negative and
    /// the entity has a rendered character, returns the joint's world position
    /// (<see cref="FxJointMath.WorldPos"/>); otherwise the entity origin. Returns
    /// false when the entity has left the region.
    /// </summary>
    private sealed class ClientFxEntityLocator(KnightOnlineGame game) : IFxEntityLocator
    {
        public bool TryGetPosition(int entityId, int joint, out System.Numerics.Vector3 pos)
        {
            pos = default;

            WorldEntities world = game._context?.InGame.World!;
            if (world == null)
                return false;

            // The local player.
            if (entityId == world.Local.SocketId)
            {
                var origin = new System.Numerics.Vector3(game._playerPos.X, game._playerPos.Y, game._playerPos.Z);
                pos = JointWorld(game._character, joint) ?? origin;
                return true;
            }

            // A region-visible remote player.
            if (world.TryGet((short)entityId, out RemotePlayer player))
            {
                var origin = new System.Numerics.Vector3(player.X, player.Y, player.Z);
                pos = JointWorld(game._remotePlayers?.TryGetRenderer((short)entityId), joint) ?? origin;
                return true;
            }

            // An NPC.
            if (world.TryGetNpc((short)entityId, out NpcEntity npc))
            {
                var origin = new System.Numerics.Vector3(npc.X, npc.Y, npc.Z);
                pos = JointWorld(game._remotePlayers?.TryGetRenderer((short)entityId), joint) ?? origin;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The joint world position (JointPosGet) for a rendered character, or null to
        /// fall back to the origin: joint &lt; 0, no character, or the index is out of
        /// range (guarded).
        /// </summary>
        private static System.Numerics.Vector3? JointWorld(
            OpenKO.Client.Engine.Objects.ChrRenderer? chr, int joint)
        {
            if (chr == null || joint < 0)
                return null;

            IReadOnlyList<System.Numerics.Matrix4x4> joints = chr.JointMatrices;
            if (joint >= joints.Count)
                return null;

            return FxJointMath.WorldPos(joints[joint], chr.Chr.Matrix);
        }
    }

    /// <summary>
    /// The FXID → .fxb loader (CN3FXMgr::TriggerBundle): look the FXID up in
    /// <c>fx.tbl</c> (<see cref="OpenKO.Client.Assets.Effects.FxSourceTable"/>),
    /// normalize its <c>szFN</c> to the lower-cased <c>.fxb</c> cache key
    /// (<see cref="OpenKO.Client.Assets.Effects.FxFileName"/>), load + cache the
    /// bundle, and surface the row's <c>dwSoundID</c>. An unknown FXID (or absent
    /// table/file) → false = a no-op trigger, matching the C++ early-out.
    /// </summary>
    private sealed class ClientFxBundleLoader(
        KoPathResolver resolver, OpenKO.Client.Assets.Effects.FxSourceTable? table) : IFxBundleLoader
    {
        private readonly Dictionary<string, (OpenKO.Client.Assets.N3FXBundle Bundle, uint SoundId)> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public bool TryResolve(
            int fxId, out string cacheKey, out uint soundId, out OpenKO.Client.Assets.N3FXBundle bundle)
        {
            cacheKey = string.Empty;
            soundId = 0;
            bundle = null!;

            if (table == null || !table.TryGet((uint)fxId, out OpenKO.Client.Assets.Effects.FxSourceRow row))
                return false;

            cacheKey = OpenKO.Client.Assets.Effects.FxFileName.Normalize(row.FileName);
            if (cacheKey.Length == 0)
                return false;

            soundId = row.SoundId;

            if (_cache.TryGetValue(cacheKey, out (OpenKO.Client.Assets.N3FXBundle Bundle, uint SoundId) cached))
            {
                bundle = cached.Bundle;
                soundId = cached.SoundId;
                return true;
            }

            string? path = resolver.Resolve(cacheKey) ?? resolver.Resolve($"fx\\{cacheKey}");
            if (path == null)
                return false;

            try
            {
                var loaded = new OpenKO.Client.Assets.N3FXBundle();
                loaded.LoadFromFile(path);
                _cache[cacheKey] = (loaded, soundId);
                bundle = loaded;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
