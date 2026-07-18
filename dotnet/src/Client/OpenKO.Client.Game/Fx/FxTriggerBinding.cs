using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Fx;

/// <summary>
/// The FX ids a skill contributes, straight from <c>__TABLE_UPC_SKILL</c>
/// (skill_magic_main*.tbl): the two self/cast FX, the flying projectile FX and the
/// on-target FX plus its attach part. The resolver hands this to
/// <see cref="FxTriggerBinding"/> so the trigger layer stays independent of the
/// table loader. A zero FX id means "no effect".
/// </summary>
/// <param name="SelfFx1">iSelfFX1 — the caster/cast FX (the C++ receive path plays this on both self joints).</param>
/// <param name="SelfPart1">
/// iSelfPart1 — encodes up to two caster joints: <c>SelfPart1 % 1000</c> (idx -1)
/// and <c>abs(SelfPart1 / 1000)</c> (idx -2), matching CMagicSkillMng::MsgRecv_Casting.
/// </param>
/// <param name="SelfFx2">iSelfFX2 — the second caster FX (used on the local cast-send path, idx -3).</param>
/// <param name="SelfPart2">iSelfPart2 — the joint iSelfFX2 attaches to.</param>
/// <param name="FlyingFx">iFlyingFX — the projectile FX flying from caster to target.</param>
/// <param name="TargetFx">iTargetFX — the FX spawned on the target on hit.</param>
/// <param name="TargetPart">iTargetPart — the target joint iTargetFX attaches to.</param>
public readonly record struct SkillFxInfo(
    int SelfFx1, int SelfPart1, int SelfFx2, int SelfPart2,
    int FlyingFx, int TargetFx, int TargetPart);

/// <summary>
/// The magic → FX glue: the port of CMagicSkillMng's <c>MsgRecv_Casting</c> /
/// <c>MsgRecv_Flying</c> / <c>MsgRecv_Effecting</c> / <c>MsgRecv_Fail</c> FX side.
/// It turns a broadcast <see cref="MagicPacket"/> into <see cref="FxManager"/>
/// triggers using the skill's fx.tbl ids (resolved via an injected
/// <see cref="SkillFxInfo"/> lookup, so this layer never touches the table loader).
/// <para>
/// The command split (faithful to the receive handlers):
/// <list type="bullet">
/// <item><b>Casting</b> (1) → the caster's self FX <c>iSelfFX1</c> at the two
/// joints <c>iSelfPart1</c> encodes (idx <c>-1</c> and, when non-zero, <c>-2</c>),
/// each anchored on the caster (MoveNone).</item>
/// <item><b>Flying</b> (2) → stop the self FX, then fly <c>iFlyingFX</c> from the
/// caster's <c>iSelfPart1</c> joint to the live target
/// (<see cref="FxBundleAct.MoveDirFlexableTarget"/>) or to the packet's Data1..3
/// world point (<see cref="FxBundleAct.MoveDirFixedTarget"/>). <c>idx</c> is the
/// packet's Data4 (the arrow/flight slot).</item>
/// <item><b>Effecting</b> (3) → stop the self FX, then play <c>iTargetFX</c> on the
/// target at <c>iTargetPart</c> (<see cref="FxBundleAct.MoveNone"/>), or at the
/// Data1..3 point for a ground cast.</item>
/// <item><b>Fail</b> (4) → stop the self FX only.</item>
/// </list>
/// The self-FX <em>stops</em> (idx <c>-1</c>/<c>-2</c>) mirror the C++ <c>Stop</c>
/// calls that retire the casting effect once the spell leaves the caster.
/// </para>
/// </summary>
public static class FxTriggerBinding
{
    /// <summary>The wire's "no target" id (a ground/region cast).</summary>
    public const short NoTarget = -1;

    /// <summary>The idx of the first caster self-FX copy (CMagicSkillMng uses -1).</summary>
    public const int SelfIdx1 = -1;

    /// <summary>The idx of the second caster self-FX copy (CMagicSkillMng uses -2).</summary>
    public const int SelfIdx2 = -2;

    /// <summary>
    /// Fire the FX triggers for one magic packet. <paramref name="resolver"/> maps a
    /// magic id to its <see cref="SkillFxInfo"/>; a null result (skill absent) means
    /// "no effect" and is skipped.
    /// </summary>
    public static void Trigger(FxManager fx, MagicPacket packet, Func<int, SkillFxInfo?> resolver)
    {
        if (resolver(packet.MagicId) is not { } skill)
            return;

        switch (packet.Command)
        {
            case MagicProtocol.Casting:
                TriggerCasting(fx, packet, skill);
                break;

            case MagicProtocol.Flying:
                StopSelfFx(fx, packet.SourceId, skill);
                TriggerFlying(fx, packet, skill);
                break;

            case MagicProtocol.Effecting:
                StopSelfFx(fx, packet.SourceId, skill);
                TriggerEffecting(fx, packet, skill);
                break;

            case MagicProtocol.Fail:
                StopSelfFx(fx, packet.SourceId, skill);
                break;
        }
    }

    /// <summary>
    /// Subscribe <see cref="InGameState.MagicReceived"/> so incoming WIZ_MAGIC_PROCESS
    /// broadcasts drive the FX manager. Combat/attack FX is not table-driven (the
    /// WIZ_ATTACK broadcast carries no FX id), so only magic is bound here.
    /// </summary>
    public static void Bind(InGameState state, FxManager fx, Func<int, SkillFxInfo?> resolver) =>
        state.MagicReceived += packet => Trigger(fx, packet, resolver);

    private static void TriggerCasting(FxManager fx, MagicPacket packet, in SkillFxInfo skill)
    {
        if (skill.SelfFx1 <= 0)
            return;

        // iSelfPart1 packs two joints: low = part1 (idx -1), high = part2 (idx -2).
        int spart1 = skill.SelfPart1 % 1000;
        int spart2 = Math.Abs(skill.SelfPart1 / 1000);

        fx.TriggerBundle(packet.SourceId, spart1, skill.SelfFx1, packet.SourceId, spart1, SelfIdx1);
        if (spart2 != 0)
            fx.TriggerBundle(packet.SourceId, spart2, skill.SelfFx1, packet.SourceId, spart2, SelfIdx2);
    }

    private static void TriggerFlying(FxManager fx, MagicPacket packet, in SkillFxInfo skill)
    {
        if (skill.FlyingFx <= 0)
            return;

        int spart1 = skill.SelfPart1 % 1000;
        int idx = packet.Data4;

        if (packet.TargetId != NoTarget)
        {
            fx.TriggerBundle(
                packet.SourceId, spart1, skill.FlyingFx, packet.TargetId, 0, idx,
                FxBundleAct.MoveDirFlexableTarget);
        }
        else
        {
            var targetPos = new Vector3(packet.Data1, packet.Data2, packet.Data3);
            fx.TriggerBundle(
                packet.SourceId, spart1, skill.FlyingFx, targetPos, idx, FxBundleAct.MoveDirFixedTarget);
        }
    }

    private static void TriggerEffecting(FxManager fx, MagicPacket packet, in SkillFxInfo skill)
    {
        if (skill.TargetFx <= 0)
            return;

        if (packet.TargetId != NoTarget)
        {
            fx.TriggerBundle(
                packet.SourceId, 0, skill.TargetFx, packet.TargetId, skill.TargetPart, 0, FxBundleAct.MoveNone);
        }
        else
        {
            var targetPos = new Vector3(packet.Data1, packet.Data2, packet.Data3);
            fx.TriggerBundle(packet.SourceId, 0, skill.TargetFx, targetPos, 0, FxBundleAct.MoveNone);
        }
    }

    /// <summary>CMagicSkillMng's Stop(SourceID, SourceID, iSelfFX1, -1/-2, true) — retire the cast FX.</summary>
    private static void StopSelfFx(FxManager fx, int sourceId, in SkillFxInfo skill)
    {
        if (skill.SelfFx1 <= 0)
            return;

        fx.Stop(sourceId, sourceId, skill.SelfFx1, SelfIdx1, immediately: true);
        fx.Stop(sourceId, sourceId, skill.SelfFx1, SelfIdx2, immediately: true);
    }
}
