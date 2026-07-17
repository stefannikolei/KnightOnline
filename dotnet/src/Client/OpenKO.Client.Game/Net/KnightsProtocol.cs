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

    /// <summary>
    /// Request to join a clan by its index (CGameProcMain::MsgSend_KnightsJoin — also the
    /// clan-page "Admit" which passes the target member's socket id).
    /// </summary>
    public static byte[] BuildJoin(short knightsId) => WithShort(Join, knightsId);

    /// <summary>Leave the current clan.</summary>
    public static byte[] BuildWithdraw() => [(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, Withdraw];

    /// <summary>
    /// Chief disbands the whole clan
    /// (CUIKnightsOperation::MsgSend_KnightsDestroy — <c>[WIZ_KNIGHTS_PROCESS][0x05]</c>).
    /// </summary>
    public static byte[] BuildDestroy() => [(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, Destroy];

    /// <summary>Page through the clan list.</summary>
    public static byte[] BuildAllListRequest(short page) => WithShort(AllListReq, page);

    /// <summary>Page through the current clan's members.</summary>
    public static byte[] BuildMemberRequest(short page) => WithShort(MemberReq, page);

    /// <summary>
    /// Request the full clan member list (CUIKnights::MsgSend_MemberInfoAll —
    /// <c>[WIZ_KNIGHTS_PROCESS][0x0D]</c>, no page, unlike <see cref="BuildMemberRequest"/>).
    /// </summary>
    public static byte[] BuildMemberInfoAll() => [(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, MemberReq];

    /// <summary>
    /// Expel a member by name (CGameProcMain::MsgSend_KnightsLeave —
    /// <c>[WIZ_KNIGHTS_PROCESS][0x04][s2 len][name]</c>).
    /// </summary>
    public static byte[] BuildExpel(string name) => WithString(Remove, name);

    /// <summary>
    /// Appoint a member vice-chief by name (CGameProcMain::MsgSend_KnightsAppointViceChief —
    /// <c>[WIZ_KNIGHTS_PROCESS][0x0A][s2 len][name]</c>).
    /// </summary>
    public static byte[] BuildAppointViceChief(string name) => WithString(ViceChief, name);

    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];

    /// <summary>A clan row from the AllListReq broadcast (CUIKnightsOperation::MsgRecv_KnightsList).</summary>
    public sealed record ClanListRow(short Id, string Name, string ChiefName, int MemberCount, uint Point);

    /// <summary>The parsed clan list (page + rows).</summary>
    public sealed record ClanList(short Page, IReadOnlyList<ClanListRow> Rows);

    /// <summary>A member row from the MemberInfoAll broadcast (CUIKnights::MsgRecv_MemberInfo).</summary>
    public sealed record ClanMemberRow(string Name, byte Duty, byte Level, short Class, bool Connected);

    /// <summary>The parsed clan member list (online/total counts + rows).</summary>
    public sealed record ClanMemberList(short Online, short Total, IReadOnlyList<ClanMemberRow> Members);

    /// <summary>
    /// Parse the clan list broadcast (CUIKnightsOperation::MsgRecv_KnightsList): after the
    /// <c>[opcode][subcmd]</c> header — <c>s2 page, s2 count, count×{ s2 id, s2 nameLen, name,
    /// s2 memberCount, s2 chiefNameLen, chief, u4 point }</c>.
    /// </summary>
    public static ClanList ParseClanList(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload) { Index = 2 };
        short page = r.GetShort();
        int count = r.GetShort();
        var rows = new List<ClanListRow>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            short id = r.GetShort();
            string name = Ascii.GetString(r.GetVarString(2));
            short memberCount = r.GetShort();
            string chief = Ascii.GetString(r.GetVarString(2));
            uint point = r.GetDWord();
            rows.Add(new ClanListRow(id, name, chief, memberCount, point));
        }

        return new ClanList(page, rows);
    }

    /// <summary>
    /// Parse the clan member list broadcast (CGameProcMain::MsgRecv_Knights_MemberInfoAll →
    /// CUIKnights::MsgRecv_MemberInfo): after <c>[opcode][subcmd]</c> — a common status byte
    /// (0x01 = success) then <c>s2 (unused size), s2 online, s2 total, s2 count,
    /// count×{ s2 nameLen, name, b1 duty, b1 level, s2 class, b1 connected }</c>. Returns an
    /// empty list when the status byte is not success.
    /// </summary>
    public static ClanMemberList ParseMemberList(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload) { Index = 2 };
        byte common = r.GetByte();
        if (common != CommonSuccess)
            return new ClanMemberList(0, 0, []);

        r.GetShort(); // packet size (unused, matches the C++)
        short online = r.GetShort();
        short total = r.GetShort();
        int count = r.GetShort();
        var members = new List<ClanMemberRow>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            string name = Ascii.GetString(r.GetVarString(2));
            byte duty = r.GetByte();
            byte level = r.GetByte();
            short cls = r.GetShort();
            bool connected = r.GetByte() != 0;
            members.Add(new ClanMemberRow(name, duty, level, cls, connected));
        }

        return new ClanMemberList(online, total, members);
    }

    /// <summary>N3_SP_KNIGHTS_COMMON_SUCCESS — the leading status byte on member-info replies.</summary>
    private const byte CommonSuccess = 0x01;

    private static byte[] WithShort(byte sub, short value)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        w.SetByte(sub);
        w.SetShort(value);
        return w.Written.ToArray();
    }

    private static byte[] WithString(byte sub, string name)
    {
        var buffer = new byte[4 + name.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        w.SetByte(sub);
        w.SetString2(Ascii.GetBytes(name));
        return w.Written.ToArray();
    }
}
