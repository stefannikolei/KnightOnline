using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>One row of the party-recruitment board (__InfoPartyBBS, UIPartyBBS.h).</summary>
public readonly record struct PartyBbsEntry(string Name, byte Level, short Class);

/// <summary>
/// The parsed WIZ_PARTY_BBS reply (CUIPartyBBS::MsgRecv_RefreshData). <see cref="Type"/> is the
/// echoed sub-command (register / cancel / data), <see cref="Ok"/> the 0x01 result byte;
/// <see cref="Rows"/> holds the non-empty seekers on this page and <see cref="Page"/> /
/// <see cref="Total"/> drive the pager (max page = ceil(total / 23)).
/// </summary>
public readonly record struct PartyBbsPage(
    byte Type, bool Ok, IReadOnlyList<PartyBbsEntry> Rows, short Page, short Total);

/// <summary>
/// The WIZ_PARTY_BBS client/server messages (CUIPartyBBS) — the party-wanted bulletin board.
/// Pinned against the C# Ebenezer send side (<c>GameUser.Bbs.cs::PartyBbsList</c>): the body is
/// <c>[u8 type][u8 result]</c> then <b>exactly 23 rows</b> <c>[s16 nameLen][name][u8 level]
/// [s16 class]</c> (empty rows carry nameLen 0) and finally <c>[s16 page][s16 total]</c>.
/// </summary>
public static class PartyBbsProtocol
{
    /// <summary>MAX_BBS_PAGE / PARTY_BBS_MAXLINE — 23 rows per page.</summary>
    public const int RowsPerPage = 23;

    // e_PartyBbsOpcode (N3_SP_PARTY_*): register (0x01), cancel (0x02), data (0x03).
    public const byte Register = 0x01;
    public const byte Cancel = 0x02;
    public const byte Data = 0x03;

    private static readonly Encoding Ascii = Encoding.Latin1;

    /// <summary>
    /// CUIPartyBBS::MsgSend_RefreshData — request one page of seekers:
    /// <c>[WIZ_PARTY_BBS=0x4F][N3_SP_PARTY_BBS_DATA=0x03][s16 page]</c> (4 bytes).
    /// </summary>
    public static byte[] BuildRequestPage(short page)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_PARTY_BBS);
        w.SetByte(Data);
        w.SetShort(page);
        return w.Written.ToArray();
    }

    /// <summary>CUIPartyBBS::MsgSend_Register — flag myself as recruiting: <c>[0x4F][0x01]</c>.</summary>
    public static byte[] BuildRegister() => [(byte)GameOpcode.WIZ_PARTY_BBS, Register];

    /// <summary>CUIPartyBBS::MsgSend_RegisterCancel — clear the recruiting flag: <c>[0x4F][0x02]</c>.</summary>
    public static byte[] BuildCancel() => [(byte)GameOpcode.WIZ_PARTY_BBS, Cancel];

    /// <summary>
    /// Parse a WIZ_PARTY_BBS reply (CUIPartyBBS::MsgRecv_RefreshData). After the opcode comes
    /// <c>[u8 type][u8 result]</c>; a failure result (!= 0x01) has no body (the C++ returns early).
    /// A success reply carries the 23-row table then the page/total footer.
    /// </summary>
    public static PartyBbsPage ParseList(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_PARTY_BBS
        byte type = r.GetByte();
        byte result = r.GetByte();
        if (result != 0x01)
            return new PartyBbsPage(type, false, [], 0, 0);

        var rows = new List<PartyBbsEntry>(RowsPerPage);
        for (int i = 0; i < RowsPerPage; i++)
        {
            int nameLen = r.GetShort();
            string name = nameLen > 0 ? Ascii.GetString(r.GetString(nameLen)) : string.Empty;
            byte level = r.GetByte();
            short cls = r.GetShort();
            if (nameLen > 0)
                rows.Add(new PartyBbsEntry(name, level, cls));
        }

        short page = r.GetShort();
        short total = r.GetShort();
        return new PartyBbsPage(type, true, rows, page, total);
    }
}
