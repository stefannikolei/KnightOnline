using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.World;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-7.6 pins: terrain height, entity interpolation and the game camera.</summary>
public class GameplayTests
{
    private static N3Terrain FlatOrSlopedTerrain(int mapSize, Func<int, int, float> height)
    {
        var data = new N3MapData[mapSize * mapSize];
        for (int x = 0; x < mapSize; x++)
            for (int z = 0; z < mapSize; z++)
                data[x * mapSize + z] = new N3MapData { Height = height(x, z) };
        var terrain = new N3Terrain();
        terrain.Initialize(mapSize, data, new byte[mapSize * mapSize]);
        return terrain;
    }

    [Fact]
    public void GetHeight_FlatTerrain_ReturnsConstant()
    {
        N3Terrain terrain = FlatOrSlopedTerrain(8, (_, _) => 5f);
        Assert.Equal(5f, TerrainCollision.GetHeight(terrain, 10f, 6f), 3);
    }

    [Fact]
    public void GetHeight_SlopedTerrain_InterpolatesLinearly()
    {
        // Height rises 1 unit per tile in +x (h = ix). At tile centre x the
        // interpolated height tracks x / TILE_SIZE.
        N3Terrain terrain = FlatOrSlopedTerrain(8, (x, _) => x);

        // At x = 4 (tile boundary ix=1) height should be 1; at x = 6 → 1.5.
        Assert.Equal(1.0f, TerrainCollision.GetHeight(terrain, 4f, 4f), 3);
        Assert.Equal(1.5f, TerrainCollision.GetHeight(terrain, 6f, 4f), 3);
    }

    [Fact]
    public void GetHeight_OutOfRange_ReturnsSentinel()
    {
        N3Terrain terrain = FlatOrSlopedTerrain(8, (_, _) => 0f);
        // (int)(-10)/4 == -2 (C truncation toward zero) → ix < 0 → out of range.
        Assert.Equal(TerrainCollision.OutOfRange, TerrainCollision.GetHeight(terrain, -10f, 4f));
        Assert.Equal(TerrainCollision.OutOfRange, TerrainCollision.GetHeight(terrain, 1000f, 4f));
    }

    [Fact]
    public void MoveTowards_StepsThenSnaps()
    {
        var from = new Vector3(0, 0, 0);
        var to = new Vector3(10, 0, 0);

        // speed 4 m/s over 1 s → moves 4 m, not yet arrived.
        Vector3 mid = EntityInterpolator.MoveTowards(from, to, 4f, 1f, out bool arrived1);
        Assert.Equal(4f, mid.X, 3);
        Assert.False(arrived1);

        // A big step snaps to the target and reports arrival.
        Vector3 end = EntityInterpolator.MoveTowards(mid, to, 100f, 1f, out bool arrived2);
        Assert.Equal(to, end);
        Assert.True(arrived2);
    }

    [Fact]
    public void GameCamera_ZoomAndPitchAreClamped()
    {
        var cam = new GameCamera { Target = Vector3.Zero };

        cam.Zoom(1000f); // way in
        Assert.Equal(GameCamera.MinDistance, cam.Distance, 3);
        cam.Zoom(-1000f); // way out
        Assert.Equal(GameCamera.MaxDistance, cam.Distance, 3);

        cam.Rotate(0f, 100f); // pitch clamped
        Assert.True(cam.Pitch <= 1.4f + 0.001f);
    }

    [Fact]
    public void GameCamera_EyeOrbitsTargetAtDistance()
    {
        var cam = new GameCamera { Target = new Vector3(100, 10, 100) };
        cam.Zoom(0f); // keep default distance

        float radius = (cam.Eye - cam.Target).Length();
        Assert.Equal(cam.Distance, radius, 3);
        Assert.Equal(cam.Target, cam.At);
    }
}
