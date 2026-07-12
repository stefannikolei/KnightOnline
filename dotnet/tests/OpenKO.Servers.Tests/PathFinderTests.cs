using OpenKO.Servers.AIServer;
using Xunit;

namespace OpenKO.Servers.Tests;

public class PathFinderTests
{
    /// <summary>Builds the x-major map layout SetMap expects (0 = walkable).</summary>
    private static int[] BuildMap(int sizeX, int sizeY, params (int X, int Y)[] blocked)
    {
        var map = new int[sizeX * sizeY];
        foreach ((int x, int y) in blocked)
            map[x * sizeY + y] = 1;
        return map;
    }

    [Fact]
    public void FindsStraightPath()
    {
        var finder = new PathFinder();
        finder.SetMap(10, 10, BuildMap(10, 10));

        var dest = finder.FindPath(1, 1, 5, 1);

        Assert.NotNull(dest);
        Assert.Equal(5, dest.X);
        Assert.Equal(1, dest.Y);

        var path = PathFinder.ToPath(dest);
        Assert.Equal((1, 1), path[0]);
        Assert.Equal((5, 1), path[^1]);

        // Every step moves to one of the 8 neighbors.
        for (int i = 1; i < path.Count; i++)
        {
            Assert.InRange(System.Math.Abs(path[i].X - path[i - 1].X), 0, 1);
            Assert.InRange(System.Math.Abs(path[i].Y - path[i - 1].Y), 0, 1);
        }
    }

    [Fact]
    public void RoutesAroundWall()
    {
        // Vertical wall at x=3 with a gap at y=6.
        var blocked = Enumerable.Range(0, 10).Where(y => y != 6).Select(y => (3, y)).ToArray();
        var finder = new PathFinder();
        finder.SetMap(10, 10, BuildMap(10, 10, blocked));

        var dest = finder.FindPath(1, 1, 6, 1);

        Assert.NotNull(dest);
        var path = PathFinder.ToPath(dest);

        // The path must pass through the gap column and never a blocked cell.
        Assert.Contains(path, p => p.X == 3);
        Assert.All(path, p => Assert.True(p.X != 3 || p.Y == 6));
    }

    [Fact]
    public void UnreachableDestinationReturnsNull()
    {
        // Destination fully enclosed.
        var blocked = new[] { (4, 3), (4, 5), (3, 4), (5, 4), (3, 3), (3, 5), (5, 3), (5, 5) };
        var finder = new PathFinder();
        finder.SetMap(10, 10, BuildMap(10, 10, blocked));

        Assert.Null(finder.FindPath(0, 0, 4, 4));
    }

    [Fact]
    public void OutOfBoundsIsNotWalkable()
    {
        var finder = new PathFinder();
        finder.SetMap(4, 4, BuildMap(4, 4));

        Assert.False(finder.IsBlankMap(-1, 0));
        Assert.False(finder.IsBlankMap(0, -1));
        Assert.False(finder.IsBlankMap(4, 0));
        Assert.True(finder.IsBlankMap(3, 3));
    }
}
