using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>CUser method bodies needed by the GameSocket handlers (User.cpp).</summary>
public partial class AiUser
{
    public const byte UserDead = 0x00; // USER_DEAD
    public const byte UserLive = 0x01; // USER_LIVE

    private const byte AttackTargetDead = 2; // ATTACK_TARGET_DEAD

    /// <summary>Port of CUser::InitNpcAttack.</summary>
    public void InitNpcAttack() => Array.Fill(SurroundNpcNumber, (short)-1);

    /// <summary>
    /// Port of CUser::Dead: marks the user dead, drops it from its region and
    /// builds the AG_ATTACK_RESULT broadcast. <paramref name="sendToZone"/>
    /// replaces SendAll (send on the user's zone socket); it is only invoked
    /// when tid &gt; 0, exactly like the C++.
    /// </summary>
    public void Dead(AiWorld world, int tid, int damage, ILogger logger, Func<byte[], bool>? sendToZone = null)
    {
        if (Live == UserDead)
            return;

        HP = 0;
        Live = UserDead;

        InitNpcAttack();

        AiZone? zone = ZoneIndex >= 0 && ZoneIndex < world.Zones.Count ? world.Zones[ZoneIndex] : null;
        if (zone is null)
        {
            logger.LogError("User::Dead: map not found [userId={Uid} charId={CharId} zoneId={ZoneIndex}]",
                Uid, UserId, ZoneIndex);
            return;
        }

        if (RegionX < 0 || RegionZ < 0 || RegionX > zone.RegionsX - 1 || RegionZ > zone.RegionsZ - 1)
        {
            logger.LogError("User::Dead: out of region bounds [userId={Uid} charId={CharId} x={Rx} z={Rz}]",
                Uid, UserId, RegionX, RegionZ);
            return;
        }

        zone.RegionUserRemove(RegionX, RegionZ, Uid);

        RegionX = -1;
        RegionZ = -1;

        logger.LogDebug("User::Dead: userId={Uid} charId={CharId}", Uid, UserId);

        var buff = new byte[16];
        var w = new PacketWriter(buff);
        w.SetByte(AiOpcode.AG_ATTACK_RESULT);
        w.SetByte(0x02);            // type
        w.SetByte(AttackTargetDead);
        w.SetShort(tid);            // sid: the killer
        w.SetShort(Uid);            // tid: this user (+ USER_BAND, which is 0)
        w.SetShort(damage);
        w.SetDWord((uint)HP);

        // SendAll's own uid bounds check
        if (tid > 0 && Uid >= 0 && Uid < AiConstants.MaxUser)
            sendToZone?.Invoke(buff[..w.Index]);
    }
}
