using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// WIZ_KNIGHTS_PROCESS sub-commands (CKnightsManager). Covers the common clan
/// requests; field order pinned against the C# Ebenezer read side.
/// </summary>
public static class KnightsProtocol
{
    public const byte Create = 0x01;
    public const byte Join = 0x02;
    public const byte Withdraw = 0x03;
    public const byte Remove = 0x04;
    public const byte Destroy = 0x05;
    public const byte Admit = 0x06;
    public const byte Reject = 0x07;
    public const byte Punish = 0x08;
    public const byte Chief = 0x09;
    public const byte ViceChief = 0x0A;
    public const byte Officer = 0x0B;
    public const byte AllListReq = 0x0C;
    public const byte MemberReq = 0x0D;
    public const byte CurrentReq = 0x0E;
    public const byte Stash = 0x0F;
    public const byte ModifyFame = 0x10;
    public const byte JoinReq = 0x11;

    private static readonly Encoding Ascii = Encoding.Latin1;

    /// <summary>Found a clan with the given name.</summary>
    public static byte[] BuildCreate(string name)
    {
        var buffer = new byte[4 + name.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        w.SetByte(Create);
        w.SetString2(Ascii.GetBytes(name));
        return w.Written.ToArray();
    }

    /// <summary>Request to join a clan by its index.</summary>
    public static byte[] BuildJoin(short knightsId) => WithShort(Join, knightsId);

    /// <summary>Leave the current clan.</summary>
    public static byte[] BuildWithdraw() => [(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, Withdraw];

    /// <summary>Page through the clan list.</summary>
    public static byte[] BuildAllListRequest(short page) => WithShort(AllListReq, page);

    /// <summary>Page through the current clan's members.</summary>
    public static byte[] BuildMemberRequest(short page) => WithShort(MemberReq, page);

    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];

    private static byte[] WithShort(byte sub, short value)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        w.SetByte(sub);
        w.SetShort(value);
        return w.Written.ToArray();
    }
}
