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
/// Stage-6.2 scene: browses the .n3pmesh corpus with an orbiting camera.
/// Left/Right switches meshes, Up/Down forces the LOD, a same-named .dxt
/// next to the mesh is used as texture when present.
/// </summary>
public sealed class MeshBrowserScene : IScene
{
    private readonly List<string> _meshFiles = [];
    private BasicEffect? _effect;
    private TextureCache? _textures;
    private KoPathResolver? _resolver;
    private PMeshInstanceRenderer? _renderer;
    private N3PMesh? _mesh;
    private int _index;
    private float _orbit;
    private int _forcedVertices = -1;

    public string Name => _mesh == null
        ? "Mesh-Browser (keine Daten)"
        : $"Mesh-Browser [{_index + 1}/{_meshFiles.Count}] {Path.GetFileName(_meshFiles[_index])} " +
          $"({_renderer!.Instance.NumVertices}/{_mesh.MaxNumVertices} Vertizes)";

    public void Load(ViewerContext context)
    {
        _effect = new BasicEffect(context.Device)
        {
            TextureEnabled = false,
            VertexColorEnabled = false,
        };
        _effect.EnableDefaultLighting();

        if (context.DataPath != null)
        {
            _resolver = new KoPathResolver(context.DataPath);
            _textures = new TextureCache(context.Device, _resolver);
            _meshFiles.AddRange(Directory
                .EnumerateFiles(context.DataPath, "*.n3pmesh", new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                })
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        LoadCurrent(context);
    }

    private void LoadCurrent(ViewerContext context)
    {
        _renderer = null;
        _mesh = null;
        if (_meshFiles.Count == 0)
            return;

        string path = _meshFiles[_index];
        try
        {
            var mesh = new N3PMesh();
            mesh.LoadFromFile(path);
            _mesh = mesh;
            _renderer = new PMeshInstanceRenderer(mesh);
            _renderer.SetLodByNumVertices(int.MaxValue);
            _forcedVertices = -1;

            // Heuristic: a same-named texture next to the mesh.
            string texPath = Path.ChangeExtension(path, ".dxt");
            Texture2D? texture = null;
            if (File.Exists(texPath) && _textures != null && _resolver != null)
                texture = _textures.Get(Path.GetRelativePath(_resolver.RootPath, texPath));

            _effect!.Texture = texture;
            _effect.TextureEnabled = texture != null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{path}: {ex.Message}");
        }
    }

    public void Tick(ViewerContext context)
    {
        _orbit += context.Timer.SecPerFrame * 0.6f;

        if (_meshFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_RIGHT))
        {
            _index = (_index + 1) % _meshFiles.Count;
            LoadCurrent(context);
        }

        if (_meshFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_LEFT))
        {
            _index = (_index - 1 + _meshFiles.Count) % _meshFiles.Count;
            LoadCurrent(context);
        }

        if (_mesh != null && _renderer != null)
        {
            if (context.Input.IsKeyPress(KeyMap.DIK_DOWN))
            {
                _forcedVertices = _forcedVertices < 0 ? _mesh.MaxNumVertices : _forcedVertices;
                _forcedVertices = Math.Max(_mesh.MinNumVertices, _forcedVertices - Math.Max(1, _mesh.MaxNumVertices / 10));
                _renderer.SetLodByNumVertices(_forcedVertices);
            }

            if (context.Input.IsKeyPress(KeyMap.DIK_UP))
            {
                _forcedVertices = _forcedVertices < 0 ? _mesh.MaxNumVertices : _forcedVertices;
                _forcedVertices = Math.Min(_mesh.MaxNumVertices, _forcedVertices + Math.Max(1, _mesh.MaxNumVertices / 10));
                _renderer.SetLodByNumVertices(_forcedVertices);
            }
        }
    }

    public void Render(ViewerContext context)
    {
        GraphicsDevice device = context.Device;
        device.Clear(new Color(32, 32, 40));
        if (_mesh == null || _renderer == null || _effect == null)
            return;

        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.BlendState = BlendState.Opaque;
        device.SamplerStates[0] = SamplerState.LinearWrap;

        // Orbit around the mesh bounds, LH camera like the C++ engine.
        System.Numerics.Vector3 center = (_mesh.Min + _mesh.Max) * 0.5f;
        float radius = MathF.Max(_mesh.Radius, 0.5f);
        var eye = center + new System.Numerics.Vector3(
            MathF.Sin(_orbit) * radius * 2.2f, radius * 0.9f, MathF.Cos(_orbit) * radius * 2.2f);

        var camera = new N3EngineCamera
        {
            Eye = eye,
            At = center,
            Fov = N3EngineCamera.GameFov,
            Aspect = device.Viewport.AspectRatio,
            NearPlane = 0.1f,
            FarPlane = MathF.Max(radius * 10f, 64f),
        };
        camera.Update();

        _effect.World = Microsoft.Xna.Framework.Matrix.Identity;
        _effect.View = camera.View.ToXna();
        _effect.Projection = camera.Projection.ToXna();

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _renderer.Draw(device);
        }
    }

    public void Unload()
    {
        _textures?.Dispose();
        _textures = null;
        _effect?.Dispose();
        _effect = null;
        _meshFiles.Clear();
        _renderer = null;
        _mesh = null;
        _index = 0;
    }
}
