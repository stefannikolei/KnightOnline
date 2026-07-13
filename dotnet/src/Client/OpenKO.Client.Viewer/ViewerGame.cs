using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenKO.Client.Engine.Input;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Viewer;

/// <summary>
/// The debug-viewer host: replaces the WarFareMain message pump with the
/// MonoGame game loop. Scenes register in a list; Tab cycles through them
/// (each scene owns its frame like the C++ game procedures).
/// </summary>
public sealed class ViewerGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly List<IScene> _scenes = [];
    private readonly FrameTimer _timer = new();
    private readonly InputState _input = new();
    private readonly bool[] _dikDown = new bool[InputState.NumKeys];
    private readonly string? _dataPath;
    private readonly string? _startScene;
    private readonly string? _screenshotPath;
    private ViewerContext? _context;
    private int _sceneIndex;
    private bool _sceneLoaded;
    private int _framesDrawn;

    public ViewerGame(string? dataPath, string? startScene, string? screenshotPath = null)
    {
        _dataPath = dataPath;
        _startScene = startScene;
        _screenshotPath = screenshotPath;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1024,
            PreferredBackBufferHeight = 768,
            SynchronizeWithVerticalRetrace = true, // bVSyncEnabled default
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    public void AddScene(IScene scene) => _scenes.Add(scene);

    protected override void Initialize()
    {
        base.Initialize();

        _context = new ViewerContext
        {
            Device = GraphicsDevice,
            DataPath = _dataPath,
            Timer = _timer,
            Input = _input,
        };

        if (_startScene != null)
        {
            int index = _scenes.FindIndex(
                s => s.Name.Contains(_startScene, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _sceneIndex = index;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        _timer.Tick(gameTime.ElapsedGameTime.TotalSeconds);

        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        KeyMap.FillDikArray(keyboard.GetPressedKeys(), _dikDown);
        _input.Tick(_dikDown, new InputSnapshot(
            mouse.X, mouse.Y,
            mouse.LeftButton == ButtonState.Pressed,
            mouse.MiddleButton == ButtonState.Pressed,
            mouse.RightButton == ButtonState.Pressed), _timer.TotalSeconds);

        if (_input.IsKeyPress(KeyMap.DIK_ESCAPE))
        {
            Exit();
            return;
        }

        if (_scenes.Count > 0 && _input.IsKeyPress(KeyMap.DIK_TAB))
        {
            CurrentScene?.Unload();
            _sceneLoaded = false;
            _sceneIndex = (_sceneIndex + 1) % _scenes.Count;
        }

        if (_context != null && CurrentScene is { } scene)
        {
            if (!_sceneLoaded)
            {
                scene.Load(_context);
                _sceneLoaded = true;
            }

            scene.Tick(_context);
            Window.Title = $"OpenKO Viewer — {scene.Name} — {_timer.FramesPerSecond:F0} fps (Tab: nächste Szene)";
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_context != null && CurrentScene is { } scene && _sceneLoaded)
        {
            scene.Render(_context);
        }
        else
        {
            GraphicsDevice.Clear(new Color(24, 24, 48));
        }

        base.Draw(gameTime);

        // --screenshot: let animations tick for a moment, dump, exit.
        if (_screenshotPath != null && ++_framesDrawn == 30)
        {
            Screenshot.SaveBackBuffer(GraphicsDevice, _screenshotPath);
            Console.WriteLine($"Screenshot: {_screenshotPath}");
            Exit();
        }
    }

    private IScene? CurrentScene => _sceneIndex < _scenes.Count ? _scenes[_sceneIndex] : null;
}
