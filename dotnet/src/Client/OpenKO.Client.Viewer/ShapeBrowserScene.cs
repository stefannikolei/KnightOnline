using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Input;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Objects;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Viewer;

/// <summary>
/// Stage-6.3 scene: browses the .n3shape corpus with full material handling
/// (alpha manager, texture animation, billboard/windy parts).
/// </summary>
public sealed class ShapeBrowserScene : IScene
{
    private readonly List<string> _shapeFiles = [];
    private readonly AlphaManager _alphaManager = new();
    private BasicEffect? _effect;
    private KoPathResolver? _resolver;
    private TextureCache? _textures;
    private PMeshCache? _meshes;
    private ShapeRenderer? _renderer;
    private int _index;
    private float _orbit;

    public string Name => _renderer == null
        ? "Shape-Browser (keine Daten)"
        : $"Shape-Browser [{_index + 1}/{_shapeFiles.Count}] {Path.GetFileName(_shapeFiles[_index])}";

    public void Load(ViewerContext context)
    {
        _effect = new BasicEffect(context.Device);
        _effect.EnableDefaultLighting();

        if (context.DataPath != null)
        {
            _resolver = new KoPathResolver(context.DataPath);
            _textures = new TextureCache(context.Device, _resolver);
            _meshes = new PMeshCache(_resolver);
            _shapeFiles.AddRange(Directory
                .EnumerateFiles(context.DataPath, "*.n3shape", new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                })
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        LoadCurrent();
    }

    private void LoadCurrent()
    {
        _renderer = null;
        if (_shapeFiles.Count == 0 || _meshes == null || _textures == null)
            return;

        try
        {
            var shape = new N3Shape();
            shape.LoadFromFile(_shapeFiles[_index]);
            _renderer = new ShapeRenderer(shape, _meshes, _textures, new Random(12345));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{_shapeFiles[_index]}: {ex.Message}");
        }
    }

    public void Tick(ViewerContext context)
    {
        _orbit += context.Timer.SecPerFrame * 0.4f;

        if (_shapeFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_RIGHT))
        {
            _index = (_index + 1) % _shapeFiles.Count;
            LoadCurrent();
        }

        if (_shapeFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_LEFT))
        {
            _index = (_index - 1 + _shapeFiles.Count) % _shapeFiles.Count;
            LoadCurrent();
        }
    }

    public void Render(ViewerContext context)
    {
        GraphicsDevice device = context.Device;
        device.Clear(new Color(40, 44, 52));
        if (_renderer == null || _effect == null)
            return;

        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.BlendState = BlendState.Opaque;
        device.SamplerStates[0] = SamplerState.LinearWrap;

        var camera = new N3EngineCamera
        {
            Eye = _renderer.Shape.Position + new System.Numerics.Vector3(
                MathF.Sin(_orbit) * 20f, 8f, MathF.Cos(_orbit) * 20f),
            At = _renderer.Shape.Position + new System.Numerics.Vector3(0f, 2f, 0f),
            Fov = N3EngineCamera.GameFov,
            Aspect = device.Viewport.AspectRatio,
            NearPlane = 0.3f,
            FarPlane = 256f,
        };
        camera.Update();

        _renderer.Tick(camera, context.Timer);

        _effect.View = camera.View.ToXna();
        _effect.Projection = camera.Projection.ToXna();

        _renderer.Render(device, _effect, _alphaManager, camera);
        _alphaManager.Render(device, _effect);
    }

    public void Unload()
    {
        _textures?.Dispose();
        _textures = null;
        _effect?.Dispose();
        _effect = null;
        _shapeFiles.Clear();
        _renderer = null;
        _alphaManager.Clear();
        _index = 0;
    }
}
