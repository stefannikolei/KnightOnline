using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Zones;
using OpenKO.Client.Engine.Audio;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Objects;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;
using OpenKO.Client.Engine.Sky;
using OpenKO.Client.Engine.Terrain;
using OpenKO.Client.Engine.Ui;
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
    private bool _prevMouseLeft;
    private bool _prevAttackKey;
    private string _selection = "none";
    private short? _targetId;
    private string _targetHp = "";

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

        WireAutoLogin();

        // Combat feedback: the target's HP + last damage after each hit.
        _context.InGame.TargetHpReceived = t =>
            _targetHp = $"HP {t.Hp}/{t.MaxHp}  (-{t.Damage})";
        _context.InGame.EntityDied = id =>
        {
            if (_targetId == id) { _targetHp = "dead"; }
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
        _context.EnteredGame = spawn =>
        {
            Log($"Entered game — zone {spawn.Zone} at ({spawn.X / 10f}, {spawn.Z / 10f}).");
            LoadOnlineZone(spawn);
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
            Log($"Zone '{zone?.Name ?? spawn.Zone.ToString()}' rendered at the spawn.");
        }
        catch (Exception ex)
        {
            Log($"Online zone load failed: {ex.Message}");
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
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        _timer.Tick(gameTime.ElapsedGameTime.TotalSeconds);
        _network?.Pump(_context.Machine);
        _context?.Machine.TickActive();
        HandleInput((float)gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(18, 22, 32));

        if (_terrain != null)
            RenderWorld();

        DrawHud();

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

        KeyboardState kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.Left))
            _cameraYaw -= dt * 1.6f;
        if (kb.IsKeyDown(Keys.Right))
            _cameraYaw += dt * 1.6f;

        float forwardInput = (kb.IsKeyDown(Keys.W) ? 1f : 0f) - (kb.IsKeyDown(Keys.S) ? 1f : 0f);
        float strafeInput = (kb.IsKeyDown(Keys.D) ? 1f : 0f) - (kb.IsKeyDown(Keys.A) ? 1f : 0f);

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
        MouseState mouse = Mouse.GetState();
        bool left = mouse.LeftButton == ButtonState.Pressed;
        if (left && !_prevMouseLeft && _context != null)
        {
            var ray = OpenKO.Client.Game.World.Picking.ScreenPointToRay(
                _lastView, _lastProj, mouse.X, mouse.Y,
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
            }
            else
            {
                _selection = "none";
                _targetId = null;
            }
        }

        _prevMouseLeft = left;

        // Space attacks the selected target (CGameProcMain::MsgSend_Attack).
        bool attack = Keyboard.GetState().IsKeyDown(Keys.Space);
        if (attack && !_prevAttackKey && _targetId is { } tid
            && _network != null && _context != null && _context.Machine.Active == _context.InGame)
        {
            _context.InGame.SendAttack(tid, interval: 1.0f, distance: 3.0f);
        }

        _prevAttackKey = attack;
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
        _terrain?.Dispose();
        _sky?.Dispose();
        _caches?.Textures.Dispose();
        _characterEffect?.Dispose();
        _fonts.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
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
