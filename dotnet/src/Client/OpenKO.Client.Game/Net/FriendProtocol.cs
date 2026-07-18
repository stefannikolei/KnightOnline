using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// One friend's online/party status parsed from a WIZ_FRIEND_PROCESS reply
/// (CUIFriends::MsgRecv_MemberInfo). <see cref="Status"/> bit 0 = online, bit 1 = in a party.
/// </summary>
public readonly record struct FriendStatus(string Name, short Id, byte Status)
{
    /// <summary>Status bit 0 — the friend is online.</summary>
    public bool Online => (Status & 0x01) != 0;

    /// <summary>Status bit 1 — the friend is in a party.</summary>
    public bool InParty => (Status & 0x02) != 0;
}

/// <summary>
/// The WIZ_FRIEND_PROCESS client/server message (CUIFriends) — the friend online/party status
/// query. <b>The server ignores it upstream</b> (<c>GameUser.cs</c>: <c>#if 0</c> "outdated" →
/// a silent no-op), so the request is sent but no reply arrives and the status columns stay
/// inert. The parse side is kept faithful for parity/tests.
/// </summary>
public static class FriendProtocol
{
    private static readonly Encoding Ascii = Encoding.Latin1;

    /// <summary>
    /// CUIFriends::MsgSend_MemberInfo — query a set of names:
    /// <c>[WIZ_FRIEND_PROCESS=0x49][s16 count]</c> then, per name, <c>[s16 len][name]</c>.
    /// </summary>
    public static byte[] BuildRequest(IReadOnlyList<string> names)
    {
        int size = 3;
        var encoded = new byte[names.Count][];
        for (int i = 0; i < names.Count; i++)
        {
            encoded[i] = Ascii.GetBytes(names[i]);
            size += 2 + encoded[i].Length;
        }

        var buffer = new byte[size];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_FRIEND_PROCESS);
        w.SetShort((short)names.Count);
        foreach (byte[] name in encoded)
            w.SetString2(name);
        return w.Written.ToArray();
    }

    /// <summary>
    /// Parse a WIZ_FRIEND_PROCESS reply (CUIFriends::MsgRecv_MemberInfo). After the opcode comes
    /// <c>[s16 count]</c> then, per name, <c>[s16 len][name][s16 id][u8 status]</c>. (Never sent
    /// by the current server — no-op upstream — but pinned for parity.)
    /// </summary>
    public static IReadOnlyList<FriendStatus> ParseReply(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_FRIEND_PROCESS
        short count = r.GetShort();
        var list = new List<FriendStatus>(Math.Max(0, (int)count));
        for (int i = 0; i < count; i++)
        {
            string name = Ascii.GetString(r.GetVarString(2));
            short id = r.GetShort();
            byte status = r.GetByte();
            list.Add(new FriendStatus(name, id, status));
        }

        return list;
    }
}
