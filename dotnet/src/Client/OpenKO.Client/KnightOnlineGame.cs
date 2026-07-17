using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenKO.Client.Assets;
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
/// The runnable client host: a MonoGame game loop that owns the
/// <see cref="GameStateMachine"/>, drives the client network layer and renders
/// the world plus a status HUD. With <c>--server</c> it connects and auto-runs
/// the login→char-select→in-game flow; with <c>--offline &lt;zone&gt;</c> it renders a
/// zone directly (no server needed).
/// </summary>
public sealed class KnightOnlineGame : Microsoft.Xna.Framework.Game
{
    private readonly ClientOptions _options;
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

    // Water + weather + FX (slice 9.11d): the already-built renderers wired into
    // the zone scene. _fx is the pure game-side manager (bundles + weather field);
    // _river/_weather/_fxRenderer are the device layers.
    private RiverRenderer? _river;
    private WeatherRenderer? _weather;
    private FxManager? _fx;
    private FxRenderer? _fxRenderer;
    private bool _fxHooksBound;

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
    private int _prevScrollWheel;

    /// <summary>The manager receiving input/drawing this frame (frontend during login, HUD in-game).</summary>
    private UiManager ActiveUi => _frontend?.Manager ?? _inGameUi?.Manager ?? _ui;

    private string _selection = "none";
    private short? _targetId;
    private string _targetHp = "";
    private double _gameSeconds;

    private readonly List<string> _log = [];
    private int _framesDrawn;

    public KnightOnlineGame(ClientOptions options)
    {
        _options = options;
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
        _sound = new SoundManager(new MonoGameAudioBackend());
        Log(_sound.Backend.IsAvailable ? "Audio: OpenAL device ready." : "Audio: no device (silent).");

        if (_options.OfflineZone != null && _options.DataPath != null)
            StartOfflineZone();
        else if (_options.ServerHost != null)
            StartOnline();
        else
            Log("No --server or --offline given — idle title screen.");
    }

    // ---- Offline zone demo ---------------------------------------------------

    private void StartOfflineZone()
    {
        _context = new GameContext(new NullGameClient());
        try
        {
            string gtd = Path.Combine(_options.DataPath!, "Zones", _options.OfflineZone + ".gtd");
            var resolver = new KoPathResolver(_options.DataPath!);

            // Place the player at the map centre, on the terrain surface.
            float centre = 0f;
            BuildZoneScene(gtd, resolver, useCentreSpawn: true, spawn: default);
            centre = _mapWorldSize * 0.5f;

            _context.Spawn = new SelectCharResult(
                1, 0, (ushort)(centre * 10f), (ushort)(centre * 10f), (short)(_playerPos.Y * 10f), 1);
            _context.InGame.World.Local.X = _playerPos.X;
            _context.InGame.World.Local.Y = _playerPos.Y;
            _context.InGame.World.Local.Z = _playerPos.Z;
            _context.Machine.SetActive(_context.InGame);
            EnsureInGameUi();
            Log($"Offline zone '{_options.OfflineZone}' loaded ({_terrainData!.MapSize} tiles).");
        }
        catch (Exception ex)
        {
            Log($"Zone load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a zone's terrain/sky and the player character, then places the
    /// player. Shared by the offline demo (centre spawn) and the online flow
    /// (server spawn). Requires --data for the asset corpus.
    /// </summary>
    private void BuildZoneScene(
        string gtdPath, KoPathResolver resolver, bool useCentreSpawn, System.Numerics.Vector3 spawn)
    {
        var terrain = new N3Terrain();
        terrain.LoadFromFile(gtdPath);
        _terrain = new TerrainRenderer(GraphicsDevice, terrain, resolver, gtdPath);
        _terrainData = terrain;
        _sky = new SkyRenderer(GraphicsDevice);
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
            var locator = new ClientFxEntityLocator(this);
            var loader = new ClientFxBundleLoader(resolver);
            _fx = new FxManager(locator, loader);
            _fxRenderer = new FxRenderer(GraphicsDevice, ResolveFxTexture);

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

        // WIZ_MAGIC_PROCESS → the cast/fly/hit FX triggers. The magic→(fx1,fx2)
        // resolver would come from the skill/magic table; that table is not loaded
        // in the client yet, so it degrades to (0,0) = no FX (documented deferral).
        inGame.MagicReceived += packet =>
        {
            if (_fx is { } fx)
                FxTriggerBinding.Trigger(fx, packet, MagicFxResolve);
        };
    }

    /// <summary>
    /// magicId → (fx1, fx2) effect ids. The effect table is not wired, so this
    /// returns (0, 0) (no FX). Wire it to the skill/magic table (dwEffectID1/2)
    /// once that table is loaded client-side.
    /// </summary>
    private static (int Fx1, int Fx2) MagicFxResolve(int magicId) => (0, 0);

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
        if (!TryPlayBgmWav(track))
            Log($"BGM: {track.Name} (id {track.Id}) selected — no .wav in corpus (MP3 streaming deferred).");
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

    /// <summary>Play a BGM track from a corpus .wav (Sound\&lt;id&gt;.wav), looping. False if absent/unplayable.</summary>
    private bool TryPlayBgmWav(BgmTrack track)
    {
        if (_options.DataPath == null)
            return false;

        try
        {
            var resolver = new KoPathResolver(_options.DataPath);
            string? wav = resolver.Resolve($"Sound\\{track.Id}.wav") ?? resolver.Resolve($"Sound\\{track.Name}.wav");
            if (wav == null)
                return false;

            if (!_sound.IsRegistered(track.Name))
                _sound.Register(track.Name, WavAudio.LoadFromFile(wav), SoundType.Stream);

            return _sound.Play(track.Name, gain: 0.6f, loop: true);
        }
        catch (Exception)
        {
            return false;
        }
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
        _context = new GameContext(_network)
        {
            Account = _options.Account,
            Password = _options.Password,
        };

        // --account keeps the scripted auto-login; otherwise the interactive
        // frontend (real .uif dialogs) drives login → char-select.
        if (_options.Account.Length > 0)
        {
            WireAutoLogin();
        }
        else if (_options.DataPath != null)
        {
            try
            {
                _frontend = new FrontendUi(_context, GraphicsDevice, _fonts, _options.DataPath);
                _frontend.Log += Log;
                _frontend.QuitRequested += Exit;
                Log("Interactive frontend ready (no --account).");
            }
            catch (Exception ex)
            {
                Log($"Frontend UI unavailable: {ex.Message}");
            }
        }
        else
        {
            Log("No --account and no --data — cannot log in (pass one of them).");
        }

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
        _ = ConnectAndRunAsync(_options.ServerHost!, _options.ServerPort);
        _context.Machine.SetActive(_context.Login);
        Log($"Connecting to login server {_options.ServerHost}:{_options.ServerPort} …");
    }

    private void WireAutoLogin()
    {
        _context.ServerListReceived = servers =>
        {
            Log($"Server list: {servers.Count} server(s).");
            _context.Login.SubmitAccountLogin(_options.Account, _options.Password);
        };
        _context.AccountLoginResult = result =>
        {
            Log($"Account login: result {result.Result}.");
            if (result.Success && _context.Servers.Count > 0)
                _context.Login.ConnectToGameServer(_context.Servers[0]);
        };
        _context.NationResolved = nation => Log($"Nation: {nation}.");
        _context.CharactersReceived = chars =>
        {
            int slot = -1;
            for (int i = 0; i < chars.Count; i++)
                if (!chars[i].IsEmpty) { slot = i; break; }

            Log(slot >= 0 ? $"Selecting character '{chars[slot].CharId}'." : "No characters on the account.");
            if (slot >= 0)
                _context.CharSelect.SelectCharacter(slot);
        };
    }

    /// <summary>
    /// Online zone entry: resolve the spawn's zone id through Zones.tbl to the
    /// terrain .gtd and build the world scene at the server spawn position
    /// (CGameProcMain zone load). Requires --data; degrades to protocol-only.
    /// </summary>
    private void LoadOnlineZone(SelectCharResult spawn)
    {
        if (_options.DataPath == null)
        {
            Log("Zone render skipped — no --data path (protocol only).");
            return;
        }

        try
        {
            var resolver = new KoPathResolver(_options.DataPath);
            string? tblPath = resolver.Resolve("Data\\Zones.tbl");
            ZoneRow? zone = tblPath != null ? ZoneTable.LoadFromFile(tblPath).Find(spawn.Zone) : null;

            // Resolve the .gtd from Zones.tbl, falling back to <zoneId>.gtd.
            string gtd = zone != null && !string.IsNullOrEmpty(zone.TerrainFileName)
                ? resolver.Resolve(zone.TerrainFileName) ?? resolver.Resolve($"Zones\\{zone.TerrainFileName}")
                    ?? Path.Combine(_options.DataPath, "Zones", zone.TerrainFileName)
                : Path.Combine(_options.DataPath, "Zones", $"{spawn.Zone}.gtd");

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
    /// Build the in-game HUD once the player is in the world. Requires --data (the .uif
    /// corpus); degrades to the immediate-mode <see cref="DrawHud"/> when absent, like the
    /// zone render. Binds only the hooks the executable does not own (MyInfo/HP/chat).
    /// </summary>
    private void EnsureInGameUi()
    {
        if (_inGameUi != null || _options.DataPath == null)
            return;

        try
        {
            _inGameUi = new InGameUi(_context, GraphicsDevice, _fonts, _options.DataPath);
            _inGameUi.Log += Log;
            _inGameUi.Bind(_context.InGame);

            // Feed the game clock to the hotkey bar's drag-cast cooldown gate.
            if (_inGameUi.HotKey is { } hk)
                hk.NowSeconds = () => _gameSeconds;

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
        SampleInput(gameTime.TotalGameTime.TotalSeconds);

        if (_input.IsKeyDown(OpenKO.Client.Engine.Input.KeyMap.DIK_ESCAPE))
            Exit();

        _timer.Tick(gameTime.ElapsedGameTime.TotalSeconds);
        _network?.Pump(_context.Machine);
        _context?.Machine.TickActive();
        _frontend?.Tick();
        _inGameUi?.Tick();

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

        // The real HUD replaces the immediate-mode overlay once it exists (needs --data);
        // fall back to DrawHud only when the HUD could not be built.
        if (_inGameUi != null)
            _inGameUi.Draw(gameTime.TotalGameTime.TotalSeconds);
        else
            DrawHud();
        _frontend?.Draw(gameTime.TotalGameTime.TotalSeconds);

        base.Draw(gameTime);

        if (_options.ScreenshotPath != null && ++_framesDrawn == 30)
        {
            SaveScreenshot(_options.ScreenshotPath);
            Console.WriteLine($"Screenshot: {_options.ScreenshotPath}");
            Exit();
        }
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
        _sky?.SetTimeOfDay(DayNightCycle.DayFractionFromSeconds((float)_gameSeconds));
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
            string full = Path.Combine(_options.DataPath!, dir);
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

    private void DrawHud()
    {
        DynamicSpriteFont title = _fonts.GetUiFont(18);
        DynamicSpriteFont body = _fonts.GetUiFont(11);

        _spriteBatch.Begin();
        _spriteBatch.DrawString(title, "Knight Online — OpenKO C# Port", new Vector2(16, 12), Color.White);

        string state = _context?.Machine.Active?.Name ?? "—";
        _spriteBatch.DrawString(body, $"State: {state}", new Vector2(16, 44), new Color(180, 210, 255));

        if (_terrainData != null)
        {
            _spriteBatch.DrawString(body, "WASD move · ←→ camera · click target · Esc quit",
                new Vector2(GraphicsDevice.Viewport.Width - 330, 44), new Color(150, 160, 180));
            _spriteBatch.DrawString(body, $"Target: {_selection}  {_targetHp}",
                new Vector2(GraphicsDevice.Viewport.Width - 330, 62), new Color(255, 200, 160));
        }

        if (_context?.Machine.Active == _context?.InGame && _context != null)
        {
            var l = _context.InGame.World.Local;
            _spriteBatch.DrawString(body,
                $"Zone {_context.Spawn.Zone}  pos ({l.X:F0}, {l.Y:F0}, {l.Z:F0})  " +
                $"players: {_context.InGame.World.Players.Count}  npcs: {_context.InGame.World.Npcs.Count}",
                new Vector2(16, 62), new Color(180, 255, 200));

            // Full character sheet once the WIZ_MYINFO block has landed (level > 0).
            if (l.Level > 0)
            {
                _spriteBatch.DrawString(body,
                    $"{l.Name}  Lv {l.Level}   HP {l.Hp}/{l.MaxHp}   MP {l.Mp}/{l.MaxMp}   " +
                    $"AC {l.TotalAc}   Gold {l.Gold:N0}",
                    new Vector2(16, 80), new Color(255, 230, 160));
                _spriteBatch.DrawString(body,
                    $"STR {l.Str}+{l.ItemStr}  STA {l.Sta}+{l.ItemSta}  DEX {l.Dex}+{l.ItemDex}  " +
                    $"INT {l.Intel}+{l.ItemIntel}  CHA {l.Cha}+{l.ItemCha}   items {_context.InGame.Inventory.Slots.Count}",
                    new Vector2(16, 96), new Color(200, 220, 180));
            }
        }

        int y = GraphicsDevice.Viewport.Height - 16 - _log.Count * 16;
        foreach (string line in _log)
        {
            _spriteBatch.DrawString(body, line, new Vector2(16, y), new Color(200, 200, 200));
            y += 16;
        }

        _spriteBatch.End();
    }

    private void Log(string message)
    {
        Console.WriteLine(message);
        _log.Add(message);
        if (_log.Count > 12)
            _log.RemoveAt(0);
    }

    private void SaveScreenshot(string path)
    {
        int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var data = new Color[w * h];
        GraphicsDevice.GetBackBufferData(data);
        using var tex = new Texture2D(GraphicsDevice, w, h);
        tex.SetData(data);
        using FileStream fs = File.Create(path);
        tex.SaveAsPng(fs, w, h);
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
    /// CGameProcMain::CharacterGetByID/JointPosGet over the client world roster: the
    /// local player (<see cref="_playerPos"/>), the region-visible remote players and
    /// the NPCs. Joints are not exposed by the roster, so a joint offset is ignored
    /// (origin only); returns false when the entity has left the region.
    /// </summary>
    private sealed class ClientFxEntityLocator(KnightOnlineGame game) : IFxEntityLocator
    {
        public bool TryGetPosition(int entityId, int joint, out System.Numerics.Vector3 pos)
        {
            _ = joint; // Joint offset deferred (the roster exposes origins only).
            pos = default;

            WorldEntities world = game._context?.InGame.World!;
            if (world == null)
                return false;

            // The local player.
            if (entityId == world.Local.SocketId)
            {
                pos = new System.Numerics.Vector3(game._playerPos.X, game._playerPos.Y, game._playerPos.Z);
                return true;
            }

            // A region-visible remote player.
            if (world.TryGet((short)entityId, out RemotePlayer player))
            {
                pos = new System.Numerics.Vector3(player.X, player.Y, player.Z);
                return true;
            }

            // An NPC.
            if (world.TryGetNpc((short)entityId, out NpcEntity npc))
            {
                pos = new System.Numerics.Vector3(npc.X, npc.Y, npc.Z);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The FXID → .fxb loader. The FX effect table (s_pTbl_FXSource) is not wired
    /// yet, so this resolves a best-effort <c>fx\&lt;FXID&gt;.fxb</c> path through the
    /// resolver and caches the loaded bundle by its lower-cased filename. Returns
    /// false (a no-op trigger) when the file is absent — the offline corpus has no
    /// .fxb, which is expected.
    /// </summary>
    private sealed class ClientFxBundleLoader(KoPathResolver resolver) : IFxBundleLoader
    {
        private readonly Dictionary<string, OpenKO.Client.Assets.N3FXBundle> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public bool TryResolve(int fxId, out string cacheKey, out OpenKO.Client.Assets.N3FXBundle bundle)
        {
            cacheKey = $"{fxId}.fxb";
            if (_cache.TryGetValue(cacheKey, out OpenKO.Client.Assets.N3FXBundle? cached))
            {
                bundle = cached;
                return true;
            }

            bundle = null!;
            string? path = resolver.Resolve($"fx\\{fxId}.fxb") ?? resolver.Resolve($"{fxId}.fxb");
            if (path == null)
                return false;

            try
            {
                var loaded = new OpenKO.Client.Assets.N3FXBundle();
                loaded.LoadFromFile(path);
                _cache[cacheKey] = loaded;
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

/// <summary>A no-op client for the offline zone demo (no networking).</summary>
internal sealed class NullGameClient : IGameClient
{
    public bool CryptionEnabled => false;

    public void Send(ReadOnlySpan<byte> payload) { }

    public void Connect(string host, int port) { }

    public void EnableCryption(ulong publicKey) { }
}
