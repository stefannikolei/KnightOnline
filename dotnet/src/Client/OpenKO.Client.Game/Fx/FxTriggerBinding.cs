using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Fx;

/// <summary>
/// The magic → FX glue (the trigger half of <c>CMagicSkillMng</c>'s cast/fly/hit
/// split): turns a <see cref="MagicPacket"/> into <see cref="FxManager"/> triggers
/// using the two effect ids from the magic/skill table
/// (<c>dwEffectID1</c> = the flying/projectile FX, <c>dwEffectID2</c> = the
/// target/hit FX). The FX ids are supplied by an injected resolver so this layer
/// stays independent of the table loader.
/// <para>
/// Which commands trigger:
/// <list type="bullet">
/// <item><b>Flying</b> (2) → the <c>fx1</c> projectile flies from the caster to
/// the target: a <see cref="FxBundleAct.MoveDirFlexableTarget"/> chase when the
/// target is a live entity, or a <see cref="FxBundleAct.MoveDirFixedTarget"/> shot
/// at the packet's Data1..3 world point otherwise.</item>
/// <item><b>Effecting</b> (3) → the <c>fx2</c> hit effect plays on the target
/// (<see cref="FxBundleAct.MoveNone"/>, attached to the target), or at the Data1..3
/// point for a ground cast.</item>
/// </list>
/// Casting (1) and Fail (4) trigger no bundle here (Casting starts the self/cast
/// animation FX driven by the caster locally; Fail only stops FX). The
/// <c>idx</c> for a flying shot is the packet's <c>Data4</c> (the arrow/flight
/// slot the C++ threads through as <c>idx</c>).
/// </para>
/// </summary>
public static class FxTriggerBinding
{
    /// <summary>The wire's "no target" id (a ground/region cast).</summary>
    public const short NoTarget = -1;

    /// <summary>
    /// Fire the FX triggers for one magic packet. <paramref name="resolver"/> maps a
    /// magic id to <c>(fx1, fx2)</c>; a zero id means "no effect" and is skipped.
    /// </summary>
    public static void Trigger(FxManager fx, MagicPacket packet, Func<int, (int Fx1, int Fx2)> resolver)
    {
        (int fx1, int fx2) = resolver(packet.MagicId);

        switch (packet.Command)
        {
            case MagicProtocol.Flying:
                TriggerFlying(fx, packet, fx1);
                break;

            case MagicProtocol.Effecting:
                TriggerEffecting(fx, packet, fx2);
                break;
        }
    }

    /// <summary>
    /// Subscribe <see cref="InGameState.MagicReceived"/> so incoming WIZ_MAGIC_PROCESS
    /// broadcasts drive the FX manager. Combat/attack FX is not table-driven (the
    /// WIZ_ATTACK broadcast carries no FX id), so only magic is bound here.
    /// </summary>
    public static void Bind(InGameState state, FxManager fx, Func<int, (int Fx1, int Fx2)> resolver) =>
        state.MagicReceived += packet => Trigger(fx, packet, resolver);

    private static void TriggerFlying(FxManager fx, MagicPacket packet, int fx1)
    {
        if (fx1 <= 0)
            return;

        int idx = packet.Data4;
        if (packet.TargetId != NoTarget)
        {
            fx.TriggerBundle(
                packet.SourceId, 0, fx1, packet.TargetId, 0, idx, FxBundleAct.MoveDirFlexableTarget);
        }
        else
        {
            var targetPos = new Vector3(packet.Data1, packet.Data2, packet.Data3);
            fx.TriggerBundle(packet.SourceId, 0, fx1, targetPos, idx, FxBundleAct.MoveDirFixedTarget);
        }
    }

    private static void TriggerEffecting(FxManager fx, MagicPacket packet, int fx2)
    {
        if (fx2 <= 0)
            return;

        if (packet.TargetId != NoTarget)
        {
            fx.TriggerBundle(packet.SourceId, 0, fx2, packet.TargetId, 0, 0, FxBundleAct.MoveNone);
        }
        else
        {
            var targetPos = new Vector3(packet.Data1, packet.Data2, packet.Data3);
            fx.TriggerBundle(packet.SourceId, 0, fx2, targetPos, 0, FxBundleAct.MoveNone);
        }
    }
}
