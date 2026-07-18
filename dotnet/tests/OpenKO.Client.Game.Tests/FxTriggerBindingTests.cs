using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Fx;
using OpenKO.Client.Game.Net;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Slice-10.4 pins: the magic → FX trigger binding (CMagicSkillMng::MsgRecv_* FX side).</summary>
public class FxTriggerBindingTests
{
    private static MagicPacket Packet(byte command, int magicId, short source, short target,
        short d1 = 0, short d2 = 0, short d3 = 0, short d4 = 0) =>
        new(command, magicId, source, target, d1, d2, d3, d4, 0, 0);

    /// <summary>A resolver returning one fixed skill row for any magic id.</summary>
    private static Func<int, SkillFxInfo?> Skill(
        int selfFx1 = 0, int selfPart1 = 0, int selfFx2 = 0, int selfPart2 = 0,
        int flyingFx = 0, int targetFx = 0, int targetPart = 0) =>
        _ => new SkillFxInfo(selfFx1, selfPart1, selfFx2, selfPart2, flyingFx, targetFx, targetPart);

    [Fact]
    public void Effecting_TriggersTheTargetFxAtTheTargetPart()
    {
        var locator = new FakeLocator().Set(10, Vector3.Zero).Set(20, new Vector3(5f, 0f, 0f));
        var loader = new FakeBundleLoader().Add(700);
        var fx = new FxManager(locator, loader);

        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Effecting, magicId: 42, source: 10, target: 20),
            Skill(targetFx: 700, targetPart: 4));

        Assert.Single(fx.Bundles);
        FxBundleGame bundle = fx.Bundles[0];
        Assert.Equal(700, bundle.FxId);
        Assert.Equal(10, bundle.SourceId);
        Assert.Equal(20, bundle.TargetId);
        Assert.Equal(4, bundle.TargetJoint);
        Assert.Equal(FxBundleAct.MoveNone, bundle.MoveType);
    }

    [Fact]
    public void Flying_TriggersTheFlyingFxChasingALiveTarget()
    {
        var locator = new FakeLocator().Set(10, Vector3.Zero).Set(20, new Vector3(5f, 0f, 0f));
        var loader = new FakeBundleLoader().Add(500);
        var fx = new FxManager(locator, loader);

        // idx carried in Data4; source joint = SelfPart1 % 1000.
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Flying, magicId: 42, source: 10, target: 20, d4: 3),
            Skill(selfPart1: 7, flyingFx: 500, targetFx: 700));

        Assert.Single(fx.Bundles);
        FxBundleGame bundle = fx.Bundles[0];
        Assert.Equal(500, bundle.FxId);
        Assert.Equal(20, bundle.TargetId);
        Assert.Equal(7, bundle.SourceJoint);
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
            Skill(flyingFx: 500));

        Assert.Single(fx.Bundles);
        FxBundleGame bundle = fx.Bundles[0];
        Assert.Equal(500, bundle.FxId);
        Assert.True(bundle.Region);
        Assert.Equal(FxBundleAct.MoveDirFixedTarget, bundle.MoveType);
        Assert.Equal(new Vector3(3f, 4f, 5f), bundle.DestPos);
    }

    [Fact]
    public void Casting_TriggersTheSelfFxOnTheCaster()
    {
        var locator = new FakeLocator().Set(10, Vector3.Zero);
        var loader = new FakeBundleLoader().Add(500);
        var fx = new FxManager(locator, loader);

        // SelfPart1 = 3 → one joint (spart1=3, spart2=0), idx -1, self-anchored.
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Casting, magicId: 42, source: 10, target: 20),
            Skill(selfFx1: 500, selfPart1: 3));

        Assert.Single(fx.Bundles);
        FxBundleGame bundle = fx.Bundles[0];
        Assert.Equal(500, bundle.FxId);
        Assert.Equal(10, bundle.SourceId);
        Assert.Equal(10, bundle.TargetId); // self-cast: source == target
        Assert.Equal(3, bundle.SourceJoint);
        Assert.Equal(FxTriggerBinding.SelfIdx1, bundle.Idx);
    }

    [Fact]
    public void Casting_WithTwoEncodedJointsTriggersBothSelfCopies()
    {
        var locator = new FakeLocator().Set(10, Vector3.Zero);
        var loader = new FakeBundleLoader().Add(500);
        var fx = new FxManager(locator, loader);

        // SelfPart1 = 2005 → spart1 = 5 (idx -1), spart2 = 2 (idx -2).
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Casting, magicId: 42, source: 10, target: 20),
            Skill(selfFx1: 500, selfPart1: 2005));

        Assert.Equal(2, fx.Bundles.Count);
        Assert.Contains(fx.Bundles, b => b.SourceJoint == 5 && b.Idx == FxTriggerBinding.SelfIdx1);
        Assert.Contains(fx.Bundles, b => b.SourceJoint == 2 && b.Idx == FxTriggerBinding.SelfIdx2);
    }

    [Fact]
    public void Fail_TriggersNoBundle()
    {
        var fx = new FxManager(new FakeLocator().Set(10, Vector3.Zero), new FakeBundleLoader().Add(500).Add(700));

        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Fail, 42, 10, 20), Skill(selfFx1: 500, flyingFx: 700));

        Assert.Empty(fx.Bundles);
    }

    [Fact]
    public void UnknownSkill_IsSkipped()
    {
        var fx = new FxManager(new FakeLocator().Set(10, Vector3.Zero), new FakeBundleLoader().Add(500));

        // Resolver returns null (skill absent) => nothing triggers, no loader lookup.
        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Effecting, 42, 10, 20), _ => null);

        Assert.Empty(fx.Bundles);
    }

    [Fact]
    public void ZeroTargetFx_IsSkipped()
    {
        var fx = new FxManager(new FakeLocator().Set(10, Vector3.Zero), new FakeBundleLoader());

        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Effecting, 42, 10, 20), Skill(targetFx: 0));

        Assert.Empty(fx.Bundles);
    }

    [Fact]
    public void Loader_SurfacesTheSoundIdOntoTheBundle()
    {
        var fx = new FxManager(new FakeLocator().Set(10, Vector3.Zero), new FakeBundleLoader().Add(700, soundId: 1234));

        FxTriggerBinding.Trigger(fx, Packet(MagicProtocol.Effecting, 42, 10, 20), Skill(targetFx: 700));

        Assert.Equal(1234u, fx.Bundles[0].SoundId);
    }
}
