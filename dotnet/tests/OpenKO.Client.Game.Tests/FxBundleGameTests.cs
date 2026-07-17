using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Fx;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Slice-9.10c pins: the FX bundle movement acts (CN3FXBundleGame).</summary>
public class FxBundleGameTests
{
    private const float Dt = 0.1f;

    private static FxBundleGame NewBundle(IFxEntityLocator locator, FxBundleAct move)
    {
        // life0 0 = infinite, velocity 10 => a 0.1s step advances 1.0 unit.
        var bundle = new FxBundleGame(FxTestBundles.Build(life0: 0f, velocity: 10f), locator)
        {
            MoveType = move,
        };
        return bundle;
    }

    [Fact]
    public void Trigger_AnchorsAtSourceAndAimsAtTarget()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero).Set(2, new Vector3(10f, 0f, 0f));
        FxBundleGame bundle = NewBundle(locator, FxBundleAct.MoveDirFixedTarget);

        bundle.Trigger(sourceId: 1, targetId: 2, targetJoint: -1);

        Assert.Equal(Vector3.Zero, bundle.Position);
        Assert.Equal(new Vector3(10f, 0f, 0f), bundle.DestPos);
        Assert.Equal(10f, bundle.Distance, 4);
        Assert.Equal(new Vector3(1f, 0f, 0f), bundle.Direction);
    }

    [Fact]
    public void FixedTarget_FliesAlongTheInitialDirectionIgnoringTargetMovement()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero).Set(2, new Vector3(10f, 0f, 0f));
        FxBundleGame bundle = NewBundle(locator, FxBundleAct.MoveDirFixedTarget);
        bundle.Trigger(sourceId: 1, targetId: 2, targetJoint: -1);

        // The target teleports off the +X axis; a fixed shot keeps its original heading.
        locator.Set(2, new Vector3(0f, 0f, 10f));
        bundle.Tick(Dt, Vector3.Zero);

        Assert.Equal(1f, bundle.Position.X, 4); // dir (1,0,0) * (10*0.1)
        Assert.Equal(0f, bundle.Position.Z, 4);
    }

    [Fact]
    public void FlexableTarget_ReAimsEachFrameTowardTheMovedTarget()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero).Set(2, new Vector3(10f, 0f, 0f));
        FxBundleGame bundle = NewBundle(locator, FxBundleAct.MoveDirFlexableTarget);
        bundle.Trigger(sourceId: 1, targetId: 2, targetJoint: -1);

        // The target jumps onto the +Z axis; a flexible shot re-homes toward it.
        locator.Set(2, new Vector3(0f, 0f, 10f));
        bundle.Tick(Dt, Vector3.Zero);

        Assert.Equal(0f, bundle.Position.X, 4);
        Assert.Equal(1f, bundle.Position.Z, 4); // re-aimed dir (0,0,1) * 1.0
        Assert.Equal(new Vector3(0f, 0f, 1f), bundle.Direction);
    }

    [Fact]
    public void MoveNone_PinsThePositionToTheDestinationEachFrame()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero).Set(2, new Vector3(3f, 4f, 5f));
        FxBundleGame bundle = NewBundle(locator, FxBundleAct.MoveNone);
        bundle.Trigger(sourceId: 1, targetId: 2, targetJoint: -1);

        // Target moves; a MoveNone (attached) bundle snaps to the new dest.
        locator.Set(2, new Vector3(6f, 7f, 8f));
        bundle.Tick(Dt, Vector3.Zero);

        Assert.Equal(new Vector3(6f, 7f, 8f), bundle.Position);
    }

    [Fact]
    public void RegionCast_FliesTowardAFixedWorldPoint()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        FxBundleGame bundle = NewBundle(locator, FxBundleAct.MoveDirFixedTarget);

        bundle.TriggerRegion(sourceId: 1, new Vector3(0f, 0f, 10f));
        Assert.True(bundle.Region);
        Assert.Equal(new Vector3(0f, 0f, 1f), bundle.Direction);

        bundle.Tick(Dt, Vector3.Zero);
        Assert.Equal(1f, bundle.Position.Z, 4);
    }

    [Fact]
    public void RegionPoison_RecentersOnTheCamera()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        FxBundleGame bundle = NewBundle(locator, FxBundleAct.RegionPoison);
        bundle.TriggerRegion(sourceId: 1, new Vector3(5f, 0f, 0f));

        var camera = new Vector3(20f, 3f, -20f);
        bundle.Tick(Dt, camera);

        Assert.Equal(camera, bundle.Position);
    }
}
