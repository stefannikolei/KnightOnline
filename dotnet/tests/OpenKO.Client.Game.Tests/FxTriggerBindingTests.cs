using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Fx;
using OpenKO.Client.Game.Net;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Slice-9.10c pins: the magic → FX trigger binding (CMagicSkillMng split).</summary>
public class FxTriggerBindingTests
{
    private static MagicPacket Packet(byte command, int magicId, short source, short target,
        short d1 = 0, short d2 = 0, short d3 = 0, short d4 = 0) =>
        new(command, magicId, source, target, d1, d2, d3, d4, 0, 0);

    [Fact]
    public void Effecting_TriggersTheHitFxOnTheTarget()
    {
        var locator = new FakeLocator().Set(10, Vector3.Zero).Set(20, new Vector3(5f, 0f, 0f));
        var loader = new FakeBundleLoader().Add(700); // fx2 (hit)
        var fx = new FxManager(locator, loader);

        // fx1 = 0 (no flying), fx2 = 700 (hit).
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Effecting, magicId: 42, source: 10, target: 20),
            _ => (0, 700));

        Assert.Single(fx.Bundles);
        FxBundleGame bundle = fx.Bundles[0];
        Assert.Equal(700, bundle.FxId);
        Assert.Equal(10, bundle.SourceId);
        Assert.Equal(20, bundle.TargetId);
        Assert.Equal(FxBundleAct.MoveNone, bundle.MoveType);
    }

    [Fact]
    public void Flying_TriggersTheProjectileFxChasingALiveTarget()
    {
        var locator = new FakeLocator().Set(10, Vector3.Zero).Set(20, new Vector3(5f, 0f, 0f));
        var loader = new FakeBundleLoader().Add(500); // fx1 (flying)
        var fx = new FxManager(locator, loader);

        // idx carried in Data4.
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Flying, magicId: 42, source: 10, target: 20, d4: 3),
            _ => (500, 700));

        Assert.Single(fx.Bundles);
        FxBundleGame bundle = fx.Bundles[0];
        Assert.Equal(500, bundle.FxId);
        Assert.Equal(20, bundle.TargetId);
        Assert.Equal(3, bundle.Idx);
        Assert.Equal(FxBundleAct.MoveDirFlexableTarget, bundle.MoveType);
    }

    [Fact]
    public void Flying_WithoutATargetShootsAtTheDataWorldPoint()
    {
        var locator = new FakeLocator().Set(10, Vector3.Zero);
        var loader = new FakeBundleLoader().Add(500);
        var fx = new FxManager(locator, loader);

        FxTriggerBinding.Trigger(fx,
            Packet(MagicProtocol.Flying, magicId: 42, source: 10, target: -1, d1: 3, d2: 4, d3: 5, d4: 1),
            _ => (500, 0));

        Assert.Single(fx.Bundles);
        FxBundleGame bundle = fx.Bundles[0];
        Assert.Equal(500, bundle.FxId);
        Assert.True(bundle.Region);
        Assert.Equal(FxBundleAct.MoveDirFixedTarget, bundle.MoveType);
        Assert.Equal(new Vector3(3f, 4f, 5f), bundle.DestPos);
    }

    [Fact]
    public void CastingAndFail_TriggerNoBundle()
    {
        var fx = new FxManager(new FakeLocator().Set(10, Vector3.Zero), new FakeBundleLoader().Add(500).Add(700));

        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Casting, 42, 10, 20), _ => (500, 700));
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Fail, 42, 10, 20), _ => (500, 700));

        Assert.Empty(fx.Bundles);
    }

    [Fact]
    public void ZeroFxId_IsSkipped()
    {
        var fx = new FxManager(new FakeLocator().Set(10, Vector3.Zero), new FakeBundleLoader());

        // fx2 resolves to 0 => no hit effect, no bundle, no loader lookup needed.
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Effecting, 42, 10, 20), _ => (0, 0));

        Assert.Empty(fx.Bundles);
    }
}
