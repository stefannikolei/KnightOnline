using OpenKO.Core.Protocol;
using OpenKO.Core.Text;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The WIZ_NOTICE (0x2E) server push — the login/update notice banner. Ported from
/// <c>CUINotice</c> (Client/WarFare/UINotice.cpp); the server side is
/// <c>GameUser.GameStart.cs SendNotice()</c>.
/// </summary>
public static class NoticeProtocol
{
    /// <summary>
    /// Parse a WIZ_NOTICE push into its lines. Layout:
    /// <c>[0x2E][u8 count][ count × String1 ]</c> where String1 is
    /// <c>[u8 len][len bytes]</c> (CP949). The lines feed <c>CUINotice::GenerateText</c>.
    /// </summary>
    public static IReadOnlyList<string> ParseNotice(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_NOTICE
        int count = r.GetByte();

        var lines = new List<string>(count);
        for (int i = 0; i < count; i++)
            lines.Add(KoEncoding.Cp949.GetString(r.GetVarString(1)));

        return lines;
    }
}
