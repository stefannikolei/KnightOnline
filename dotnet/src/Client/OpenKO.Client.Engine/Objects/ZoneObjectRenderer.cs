using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets.Zones;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Renders a zone's placed static objects (the <c>.opd</c> shape list) — trees,
/// buildings, gates — via one <see cref="ShapeRenderer"/> per object, with a
/// shared alpha manager for the transparent parts. The client-side of
/// CN3TerrainManager's <c>m_pShapes-&gt;Render()</c>.
/// </summary>
public sealed class ZoneObjectRenderer
{
    private readonly List<ShapeRenderer> _shapes = [];
    private readonly AlphaManager _alpha = new();

    public ZoneObjectRenderer(ZoneObjectSet set, PMeshCache meshes, TextureCache textures)
    {
        // Deterministic seed so texture-animation phases are reproducible.
        var random = new Random(12345);
        foreach (ZoneObject obj in set.Objects)
        {
            try
            {
                _shapes.Add(new ShapeRenderer(obj.Shape, meshes, textures, random));
            }
            catch (Exception)
            {
                // A shape referencing a missing mesh/texture is skipped, not fatal.
            }
        }
    }

    public int Count => _shapes.Count;

    public void Tick(N3EngineCamera camera, FrameTimer timer)
    {
        foreach (ShapeRenderer shape in _shapes)
            shape.Tick(camera, timer);
    }

    public void Render(GraphicsDevice device, BasicEffect effect, N3EngineCamera camera)
    {
        _alpha.Clear();
        foreach (ShapeRenderer shape in _shapes)
            shape.Render(device, effect, _alpha, camera);
        _alpha.Render(device, effect);
    }
}
