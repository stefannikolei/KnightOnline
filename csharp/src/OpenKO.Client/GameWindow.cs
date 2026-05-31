using System.Drawing;
using System.Numerics;
using OpenKO.Client.Rendering;
using OpenKO.Game;
using OpenKO.Game.Procedures;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace OpenKO.Client;

/// <summary>What the window shows. The login screen exercises the 2D UI path; the demo exercises 3D.</summary>
public enum WindowScene
{
    /// <summary>Render the ported login screen through the game-procedure + UI render path (default).</summary>
    Login,

    /// <summary>Render the textured, lit, rotating demo mesh (3D render-path smoke test).</summary>
    Demo3D,
}

/// <summary>
/// Cross-platform application window, the eventual replacement for the original Win32/DirectX 9
/// host (WarFareMain.cpp / CGameEng). It opens an OpenGL context and drives the ported render path.
///
/// In <see cref="WindowScene.Login"/> mode it hosts a <see cref="GameContext"/> and runs the
/// <see cref="GameProcedureManager"/> loop (pump network → tick → render), with a
/// <see cref="LoginProcedure"/> as the active state drawn via the OpenGL <see cref="UiRenderer"/>.
/// In <see cref="WindowScene.Demo3D"/> mode it shows the original procedural demo mesh.
/// </summary>
public sealed class GameWindow : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;

    // 3D demo resources
    private ShaderProgram? _shader;
    private MeshRenderer? _mesh;
    private GpuTexture? _texture;
    private double _elapsed;

    // login / UI resources
    private GameContext? _context;
    private UiRenderer? _uiRenderer;

    private int _frameCount;

    public string Title { get; init; } = "OpenKO (C# / Silk.NET)";
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 768;

    /// <summary>Which scene to display. Defaults to the login screen.</summary>
    public WindowScene Scene { get; init; } = WindowScene.Login;

    /// <summary>If &gt; 0, the window auto-closes after this many rendered frames (for headless smoke tests).</summary>
    public int MaxFrames { get; init; }

    /// <summary>If set, the final rendered frame is captured to this path as a BMP before closing.</summary>
    public string? ScreenshotPath { get; init; }

    public void Run()
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(Width, Height),
            Title = Title,
            VSync = true,
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += OnResize;
        _window.Closing += OnClosing;
        _window.Run();
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _input = _window!.CreateInput();

        foreach (IKeyboard keyboard in _input.Keyboards)
            keyboard.KeyDown += OnKeyDown;

        _gl.ClearColor(Color.FromArgb(255, 12, 16, 28));
        _gl.Enable(EnableCap.DepthTest);

        if (Scene == WindowScene.Demo3D)
        {
            _shader = new ShaderProgram(_gl, Shaders.Vertex, Shaders.Fragment);
            _mesh = new MeshRenderer(_gl, DemoScene.CreateQuad());
            _texture = new GpuTexture(_gl, DemoScene.CreateCheckerboard());
        }
        else
        {
            _uiRenderer = new UiRenderer(_gl, Width, Height);
            _context = new GameContext { UiRenderer = _uiRenderer };
            _context.Procedures.SetActive(new LoginProcedure());
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        if (key == Key.Escape)
            _window?.Close();
    }

    private void OnResize(Vector2D<int> size)
    {
        _gl?.Viewport(size);
        _uiRenderer?.Resize(size.X, size.Y);
    }

    private void OnRender(double delta)
    {
        _elapsed += delta;
        _gl!.Clear((uint)ClearBufferMask.ColorBufferBit | (uint)ClearBufferMask.DepthBufferBit);

        if (Scene == WindowScene.Demo3D)
            RenderDemo();
        else
            RenderLogin((float)delta);

        if (MaxFrames > 0 && ++_frameCount >= MaxFrames)
        {
            if (ScreenshotPath != null)
                CaptureScreenshot(ScreenshotPath);
            _window?.Close();
        }
    }

    private unsafe void CaptureScreenshot(string path)
    {
        Vector2D<int> fb = _window!.FramebufferSize;
        int w = fb.X, h = fb.Y;
        var pixels = new byte[w * h * 4];

        fixed (byte* p = pixels)
            _gl!.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            PngWriter.WriteRgbaBottomUp(path, w, h, pixels);
        else
            BmpWriter.WriteRgbaBottomUp(path, w, h, pixels);

        Console.WriteLine($"Saved screenshot to {path} ({w}x{h}).");
    }

    private void RenderLogin(float delta)
    {
        if (_context == null)
            return;

        _context.PumpNetwork();
        _context.Procedures.TickActive(delta);
        _context.Procedures.RenderActive();
    }

    private void RenderDemo()
    {
        if (_shader == null || _mesh == null || _texture == null)
            return;

        _shader.Use();

        float aspect = Height == 0 ? 1f : (float)Width / Height;
        Matrix4x4 model = Matrix4x4.CreateRotationY((float)_elapsed);
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 4), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.1f, 100f);

        _shader.SetUniform("uModel", model);
        _shader.SetUniform("uView", view);
        _shader.SetUniform("uProjection", projection);
        _shader.SetUniform("uTexture", 0);

        _texture.Bind(TextureUnit.Texture0);
        _mesh.Draw();
    }

    private void OnClosing()
    {
        _mesh?.Dispose();
        _texture?.Dispose();
        _shader?.Dispose();
        _uiRenderer?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
    }

    public void Dispose()
    {
        _window?.Dispose();
    }
}
