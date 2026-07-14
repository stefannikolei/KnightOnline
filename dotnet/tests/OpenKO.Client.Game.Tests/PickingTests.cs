using System.Numerics;
using OpenKO.Client.Game.World;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Pins for click-target ray casting (Picking + WorldPicker).</summary>
public class PickingTests
{
    // A left-handed camera at the origin looking down +Z, matching the engine.
    private static (Matrix4x4 View, Matrix4x4 Proj) Camera()
    {
        var view = Matrix4x4.CreateLookAtLeftHanded(
            new Vector3(0, 0, 0), new Vector3(0, 0, 1), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(
            MathF.PI / 3f, 1f, 0.1f, 1000f);
        return (view, proj);
    }

    [Fact]
    public void ScreenCentreRay_PointsAlongViewDirection()
    {
        (Matrix4x4 view, Matrix4x4 proj) = Camera();
        PickRay ray = Picking.ScreenPointToRay(view, proj, 512, 384, 1024, 768);

        // Centre of the screen → ray down the +Z view axis.
        Assert.True(ray.Direction.Z > 0.99f);
        Assert.True(MathF.Abs(ray.Direction.X) < 1e-3f);
        Assert.True(MathF.Abs(ray.Direction.Y) < 1e-3f);
    }

    [Fact]
    public void RaySphere_HitsAheadAndMissesAside()
    {
        var ray = new PickRay(Vector3.Zero, new Vector3(0, 0, 1));
        Assert.NotNull(Picking.RaySphere(ray, new Vector3(0, 0, 10), 1f));
        Assert.Null(Picking.RaySphere(ray, new Vector3(5, 0, 10), 1f));   // off to the side
        Assert.Null(Picking.RaySphere(ray, new Vector3(0, 0, -10), 1f));  // behind
    }

    [Fact]
    public void PickNearest_PicksClosestEntityUnderCentre()
    {
        (Matrix4x4 view, Matrix4x4 proj) = Camera();
        var world = new WorldEntities();
        // Two players straight ahead; the nearer one wins. Ground y so the body
        // sphere (centre = y + radius) sits on the +Z axis.
        world.AddOrUpdate(new RemotePlayer { Id = 1, X = 0, Y = -1f, Z = 30 });
        world.AddOrUpdate(new RemotePlayer { Id = 2, X = 0, Y = -1f, Z = 12 });
        // An NPC off-axis that should not be picked.
        world.AddOrUpdateNpc(new NpcEntity { Id = 9, X = 20, Y = -1f, Z = 12 });

        PickRay ray = Picking.ScreenPointToRay(view, proj, 512, 384, 1024, 768);
        WorldPicker.Pick? pick = WorldPicker.PickNearest(ray, world);

        Assert.NotNull(pick);
        Assert.Equal((short)2, pick!.Value.Id);
        Assert.False(pick.Value.IsNpc);
    }

    [Fact]
    public void PickNearest_SkipsDeadEntities()
    {
        (Matrix4x4 view, Matrix4x4 proj) = Camera();
        var world = new WorldEntities();
        world.AddOrUpdate(new RemotePlayer { Id = 1, X = 0, Y = -1f, Z = 12, IsDead = true });
        world.AddOrUpdateNpc(new NpcEntity { Id = 5, X = 0, Y = -1f, Z = 20 });

        PickRay ray = Picking.ScreenPointToRay(view, proj, 512, 384, 1024, 768);
        WorldPicker.Pick? pick = WorldPicker.PickNearest(ray, world);

        Assert.NotNull(pick);
        Assert.Equal((short)5, pick!.Value.Id);
        Assert.True(pick.Value.IsNpc); // the dead player is skipped
    }
}
