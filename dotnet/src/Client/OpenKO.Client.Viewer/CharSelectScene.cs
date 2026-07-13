using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Objects;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Viewer;

/// <summary>
/// Stage-6.6 milestone: the char-select composition like the C++
/// (GameProcCharacterSelect) — background stage shape, up to four animated
/// characters, fog, alpha manager, camera FOV 0.96/NP 0.1/FP 100.
/// </summary>
public sealed class CharSelectScene : IScene
{
    private readonly AlphaManager _alphaManager = new();
    private BasicEffect? _effect;
    private ChrAssetCaches? _caches;
    private ShapeRenderer? _background;
    private readonly List<ChrRenderer> _chrs = [];
    private CharSelectSetup? _setup;

    public string Name => _setup == null
        ? "Char-Select (keine Daten)"
        : $"Char-Select — {_chrs.Count} Charaktere, Bg: {Path.GetFileName(_setup.BackgroundShapePath ?? "-")}";

    public void Load(ViewerContext context)
    {
        _effect = new BasicEffect(context.Device);
        _effect.EnableDefaultLighting();

        if (context.DataPath == null)
            return;

        _setup = CharSelectSetup.Compose(context.DataPath);
        var resolver = new KoPathResolver(context.DataPath);
        var textures = new TextureCache(context.Device, resolver);
        var meshes = new PMeshCache(resolver);
        _caches = new ChrAssetCaches(resolver, textures, meshes);

        if (_setup.BackgroundShapePath != null)
        {
            try
            {
                var shape = new N3Shape();
                shape.LoadFromFile(_setup.BackgroundShapePath);
                _background = new ShapeRenderer(shape, meshes, textures, new Random(7));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{_setup.BackgroundShapePath}: {ex.Message}");
            }
        }

        for (int slot = 0; slot < _setup.ChrPaths.Count; slot++)
        {
            try
            {
                var chr = new N3Chr();
                chr.LoadFromFile(_setup.ChrPaths[slot]);
                chr.Position = CharSelectSetup.SlotPosition(slot);
                _chrs.Add(new ChrRenderer(chr, _caches));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{_setup.ChrPaths[slot]}: {ex.Message}");
            }
        }
    }

    public void Tick(ViewerContext context)
    {
    }

    public void Render(ViewerContext context)
    {
        GraphicsDevice device = context.Device;
        device.Clear(Color.Black); // the C++ clears char select to black
        if (_effect == null || _setup == null)
            return;

        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.BlendState = BlendState.Opaque;
        device.SamplerStates[0] = SamplerState.LinearWrap;

        var camera = new N3EngineCamera
        {
            Eye = new System.Numerics.Vector3(0f, 1.6f, -9.5f),
            At = new System.Numerics.Vector3(0f, 1.2f, 0f),
            Fov = _setup.CameraFov,
            Aspect = device.Viewport.AspectRatio,
            NearPlane = _setup.CameraNearPlane,
            FarPlane = _setup.CameraFarPlane,
        };
        camera.Update();

        _effect.View = camera.View.ToXna();
        _effect.Projection = camera.Projection.ToXna();

        // Fog like the C++ camera Apply (EXP2 mapped to the linear fit).
        (float fogStart, float fogEnd) = FogMapper.FromFarPlane(camera.FarPlane);
        _effect.FogEnabled = true;
        _effect.FogColor = Vector3.Zero; // black clear → black fog
        _effect.FogStart = fogStart;
        _effect.FogEnd = fogEnd;

        if (_background != null)
        {
            _background.Tick(camera, context.Timer);
            _background.Render(device, _effect, _alphaManager, camera);
        }

        foreach (ChrRenderer chr in _chrs)
        {
            chr.Tick(camera, context.Timer);
            chr.Render(device, _effect);
        }

        _alphaManager.Render(device, _effect);
        _effect.FogEnabled = false;
    }

    public void Unload()
    {
        _caches?.Textures.Dispose();
        _caches = null;
        _effect?.Dispose();
        _effect = null;
        _background = null;
        _chrs.Clear();
        _alphaManager.Clear();
        _setup = null;
    }
}
