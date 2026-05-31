using System.Drawing;
using System.Numerics;
using OpenKO.Client.Rendering;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace OpenKO.Client;

/// <summary>
/// Cross-platform application window, the eventual replacement for the original Win32/DirectX 9
/// host (WarFareMain.cpp / CGameEng). It opens an OpenGL context and drives the ported render path:
/// for now it shows a textured, lit demo mesh (built procedurally) through the same
/// <see cref="MeshRenderer"/> / <see cref="GpuTexture"/> / <see cref="ShaderProgram"/> that real
/// N3 assets will use.
/// </summary>
public sealed class GameWindow : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;

    private ShaderProgram? _shader;
    private MeshRenderer? _mesh;
    private GpuTexture? _texture;
    private double _elapsed;
    private int _frameCount;

    public string Title { get; init; } = "OpenKO (C# / Silk.NET)";
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 768;

    /// <summary>If &gt; 0, the window auto-closes after this many rendered frames (for headless smoke tests).</summary>
    public int MaxFrames { get; init; }

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

        _shader = new ShaderProgram(_gl, Shaders.Vertex, Shaders.Fragment);
        _mesh = new MeshRenderer(_gl, DemoScene.CreateQuad());
        _texture = new GpuTexture(_gl, DemoScene.CreateCheckerboard());
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        if (key == Key.Escape)
            _window?.Close();
    }

    private void OnResize(Vector2D<int> size)
    {
        _gl?.Viewport(size);
    }

    private void OnRender(double delta)
    {
        _elapsed += delta;

        _gl!.Clear((uint)ClearBufferMask.ColorBufferBit | (uint)ClearBufferMask.DepthBufferBit);

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

        if (MaxFrames > 0 && ++_frameCount >= MaxFrames)
            _window?.Close();
    }

    private void OnClosing()
    {
        _mesh?.Dispose();
        _texture?.Dispose();
        _shader?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
    }

    public void Dispose()
    {
        _window?.Dispose();
    }
}
