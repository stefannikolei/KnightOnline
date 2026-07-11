using System.Numerics;
using OpenKO.GameData.Maps;
using Xunit;

namespace OpenKO.GameData.Tests;

/// <summary>
/// Loads the real .smd map files shipped in Server/bin/MAP and validates the
/// parser end-to-end: full-file consumption, structural invariants, and the
/// mathematical identity that the terrain interpolation returns the stored
/// heightmap value exactly at grid corners.
/// </summary>
public class GameMapTests
{
    /// <summary>Walks up from the test binary to the repo root (contains Server/bin/MAP).</summary>
    private static string? FindMapDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Server", "bin", "MAP");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    public static TheoryData<string> SmdFiles()
    {
        var data = new TheoryData<string>();
        string? mapDir = FindMapDirectory();
        if (mapDir is not null)
        {
            foreach (string file in Directory.GetFiles(mapDir, "*.smd"))
                data.Add(file);
        }

        if (data.Count == 0)
            data.Add(""); // placeholder so the theory doesn't fail on discovery

        return data;
    }

    [Theory]
    [MemberData(nameof(SmdFiles))]
    public void LoadsEveryShippedSmdCompletely(string path)
    {
        if (path.Length == 0)
            return; // repo maps not available in this environment

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        GameMap map = GameMap.Load(reader);

        // The parser must consume the file exactly — any format misunderstanding
        // would leave bytes behind or run past the end.
        Assert.Equal(stream.Length, stream.Position);

        // Structural sanity.
        Assert.InRange(map.MapSize, 2, 4097);
        Assert.True(map.UnitDistance > 0);
        Assert.Equal((map.MapSize - 1) * map.UnitDistance, map.ShapeManager.MapWidth);
        Assert.True(map.ShapeManager.CollisionFaceCount >= 0);

        // Terrain interpolation must return the stored value exactly on grid corners
        // (dX == 0, dZ == 0 → h1 by construction).
        int mid = map.MapSize / 2;
        float expected = map.TerrainHeight[mid, mid];
        float actual = map.GetTerrainHeight(mid * map.UnitDistance, mid * map.UnitDistance);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CollisionHeightIsPlausibleOnRealMap()
    {
        string? mapDir = FindMapDirectory();
        if (mapDir is null)
            return;

        string? smd = Directory.GetFiles(mapDir, "*.smd").FirstOrDefault();
        if (smd is null)
            return;

        GameMap map = GameMap.Load(smd);

        if (map.ShapeManager.CollisionFaceCount == 0)
            return;

        // Sample the map: wherever collision geometry yields a height, it must be
        // within the vertical bounds of the collision vertex set.
        float minY = map.ShapeManager.Collisions.Min(v => v.Y);
        float maxY = map.ShapeManager.Collisions.Max(v => v.Y);

        int samples = 0;
        for (float x = 8; x < map.ShapeManager.MapWidth && samples < 500; x += 64)
        {
            for (float z = 8; z < map.ShapeManager.MapLength && samples < 500; z += 64)
            {
                float height = map.ShapeManager.GetHeight(x, z);
                if (height != float.MinValue)
                {
                    Assert.InRange(height, minY - 0.01f, maxY + 0.01f);
                    samples++;
                }
            }
        }
    }

    [Fact]
    public void IntersectTriangleMatchesReferenceSemantics()
    {
        // The C++ front-face test rejects when Cross(e1,e2)·dir > -0.0001, so a
        // downward ray hits the winding whose normal points up: (v0, v2, v1) here.
        var v0 = new Vector3(0, 10, 0);
        var v1 = new Vector3(10, 10, 0);
        var v2 = new Vector3(0, 10, 10);

        bool hit = OpenKO.GameData.Math.KoMath.IntersectTriangle(
            new Vector3(2, 5000, 2), new Vector3(0, -1, 0),
            v0, v2, v1, out float t, out _, out _, out Vector3 col);

        Assert.True(hit);
        Assert.Equal(4990, t, precision: 2);
        Assert.Equal(10, col.Y, precision: 3);

        // The opposite winding is a backface for this ray and must be culled.
        bool backface = OpenKO.GameData.Math.KoMath.IntersectTriangle(
            new Vector3(2, 5000, 2), new Vector3(0, -1, 0),
            v0, v1, v2, out _, out _, out _, out _);

        Assert.False(backface);
    }
}
