using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>A WIZ_ATTACK broadcast (CUser::Attack echo).</summary>
public readonly record struct AttackEvent(byte Type, byte Result, short AttackerId, short TargetId);

/// <summary>A WIZ_TARGET_HP update (post-hit target health + damage).</summary>
public readonly record struct TargetHpUpdate(short TargetId, byte Echo, int MaxHp, int Hp, short Damage);

/// <summary>
/// The general-attack packets (CGameProcMain::MsgSend_Attack + the WIZ_ATTACK /
/// WIZ_TARGET_HP broadcasts). The request layout is verbatim from the C++
/// client; the broadcast parsers are pinned against the C# Ebenezer send side.
/// </summary>
public static class CombatProtocol
{
    /// <summary>e_AttackResult values used by the WIZ_ATTACK broadcast.</summary>
    public const byte ResultFail = 0x00;

    public const byte ResultSuccess = 0x01;

    public const byte ResultDeath = 0x02;

    /// <summary>
    /// CGameProcMain::MsgSend_Attack: [WIZ_ATTACK][0x01 type][0x01 success]
    /// [short targetId][short (interval+0.1)*100][short distance*10]. The type
    /// and success bytes are constant on the wire (the server computes the real
    /// result); interval defends against attack-speed hacking.
    /// </summary>
    public static byte[] BuildAttack(short targetId, float interval, float distance)
    {
        var buffer = new byte[9]; // opcode + type + result + 3 shorts
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ATTACK);
        w.SetByte(0x01);                                   // type
        w.SetByte(0x01);                                   // success (client always sends 1)
        w.SetShort(targetId);
        w.SetShort((short)((interval + 0.1f) * 100f));     // attack interval
        w.SetShort((short)(distance * 10f));               // attack distance
        return w.Written.ToArray();
    }

    /// <summary>WIZ_ATTACK broadcast — [opcode][type][result][attackerId][targetId].</summary>
    public static AttackEvent ParseAttack(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte type = r.GetByte();
        byte result = r.GetByte();
        short attackerId = r.GetShort();
        short targetId = r.GetShort();
        return new AttackEvent(type, result, attackerId, targetId);
    }

    /// <summary>WIZ_TARGET_HP — [opcode][tid][echo][dword maxHp][dword hp][short damage].</summary>
    public static TargetHpUpdate ParseTargetHp(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        short tid = r.GetShort();
        byte echo = r.GetByte();
        int maxHp = (int)r.GetDWord();
        int hp = (int)r.GetDWord();
        short damage = r.GetShort();
        return new TargetHpUpdate(tid, echo, maxHp, hp, damage);
    }
}
