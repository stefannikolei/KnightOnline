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

    /// <summary>Leave / disband the party.</summary>
    public static byte[] BuildLeave() => [(byte)GameOpcode.WIZ_PARTY, Delete];

    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];

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
