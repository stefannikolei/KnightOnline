using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// WIZ_POINT_CHANGE request — port of <c>CUIState::MsgSendAblityPointChange</c>
/// (Client/WarFare/UIVarious.cpp). Spends a stat bonus point:
/// <c>[WIZ_POINT_CHANGE][byType][siValueDelta:i16]</c>. The type is
/// 1=Str, 2=Sta, 3=Dex, 4=Int, 5=MagicAttack; the delta is +1 per press.
/// </summary>
public static class StatPointProtocol
{
    public const byte Strength = 0x01;
    public const byte Stamina = 0x02;
    public const byte Dexterity = 0x03;
    public const byte Intelligence = 0x04;
    public const byte MagicAttack = 0x05;

    /// <summary>CUIState::MsgSendAblityPointChange(byType, siValueDelta).</summary>
    public static byte[] Build(byte type, short delta)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_POINT_CHANGE);
        w.SetByte(type);
        w.SetShort(delta);
        return w.Written.ToArray();
    }
}
