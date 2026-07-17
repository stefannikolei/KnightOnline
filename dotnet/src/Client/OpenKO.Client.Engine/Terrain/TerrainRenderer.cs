using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Terrain;

/// <summary>
/// Device layer for the terrain: builds every level-1 patch through the pure
/// <see cref="TerrainVertexBuilder"/>, resolves the colormap grid (.tct) and
/// tile textures (.gtt), and draws each tile with the passes chosen by
/// <see cref="TilePassPlanner"/> — colormap tiles as a DualTexture modulate,
/// tile-textured cells as opaque + additive draws, exactly like
/// <c>CN3TerrainPatch::Render</c>.
///
/// The Misc\Terrain_Base.bmp detail overlay (the second stage of the tile-less
/// draw) and the baked lightmaps streamed from the sibling .tlt file are wired
/// in here: the lightmaps are loaded whole up front (keyed by global tile) and
/// each lightmap-carrying tile gets a trailing alpha-blended overlay draw. The
/// runtime 3×3 sliding-window paging (CN3Terrain::SetLightMap) is not
/// reproduced — the static full load is equivalent for rendering.
/// </summary>
public sealed class TerrainRenderer : IDisposable
{
    private sealed class Patch
    {
        public required TerrainVertex[] Vertices { get; init; }

        public required TerrainTile[] Tiles { get; init; }

        public required Texture2D? ColorMap { get; init; }

        public System.Numerics.Vector3 Center { get; init; }

        public float Radius { get; init; }
    }

    private const int PatchPixelSize = 32;   // PATCH_PIXEL_SIZE
    private const int ColorMapTexSize = 128; // COLORMAPTEX_SIZE

    private readonly N3Terrain _terrain;
    private readonly List<Patch> _patches = [];
    private readonly List<Texture2D> _ownedTextures = [];
    private readonly Texture2D?[] _tileTextures;
    private readonly Dictionary<(int X, int Z), Texture2D> _lightMaps = [];
    private readonly Texture2D? _baseDetail;
    private readonly BasicEffect _basic;
    private readonly DualTextureEffect _dual;

    public TerrainRenderer(GraphicsDevice device, N3Terrain terrain, KoPathResolver resolver, string zonePath)
    {
        _terrain = terrain;
        _basic = new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = false, LightingEnabled = false };
        _dual = new DualTextureEffect(device);

        Texture2D?[] colorMap = LoadColorMap(device, zonePath);
        _tileTextures = LoadTileTextures(device, terrain, resolver, zonePath);
        _baseDetail = LoadBaseDetail(device, resolver);
        LoadLightMaps(device, zonePath);

        int numColorMap = ColorMapCount();
        for (int px = 0; px < terrain.PatchMapSize; px++)
        {
            for (int pz = 0; pz < terrain.PatchMapSize; pz++)
            {
                TerrainPatchMesh mesh = TerrainVertexBuilder.BuildLevel1(
                    terrain, px * TerrainVertexBuilder.PatchTileSize, pz * TerrainVertexBuilder.PatchTileSize);

                int cx = px * PatchPixelSize / ColorMapTexSize;
                int cz = pz * PatchPixelSize / ColorMapTexSize;
                Texture2D? patchColorMap = (numColorMap > 0 && cx < numColorMap && cz < numColorMap)
                    ? colorMap[cx * numColorMap + cz]
                    : null;

                float middleY = terrain.PatchMiddleY[px * terrain.PatchMapSize + pz];
                float radius = terrain.PatchRadius[px * terrain.PatchMapSize + pz];
                var center = new System.Numerics.Vector3(
                    (px * TerrainVertexBuilder.PatchTileSize + 4) * TerrainVertexBuilder.TileSize,
                    middleY,
                    (pz * TerrainVertexBuilder.PatchTileSize + 4) * TerrainVertexBuilder.TileSize);

                _patches.Add(new Patch
                {
                    Vertices = mesh.Vertices,
                    Tiles = mesh.Tiles,
                    ColorMap = patchColorMap,
                    Center = center,
                    Radius = radius,
                });
            }
        }
    }

    /// <summary>Number of patches rendered this frame after culling.</summary>
    public int LastRenderedPatches { get; private set; }

    public void Render(GraphicsDevice device, N3EngineCamera camera)
    {
        Matrix view = camera.View.ToXna();
        Matrix projection = camera.Projection.ToXna();

        _basic.World = Matrix.Identity;
        _basic.View = view;
        _basic.Projection = projection;
        _dual.World = Matrix.Identity;
        _dual.View = view;
        _dual.Projection = projection;
        // DualTextureEffect is Modulate2X; halve the diffuse to match D3D's 1x MODULATE.
        _dual.DiffuseColor = new Vector3(DualTextureCompensation.DiffuseRgbScale);

        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.SamplerStates[0] = SamplerState.LinearWrap;
        device.SamplerStates[1] = SamplerState.LinearWrap;

        LastRenderedPatches = 0;
        foreach (Patch patch in _patches)
        {
            // Per-patch frustum cull (CheckRenderablePatch uses radius * 2).
            if (camera.Frustum.IsOutOfFrustum(patch.Center, patch.Radius * 2f))
                continue;
            LastRenderedPatches++;

            foreach (TerrainTile tile in patch.Tiles)
                DrawTile(device, patch, tile);
        }
    }

    private void DrawTile(GraphicsDevice device, Patch patch, TerrainTile tile)
    {
        bool hasLightMap = _lightMaps.ContainsKey((tile.TileX, tile.TileZ));
        IReadOnlyList<TerrainPass> passes = TilePassPlanner.Plan(
            tile.Tex1Idx, tile.Tex2Idx, tile.IsTileFull, _tileTextures.Length, hasLightMap);

        // The tile's four-vertex fan → two triangles referencing the patch VB.
        short[] indices =
        [
            (short)tile.BaseVertex, (short)(tile.BaseVertex + 1), (short)(tile.BaseVertex + 2),
            (short)tile.BaseVertex, (short)(tile.BaseVertex + 2), (short)(tile.BaseVertex + 3),
        ];

        foreach (TerrainPass pass in passes)
        {
            Texture2D? primary = Resolve(patch, pass.Primary, tile);

            device.BlendState = pass.Blend switch
            {
                TerrainPassBlend.Additive => BlendState.Additive,
                TerrainPassBlend.AlphaBlend => BlendState.NonPremultiplied,
                _ => BlendState.Opaque,
            };

            if (pass.Secondary.HasValue)
            {
                Texture2D? secondary = Resolve(patch, pass.Secondary.Value, tile);
                _dual.Texture = primary;
                _dual.Texture2 = secondary ?? primary;
                Draw(device, _dual, patch.Vertices, indices);
            }
            else
            {
                _basic.Texture = primary;
                _basic.TextureEnabled = primary != null;
                Draw(device, _basic, patch.Vertices, indices);
            }
        }
    }

    private Texture2D? Resolve(Patch patch, TerrainTextureSource source, TerrainTile tile) => source switch
    {
        TerrainTextureSource.ColorMap => patch.ColorMap,
        TerrainTextureSource.BaseDetail => _baseDetail,
        TerrainTextureSource.Tile0 => TileTexture(tile.Tex1Idx),
        TerrainTextureSource.Tile1 => TileTexture(tile.Tex2Idx),
        TerrainTextureSource.LightMap =>
            _lightMaps.TryGetValue((tile.TileX, tile.TileZ), out Texture2D? lm) ? lm : null,
        _ => null,
    };

    private Texture2D? TileTexture(int index)
        => (uint)index < (uint)_tileTextures.Length ? _tileTextures[index] : null;

    private static void Draw(GraphicsDevice device, Effect effect, TerrainVertex[] vertices, short[] indices)
    {
        foreach (EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, 2, TerrainVertex.VertexDeclaration);
        }
    }

    private int ColorMapCount()
        => (_terrain.PatchMapSize * PatchPixelSize) / ColorMapTexSize;

    private Texture2D?[] LoadColorMap(GraphicsDevice device, string zonePath)
    {
        int count = ColorMapCount();
        if (count <= 0)
            return [];

        string tct = Path.ChangeExtension(zonePath, ".tct");
        var textures = new Texture2D?[count * count];
        if (!File.Exists(tct))
            return textures;

        try
        {
            using FileStream stream = File.OpenRead(tct);
            using var reader = new BinaryReader(stream);
            for (int x = 0; x < count; x++)
            {
                for (int z = 0; z < count; z++)
                {
                    if (stream.Position >= stream.Length)
                        return textures;
                    Texture2D? texture = TryLoadTexture(device, reader);
                    textures[x * count + z] = texture;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{tct}: {ex.Message}");
        }

        return textures;
    }

    private Texture2D?[] LoadTileTextures(
        GraphicsDevice device, N3Terrain terrain, KoPathResolver resolver, string zonePath)
    {
        var textures = new Texture2D?[terrain.TileTextures.Count];
        // Group by source so each .gtt is opened once and read sequentially.
        string zoneDir = Path.GetDirectoryName(zonePath) ?? ".";

        for (int i = 0; i < terrain.TileTextures.Count; i++)
        {
            (short srcIdx, short tileIdx) = terrain.TileTextures[i];
            if (srcIdx < 0 || srcIdx >= terrain.TileTexSources.Count)
                continue;

            string source = terrain.TileTexSources[srcIdx];
            string? full = resolver.Resolve(source) ?? ResolveNextToZone(zoneDir, source);
            if (full == null || !File.Exists(full))
                continue;

            try
            {
                using FileStream stream = File.OpenRead(full);
                using var reader = new BinaryReader(stream);
                for (int j = 0; j < tileIdx; j++)
                    SkipTexture(reader); // CN3Texture::SkipFileHandle
                textures[i] = TryLoadTexture(device, reader);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{full}: {ex.Message}");
            }
        }

        return textures;
    }

    /// <summary>
    /// Loads Misc\Terrain_Base.bmp, the shared detail overlay
    /// (CN3Terrain::Load: m_pBaseTex.LoadFromFile). It is a plain 24-bit BMP,
    /// not an NTF container, so it goes through the image loader rather than
    /// N3Texture.
    /// </summary>
    private Texture2D? LoadBaseDetail(GraphicsDevice device, KoPathResolver resolver)
    {
        string? full = resolver.Resolve(@"Misc\Terrain_Base.bmp");
        if (full == null || !File.Exists(full))
            return null;

        try
        {
            using FileStream stream = File.OpenRead(full);
            Texture2D texture = Texture2D.FromStream(device, stream);
            _ownedTextures.Add(texture);
            return texture;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{full}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads the sibling .tlt lightmap file whole (all patches, not the runtime
    /// 3×3 window), uploads each baked lightmap and keys it by global tile so
    /// <see cref="Resolve"/> can bind it for the trailing alpha-blend pass.
    /// </summary>
    private void LoadLightMaps(GraphicsDevice device, string zonePath)
    {
        if (_terrain.PatchMapSize <= 0)
            return;

        string tlt = Path.ChangeExtension(zonePath, ".tlt");
        if (!File.Exists(tlt))
            return;

        try
        {
            var file = new N3TerrainLightMapFile { FileFormatVersion = _terrain.FileFormatVersion };
            using (FileStream stream = File.OpenRead(tlt))
            using (var reader = new BinaryReader(stream))
            {
                file.Load(reader, _terrain.PatchMapSize);
            }

            foreach ((int tx, int tz, N3Texture n3) in file.EnumerateGlobalTiles())
            {
                Texture2D texture = TextureFactory.FromN3Texture(device, n3);
                _ownedTextures.Add(texture);
                _lightMaps[(tx, tz)] = texture;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{tlt}: {ex.Message}");
        }
    }

    private static string? ResolveNextToZone(string zoneDir, string source)
    {
        string name = Path.GetFileName(source.Replace('\\', '/'));
        string candidate = Path.Combine(zoneDir, name);
        return File.Exists(candidate) ? candidate : null;
    }

    private Texture2D? TryLoadTexture(GraphicsDevice device, BinaryReader reader)
    {
        var n3 = new N3Texture { FileFormatVersion = _terrain.FileFormatVersion };
        n3.Load(reader);
        Texture2D texture = TextureFactory.FromN3Texture(device, n3);
        _ownedTextures.Add(texture);
        return texture;
    }

    /// <summary>Advances the reader past one texture without uploading it.</summary>
    private void SkipTexture(BinaryReader reader)
    {
        var n3 = new N3Texture { FileFormatVersion = _terrain.FileFormatVersion };
        n3.Load(reader); // Load consumes the same bytes as the C++ SkipFileHandle
    }

    public void Dispose()
    {
        foreach (Texture2D texture in _ownedTextures)
            texture.Dispose();
        _ownedTextures.Clear();
        _lightMaps.Clear();
        _patches.Clear();
        _basic.Dispose();
        _dual.Dispose();
    }
}
