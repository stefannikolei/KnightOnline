using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Fx;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Slice-9.10c pins: the game FX manager (CN3FXMgr) spawn/advance/cull/stop.</summary>
public class FxManagerTests
{
    private const float Dt = 0.1f;

    [Fact]
    public void TriggerBundle_AddsALiveBundleAndCachesTheOrigin()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero).Set(2, new Vector3(5f, 0f, 0f));
        var loader = new FakeBundleLoader().Add(100);
        var fx = new FxManager(locator, loader);

        FxBundleGame? bundle = fx.TriggerBundle(1, 0, 100, 2, 0, idx: 3, FxBundleAct.MoveNone);

        Assert.NotNull(bundle);
        Assert.Equal(100, bundle!.FxId);
        Assert.Equal(1, bundle.SourceId);
        Assert.Equal(2, bundle.TargetId);
        Assert.Equal(3, bundle.Idx);
        Assert.Single(fx.Bundles);
        Assert.Equal(1, fx.OriginCount);
    }

    [Fact]
    public void TriggerBundle_UnknownFxIdIsANoOp()
    {
        var fx = new FxManager(new FakeLocator(), new FakeBundleLoader());
        Assert.Null(fx.TriggerBundle(1, 0, 999, 2, 0));
        Assert.Empty(fx.Bundles);
        Assert.Equal(0, fx.OriginCount);
    }

    [Fact]
    public void Tick_AdvancesTheBundleClock()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100, life0: 0f));
        FxBundleGame bundle = fx.TriggerBundle(1, 0, 100, 1, -1, moveType: FxBundleAct.MoveNone)!;

        fx.Tick(Dt, Vector3.Zero);
        fx.Tick(Dt, Vector3.Zero);

        Assert.False(bundle.IsDead);
        Assert.True(bundle.Simulator.Life > 0f);
    }

    [Fact]
    public void Tick_CullsDeadBundlesAndReleasesTheOriginRefCount()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        // life0 = 0.2 => the bundle retires once its clock passes 0.2s.
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100, life0: 0.2f));
        fx.TriggerBundle(1, 0, 100, 1, -1, moveType: FxBundleAct.MoveNone);
        Assert.Single(fx.Bundles);

        // Advance well past life0 so the sim marks it dead, then one more Tick culls it.
        for (int i = 0; i < 6; i++)
            fx.Tick(Dt, Vector3.Zero);

        Assert.Empty(fx.Bundles);
        // The origin lingers (ref count back to 0) until OriginLimitedTime elapses.
        Assert.Equal(1, fx.OriginCount);
    }

    [Fact]
    public void Tick_EvictsUnusedOriginsAfterTheLimitedTime()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100, life0: 0.2f))
        {
            OriginLimitedTime = 0.5f,
        };
        fx.TriggerBundle(1, 0, 100, 1, -1, moveType: FxBundleAct.MoveNone);

        for (int i = 0; i < 20; i++)
            fx.Tick(Dt, Vector3.Zero);

        Assert.Empty(fx.Bundles);
        Assert.Equal(0, fx.OriginCount);
    }

    [Fact]
    public void Stop_ImmediatelyRetiresMatchingBundlesForCulling()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100, life0: 0f));
        fx.TriggerBundle(1, 0, 100, 1, -1, idx: 2, moveType: FxBundleAct.MoveNone);

        fx.Stop(sourceId: 1, targetId: 1, fxId: 100, idx: 2, immediately: true);
        // Stop(immediately) drives the sim to Dead; the next Tick culls it.
        fx.Tick(Dt, Vector3.Zero);

        Assert.Empty(fx.Bundles);
    }

    [Fact]
    public void Stop_IgnoresBundlesWithADifferentIndex()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100, life0: 0f));
        FxBundleGame bundle = fx.TriggerBundle(1, 0, 100, 1, -1, idx: 2, moveType: FxBundleAct.MoveNone)!;

        fx.Stop(sourceId: 1, targetId: 1, fxId: 100, idx: 99, immediately: true);
        fx.Tick(Dt, Vector3.Zero);

        Assert.Single(fx.Bundles);
        Assert.False(bundle.IsDead);
    }

    [Fact]
    public void StopMine_RetiresOnlyTheLocalPlayersBundles()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero).Set(2, Vector3.Zero);
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100, life0: 0f).Add(101, life0: 0f));
        FxBundleGame mine = fx.TriggerBundle(1, 0, 100, 1, -1, moveType: FxBundleAct.MoveNone)!;
        FxBundleGame theirs = fx.TriggerBundle(2, 0, 101, 2, -1, moveType: FxBundleAct.MoveNone)!;

        fx.StopMine(localId: 1);

        Assert.Equal(FxBundleState.Dead, mine.Simulator.State);
        Assert.NotEqual(FxBundleState.Dead, theirs.Simulator.State);
    }

    [Fact]
    public void SetBundlePos_RetargetsTheFirstMatchingBundle()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100, life0: 0f));
        FxBundleGame bundle = fx.TriggerBundle(1, 0, 100, 1, -1, idx: 4, moveType: FxBundleAct.MoveNone)!;

        var dest = new Vector3(7f, 8f, 9f);
        fx.SetBundlePos(100, 4, dest);

        Assert.Equal(dest, bundle.DestPos);
    }

    [Fact]
    public void ClearAll_DropsBundlesAndOrigins()
    {
        var locator = new FakeLocator().Set(1, Vector3.Zero);
        var fx = new FxManager(locator, new FakeBundleLoader().Add(100));
        fx.TriggerBundle(1, 0, 100, 1, -1);

        fx.ClearAll();

        Assert.Empty(fx.Bundles);
        Assert.Equal(0, fx.OriginCount);
    }

    [Fact]
    public void Tick_AdvancesTheWeatherField()
    {
        var fx = new FxManager(new FakeLocator(), new FakeBundleLoader());
        fx.SetWeather(OpenKO.Client.Engine.Fx.WeatherType.Rain, 100);
        Assert.True(fx.Weather.Active);

        float y0 = fx.Weather.RainParticles[0].Tail.Y;
        fx.Tick(Dt, Vector3.Zero);
        Assert.NotEqual(y0, fx.Weather.RainParticles[0].Tail.Y);
    }
}
