using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenKO.Client.Assets;
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
    private readonly OpenKO.Client.Engine.Scene.FrameTimer _timer = new();
    private readonly OpenKO.Client.Game.World.GameCamera _gameCamera = new();
    private OpenKO.Client.Game.World.PlayerController? _player;
    private System.Numerics.Vector3 _playerPos;
    private float _mapWorldSize;
    private float _cameraYaw;
    private float _moveThrottle;
    private bool _wasMoving;

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
            var terrain = new N3Terrain();
            terrain.LoadFromFile(gtd);
            var resolver = new KoPathResolver(_options.DataPath!);
            _terrain = new TerrainRenderer(GraphicsDevice, terrain, resolver, gtd);
            _terrainData = terrain;
            _sky = new SkyRenderer(GraphicsDevice);
            _mapWorldSize = terrain.MapSize * TerrainVertexBuilder.TileSize;

            // Place the player at the map centre, on the terrain surface.
            float cx = _mapWorldSize * 0.5f;
            float cz = _mapWorldSize * 0.5f;
            float cy = OpenKO.Client.Game.World.TerrainCollision.GetHeight(terrain, cx, cz);
            if (cy <= OpenKO.Client.Game.World.TerrainCollision.OutOfRange + 1f)
                cy = 0f;
            _playerPos = new System.Numerics.Vector3(cx, cy, cz);
            _player = new PlayerController { Position = _playerPos };

            LoadDemoCharacter(resolver);

            _context.Spawn = new SelectCharResult(1, 0, (ushort)(cx * 10f), (ushort)(cz * 10f), (short)(cy * 10f), 1);
            _context.InGame.World.Local.X = cx;
            _context.InGame.World.Local.Y = cy;
            _context.InGame.World.Local.Z = cz;
            _context.Machine.SetActive(_context.InGame);
            Log($"Offline zone '{_options.OfflineZone}' loaded ({terrain.MapSize} tiles).");
        }
        catch (Exception ex)
        {
            Log($"Zone load failed: {ex.Message}");
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
        _context.EnteredGame = spawn => Log($"Entered game — zone {spawn.Zone} at ({spawn.X / 10f}, {spawn.Z / 10f}).");
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

        // 3D audio listener follows the camera (CN3SndObj::SetListener*).
        System.Numerics.Vector3 forward = System.Numerics.Vector3.Normalize(camera.At - camera.Eye);
        _sound.SetListener(camera.Eye, forward, System.Numerics.Vector3.UnitY);

        _sky?.Render(GraphicsDevice, camera);
        _terrain!.Render(GraphicsDevice, camera);

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
    }

    private void LoadDemoCharacter(KoPathResolver resolver)
    {
        var textures = new TextureCache(GraphicsDevice, resolver);
        var meshes = new PMeshCache(resolver);
        _caches = new ChrAssetCaches(resolver, textures, meshes);
        _characterEffect = new BasicEffect(GraphicsDevice);
        _characterEffect.EnableDefaultLighting();

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
            Log($"Player model: {Path.GetFileName(chrPath)}");
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
            _spriteBatch.DrawString(body, "WASD move · ←→ camera · Esc quit",
                new Vector2(GraphicsDevice.Viewport.Width - 260, 44), new Color(150, 160, 180));
        }

        if (_context?.Machine.Active == _context?.InGame && _context != null)
        {
            var l = _context.InGame.World.Local;
            _spriteBatch.DrawString(body,
                $"Zone {_context.Spawn.Zone}  pos ({l.X:F0}, {l.Y:F0}, {l.Z:F0})  " +
                $"players nearby: {_context.InGame.World.Players.Count}",
                new Vector2(16, 62), new Color(180, 255, 200));
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
