using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Input;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Scene;
using OpenKO.Client.Engine.Sky;
using OpenKO.Client.Engine.Terrain;

namespace OpenKO.Client.Viewer;

/// <summary>
/// Stage-6.7 scene: loads a zone's terrain (.gtd + colormap .tct + tile .gtt)
/// and the sky, then orbits the camera over the map. Left/Right cycle zones.
/// </summary>
public sealed class TerrainScene : IScene
{
    private readonly List<string> _zones = [];
    private TerrainRenderer? _terrain;
    private RiverRenderer? _river;
    private SkyRenderer? _sky;
    private int _index;
    private float _orbit;
    private float _mapWorldSize;

    public string Name => _terrain == null
        ? "Terrain (keine Daten)"
        : $"Terrain [{_index + 1}/{_zones.Count}] {(_zones.Count > 0 ? Path.GetFileNameWithoutExtension(_zones[_index]) : "-")}";

    public void Load(ViewerContext context)
    {
        if (context.DataPath == null)
            return;

        string zonesDir = Path.Combine(context.DataPath, "Zones");
        if (Directory.Exists(zonesDir))
        {
            _zones.AddRange(Directory
                .EnumerateFiles(zonesDir, "*.gtd", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                .Order(StringComparer.OrdinalIgnoreCase));
            // Start on OPENKO_ZONE if set, else a small quick-loading zone.
            string preferred = Environment.GetEnvironmentVariable("OPENKO_ZONE") ?? "arena";
            int start = _zones.FindIndex(z => Path.GetFileNameWithoutExtension(z)
                .Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (start >= 0)
                _index = start;
        }

        LoadCurrent(context);
    }

    private void LoadCurrent(ViewerContext context)
    {
        _terrain?.Dispose();
        _terrain = null;
        _river?.Dispose();
        _river = null;
        _sky?.Dispose();
        _sky = null;

        if (context.DataPath == null || _zones.Count == 0)
            return;

        try
        {
            var terrain = new N3Terrain();
            terrain.LoadFromFile(_zones[_index]);
            var resolver = new KoPathResolver(context.DataPath);
            _terrain = new TerrainRenderer(context.Device, terrain, resolver, _zones[_index]);
            _river = new RiverRenderer(context.Device, terrain, resolver);
            _mapWorldSize = terrain.MapSize * TerrainVertexBuilder.TileSize;
            _sky = new SkyRenderer(context.Device);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{_zones[_index]}: {ex.Message}");
        }
    }

    public void Tick(ViewerContext context)
    {
        _orbit += context.Timer.SecPerFrame * 0.1f;
        _sky?.Tick(context.Timer.SecPerFrame);

        if (_zones.Count > 1 && context.Input.IsKeyPress(KeyMap.DIK_RIGHT))
        {
            _index = (_index + 1) % _zones.Count;
            LoadCurrent(context);
        }

        if (_zones.Count > 1 && context.Input.IsKeyPress(KeyMap.DIK_LEFT))
        {
            _index = (_index - 1 + _zones.Count) % _zones.Count;
            LoadCurrent(context);
        }
    }

    public void Render(ViewerContext context)
    {
        GraphicsDevice device = context.Device;
        device.Clear(ColorInterop.FromArgb(SkyGeometry.DefaultFogColor));
        if (_terrain == null)
            return;

        float half = _mapWorldSize * 0.5f;
        var center = new System.Numerics.Vector3(half, 0f, half);
        float radius = MathF.Max(_mapWorldSize * 0.35f, 60f);

        var camera = new N3EngineCamera
        {
            Eye = center + new System.Numerics.Vector3(
                MathF.Sin(_orbit) * radius, MathF.Max(_mapWorldSize * 0.25f, 40f), MathF.Cos(_orbit) * radius),
            At = center,
            Fov = N3EngineCamera.GameFov,
            Aspect = device.Viewport.AspectRatio,
            NearPlane = 1f,
            FarPlane = MathF.Max(_mapWorldSize * 2f, 1024f),
        };
        camera.Update();

        // Sky first (Z off), then terrain, then water over it.
        _sky?.Render(device, camera);
        _terrain.Render(device, camera);
        if (_river != null)
        {
            _river.Tick(camera, context.Timer.SecPerFrame);
            _river.Render(device, camera);
        }
    }

    public void Unload()
    {
        _terrain?.Dispose();
        _terrain = null;
        _river?.Dispose();
        _river = null;
        _sky?.Dispose();
        _sky = null;
        _zones.Clear();
        _index = 0;
    }
}
