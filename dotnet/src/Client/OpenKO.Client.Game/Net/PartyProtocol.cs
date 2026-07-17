using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// WIZ_PARTY sub-commands (CUser::PartyProcess). The first body byte is the
/// sub-command; requests here are pinned against the C# Ebenezer read side.
/// </summary>
public static class PartyProtocol
{
    public const byte Create = 0x01;
    public const byte Permit = 0x02;
    public const byte Insert = 0x03;
    public const byte Remove = 0x04;
    public const byte Delete = 0x05;

    private static readonly Encoding Ascii = Encoding.Latin1;

    /// <summary>Create a party inviting the named player.</summary>
    public static byte[] BuildCreate(string name) => WithName(Create, name);

    /// <summary>Invite the named player into the existing party.</summary>
    public static byte[] BuildInvite(string name) => WithName(Insert, name);

    /// <summary>Answer an invite (accept/decline).</summary>
    public static byte[] BuildPermit(bool accept) => [(byte)GameOpcode.WIZ_PARTY, Permit, (byte)(accept ? 1 : 0)];

    /// <summary>Kick a member by socket id.</summary>
    public static byte[] BuildRemove(short sid)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_PARTY);
        w.SetByte(Remove);
        w.SetShort(sid);
        return w.Written.ToArray();
    }

    /// <summary>Leader disbands the whole party (N3_SP_PARTY_OR_FORCE_DESTROY).</summary>
    public static byte[] BuildLeave() => [(byte)GameOpcode.WIZ_PARTY, Delete];

    /// <summary>
    /// CGameProcMain::MsgSend_PartyOrForceLeave — a non-leader member leaving sends
    /// REMOVE with its own socket id (the leader-alone case disbands via BuildLeave).
    /// </summary>
    public static byte[] BuildLeaveAsMember(short ownId) => BuildRemove(ownId);

    // Broadcast sub-commands the party window observes (CGameProcMain::MsgRecv_PartyOrForce).
    public const byte HpChange = 0x06;
    public const byte LevelChange = 0x07;
    public const byte ClassChange = 0x08;

    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];

    /// <summary>A party member as delivered by the INSERT (0x03) broadcast.</summary>
    public sealed record PartyMemberInfo(
        short Id, byte Position, string Name, short MaxHp, short Hp, byte Level, short Class, short MaxMp, short Mp, byte Nation);

    /// <summary>A party HP/MP update (HP_CHANGE 0x06).</summary>
    public sealed record PartyHpUpdate(short Id, short MaxHp, short Hp, short MaxMp, short Mp);

    /// <summary>
    /// Parse the INSERT broadcast (N3_SP_PARTY_OR_FORCE_INSERT): after <c>[opcode][subcmd]</c> —
    /// <c>s2 id</c>; a negative id is an error code (returns null); otherwise <c>b1 position,
    /// s2 nameLen, name, s2 hpMax, s2 hp, b1 level, s2 class, s2 mpMax, s2 mp, b1 nation</c>.
    /// </summary>
    public static PartyMemberInfo? ParseInsert(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload) { Index = 2 };
        short id = r.GetShort();
        if (id < 0)
            return null;

        byte position = r.GetByte();
        string name = Ascii.GetString(r.GetVarString(2));
        short maxHp = r.GetShort();
        short hp = r.GetShort();
        byte level = r.GetByte();
        short cls = r.GetShort();
        short maxMp = r.GetShort();
        short mp = r.GetShort();
        byte nation = r.GetByte();
        return new PartyMemberInfo(id, position, name, maxHp, hp, level, cls, maxMp, mp, nation);
    }

    /// <summary>The member id carried by REMOVE (0x04) / LEVEL_CHANGE (0x07) / CLASS_CHANGE (0x08).</summary>
    public static short ParseId(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload) { Index = 2 };
        return r.GetShort();
    }

    /// <summary>Parse the HP_CHANGE broadcast (0x06): <c>s2 id, s2 hpMax, s2 hp, s2 mpMax, s2 mp</c>.</summary>
    public static PartyHpUpdate ParseHpChange(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload) { Index = 2 };
        short id = r.GetShort();
        short maxHp = r.GetShort();
        short hp = r.GetShort();
        short maxMp = r.GetShort();
        short mp = r.GetShort();
        return new PartyHpUpdate(id, maxHp, hp, maxMp, mp);
    }

    /// <summary>Parse the LEVEL_CHANGE broadcast (0x07): <c>s2 id, b1 level</c>.</summary>
    public static (short Id, byte Level) ParseLevelChange(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload) { Index = 2 };
        short id = r.GetShort();
        byte level = r.GetByte();
        return (id, level);
    }

    /// <summary>Parse the CLASS_CHANGE broadcast (0x08): <c>s2 id, s2 class</c>.</summary>
    public static (short Id, short Class) ParseClassChange(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload) { Index = 2 };
        short id = r.GetShort();
        short cls = r.GetShort();
        return (id, cls);
    }

    private static byte[] WithName(byte sub, string name)
    {
        var buffer = new byte[4 + name.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_PARTY);
        w.SetByte(sub);
        w.SetString2(Ascii.GetBytes(name));
        return w.Written.ToArray();
    }
}
