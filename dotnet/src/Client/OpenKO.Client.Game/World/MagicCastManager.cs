using OpenKO.Client.Assets.Player;
using OpenKO.Client.Game.Net;

namespace OpenKO.Client.Game.World;

/// <summary>Why a <see cref="MagicCastManager.TryCast"/> was rejected (or <see cref="None"/> on success).</summary>
public enum CastFailReason
{
    None = 0,
    Dead,
    OnCooldown,
    NotEnoughMp,
    NotEnoughHp,
    LevelTooLow,
    MissingItem,
    NoTarget,
    UnknownTarget,
}

/// <summary>
/// The outcome of a cast attempt: on success the built WIZ_MAGIC_PROCESS packet; on failure the
/// reason (and a default packet). Pure value — the caller decides whether to send it.
/// </summary>
public readonly record struct CastResult(bool Success, CastFailReason Reason, MagicPacket Packet)
{
    public static CastResult Fail(CastFailReason reason) => new(false, reason, default);

    public static CastResult Ok(MagicPacket packet) => new(true, CastFailReason.None, packet);
}

/// <summary>
/// Headless port of <c>CMagicSkillMng::MsgSend_MagicProcess</c> (Client/WarFare/MagicSkillMng.cpp):
/// the cast gate (cooldown + MP/HP/level/exhaust-item conditions) and the target/position/area
/// packet routing keyed on <see cref="SkillRow.Target"/> (<c>e_SkillMagicTaget</c>). Timing is
/// injected (<c>nowSeconds</c>) instead of the C++ per-frame <c>CN3Base::TimeGet()</c>, so the
/// whole thing is deterministic and testable without a device.
///
/// <para>Confirmed wire layouts (both fill the shared <see cref="MagicPacket"/>):</para>
/// <list type="bullet">
/// <item><b>target-cast</b> (<c>StartSkillMagicAtTargetPacket</c>, ~L1180-1276):
/// <c>[0x31][subcmd][id:u32][caster:i16][target:i16][d1..d6:i16=0]</c> — for a melee type-1
/// skill with CastTime==0 the C++ sets <c>d1=1, d2=1</c> (the combo flag).</item>
/// <item><b>position-cast</b> (<c>StartSkillMagicAtPosPacket</c>, ~L1051-1118):
/// <c>[0x31][subcmd][id:u32][caster:i16][-1:i16][posX:i16][posY:i16][posZ:i16][0][0][0]</c>.</item>
/// </list>
/// <c>subcmd</c> is <see cref="MagicProtocol.Effecting"/> (3) when <see cref="SkillRow.CastTime"/>
/// is 0, else <see cref="MagicProtocol.Casting"/> (1).
/// </summary>
public sealed class MagicCastManager
{
    // e_SkillMagicTaget (GameDef.h) — SkillRow.Target values.
    private const int TargetSelf = 1;
    private const int TargetFriendWithMe = 2;
    private const int TargetFriendOnly = 3;
    private const int TargetParty = 4;
    private const int TargetNpcOnly = 5;
    private const int TargetPartyAll = 6;
    private const int TargetEnemyOnly = 7;
    private const int TargetAll = 8;
    private const int TargetAreaEnemy = 10;
    private const int TargetAreaFriend = 11;
    private const int TargetAreaAll = 12;
    private const int TargetArea = 13;
    private const int TargetDeadFriendOnly = 25;

    // dwID >= this is a usable-item skill, not a class skill (UIITEM_TYPE_USABLE_ID_MIN).
    private const uint UsableItemIdMin = 450000;

    private readonly Dictionary<uint, (double End, double Duration)> _cooldowns = [];

    /// <summary>
    /// The cast gate + packet router. Records the cooldown on success (so an immediate re-cast is
    /// rejected until <c>ReCastTime</c> tenths of a second elapse). Positions are the caster's world
    /// coords truncated to int16, matching the C++ <c>(int16_t)vPos.x</c> cast.
    /// </summary>
    public CastResult TryCast(
        SkillRow skill,
        short casterId,
        short targetId,
        (short X, short Y, short Z) casterPos,
        LocalPlayer me,
        Inventory inv,
        double nowSeconds)
    {
        if (me.IsDead)
            return CastResult.Fail(CastFailReason.Dead);

        // Cooldown gate (m_RecastTimes / m_NonActionRecastTimes).
        if (_cooldowns.TryGetValue(skill.Id, out (double End, double Duration) cd) && nowSeconds < cd.End)
            return CastResult.Fail(CastFailReason.OnCooldown);

        // CheckValidCondition: MP / HP / level / exhaust item.
        if (me.Mp < skill.ExhaustMsp)
            return CastResult.Fail(CastFailReason.NotEnoughMp);
        if (skill.ExhaustHp < 10000 && me.Hp < skill.ExhaustHp)
            return CastResult.Fail(CastFailReason.NotEnoughHp);
        if (me.Level < skill.NeedLevel)
            return CastResult.Fail(CastFailReason.LevelTooLow);
        if (skill.ExhaustItem != 0 && inv.CountById((int)skill.ExhaustItem) < 1)
            return CastResult.Fail(CastFailReason.MissingItem);

        byte subcmd = skill.CastTime == 0 ? MagicProtocol.Effecting : MagicProtocol.Casting;

        MagicPacket packet;
        switch (skill.Target)
        {
            case TargetSelf:
                packet = TargetCast(skill, subcmd, casterId, casterId);
                break;

            // These fall back to the caster when nothing is targeted (C++ self / party owner).
            case TargetFriendWithMe:
            case TargetParty:
                packet = TargetCast(skill, subcmd, casterId, targetId >= 0 ? targetId : casterId);
                break;

            // A live target is required (enemy / friend / npc / all / dead-friend).
            case TargetFriendOnly:
            case TargetNpcOnly:
            case TargetEnemyOnly:
            case TargetAll:
            case TargetDeadFriendOnly:
                if (targetId < 0)
                    return CastResult.Fail(CastFailReason.NoTarget);
                packet = TargetCast(skill, subcmd, casterId, targetId);
                break;

            // Centred on the caster (party-wide buff / self AoE).
            case TargetPartyAll:
            case TargetArea:
            // Ground-targeted AoE: the C++ opens a mouse ground-point picker (deferred, 9.10);
            // here we anchor on the caster position like the party-all/area case.
            case TargetAreaEnemy:
            case TargetAreaFriend:
            case TargetAreaAll:
                packet = PosCast(skill, subcmd, casterId, casterPos);
                break;

            default:
                return CastResult.Fail(CastFailReason.UnknownTarget);
        }

        if (skill.ReCastTime > 0)
        {
            double dur = skill.ReCastTime / 10.0;
            _cooldowns[skill.Id] = (nowSeconds + dur, dur);
        }

        return CastResult.Ok(packet);
    }

    /// <summary>
    /// CMagicSkillMng::GetCooldown as a ring fraction: the remaining cooldown for a skill divided by
    /// its full <c>ReCastTime</c>, in [0,1]. 0 means ready (no cooldown running or elapsed).
    /// </summary>
    public double Cooldown(uint skillId, double nowSeconds)
    {
        if (!_cooldowns.TryGetValue(skillId, out (double End, double Duration) cd) || cd.Duration <= 0)
            return 0;

        double remaining = cd.End - nowSeconds;
        if (remaining <= 0)
            return 0;

        double frac = remaining / cd.Duration;
        return frac > 1 ? 1 : frac;
    }

    /// <summary>Clears all tracked cooldowns (e.g. on zone change / logout).</summary>
    public void ClearCooldowns() => _cooldowns.Clear();

    private static MagicPacket TargetCast(SkillRow skill, byte subcmd, short casterId, short targetId)
    {
        // Melee type-1, instant: the C++ tags the combo with Data1=1, Data2=1.
        bool meleeCombo = skill.CastTime == 0
            && (skill.FirstTableType == 1 || skill.SecondTableType == 1)
            && skill.Id < UsableItemIdMin;

        short d1 = meleeCombo ? (short)1 : (short)0;
        short d2 = meleeCombo ? (short)1 : (short)0;

        return new MagicPacket(subcmd, (int)skill.Id, casterId, targetId, d1, d2, 0, 0, 0, 0);
    }

    private static MagicPacket PosCast(SkillRow skill, byte subcmd, short casterId, (short X, short Y, short Z) pos) =>
        new(subcmd, (int)skill.Id, casterId, -1, pos.X, pos.Y, pos.Z, 0, 0, 0);
}
