using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The WIZ_SKILLPT_CHANGE request (CUISkillTreeDlg::PointPushUpButton): a single
/// tab index the player is spending a skill point into. Byte-exact:
/// <c>[0x32][tabIndex]</c> — the tab index is the 1..8 <c>PointPushUpButton(iValue)</c>
/// value (1..4 = base pools, 5..8 = the four specialization mastery tabs).
/// </summary>
public static class SkillPointProtocol
{
    public static byte[] Build(byte tabIndex)
    {
        var buffer = new byte[2];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_SKILLPT_CHANGE);
        w.SetByte(tabIndex);
        return w.Written.ToArray();
    }
}
