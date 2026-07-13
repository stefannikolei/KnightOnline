using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Audio;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Scene;
using OpenKO.Client.Engine.Sky;
using OpenKO.Client.Engine.Terrain;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

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
    private SkyRenderer? _sky;
    private float _mapWorldSize;
    private float _orbit;

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
            _sky = new SkyRenderer(GraphicsDevice);
            _mapWorldSize = terrain.MapSize * TerrainVertexBuilder.TileSize;

            _context.Spawn = new SelectCharResult(1, 0, (ushort)(_mapWorldSize * 5f), (ushort)(_mapWorldSize * 5f), 0, 1);
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

        _network?.Pump(_context.Machine);
        _context?.Machine.TickActive();
        _orbit += (float)gameTime.ElapsedGameTime.TotalSeconds * 0.15f;

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

    private void RenderWorld()
    {
        float half = _mapWorldSize * 0.5f;
        var center = new System.Numerics.Vector3(half, 0f, half);
        float radius = MathF.Max(_mapWorldSize * 0.35f, 60f);
        var camera = new N3EngineCamera
        {
            Eye = center + new System.Numerics.Vector3(
                MathF.Sin(_orbit) * radius, MathF.Max(_mapWorldSize * 0.25f, 40f), MathF.Cos(_orbit) * radius),
            At = center,
            Fov = N3EngineCamera.GameFov,
            Aspect = GraphicsDevice.Viewport.AspectRatio,
            NearPlane = 1f,
            FarPlane = MathF.Max(_mapWorldSize * 2f, 1024f),
        };
        camera.Update();

        // 3D audio listener follows the camera (CN3SndObj::SetListener*).
        System.Numerics.Vector3 forward = System.Numerics.Vector3.Normalize(camera.At - camera.Eye);
        _sound.SetListener(camera.Eye, forward, System.Numerics.Vector3.UnitY);

        _sky?.Render(GraphicsDevice, camera);
        _terrain!.Render(GraphicsDevice, camera);
    }

    private void DrawHud()
    {
        DynamicSpriteFont title = _fonts.GetUiFont(18);
        DynamicSpriteFont body = _fonts.GetUiFont(11);

        _spriteBatch.Begin();
        _spriteBatch.DrawString(title, "Knight Online — OpenKO C# Port", new Vector2(16, 12), Color.White);

        string state = _context?.Machine.Active?.Name ?? "—";
        _spriteBatch.DrawString(body, $"State: {state}", new Vector2(16, 44), new Color(180, 210, 255));

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
