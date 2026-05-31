using System.Drawing;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace OpenKO.Client;

/// <summary>
/// Cross-platform application window, the eventual replacement for the original Win32/DirectX 9
/// host (WarFareMain.cpp / CGameEng). For now it opens a GL context and clears the framebuffer,
/// establishing the windowing + render-loop skeleton that the ported renderer will plug into.
/// </summary>
public sealed class GameWindow : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;

    public string Title { get; init; } = "OpenKO (C# / Silk.NET)";
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 768;

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
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        if (key == Key.Escape)
            _window?.Close();
    }

    private void OnRender(double delta)
    {
        _gl!.Clear((uint)ClearBufferMask.ColorBufferBit | (uint)ClearBufferMask.DepthBufferBit);
        // TODO: drive the ported N3 scene/UI renderer here.
    }

    private void OnClosing()
    {
        _input?.Dispose();
        _gl?.Dispose();
    }

    public void Dispose()
    {
        _window?.Dispose();
    }
}
