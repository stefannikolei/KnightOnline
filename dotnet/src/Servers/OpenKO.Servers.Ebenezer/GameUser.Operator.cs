using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The GM slice: WIZ_OPERATOR (arrest/forbid/chat bans) and the '+' chat
/// commands (Server/Ebenezer/OperationMessage.cpp — only the commands
/// implemented upstream are ported; the upstream TODO stubs stay unhandled).
/// </summary>
public sealed partial class GameUser
{
    // e_OperatorCommand (shared/packets.h).
    private const byte OperatorArrest = 1;
    private const byte OperatorForbidConnect = 2;
    private const byte OperatorChatForbid = 3;
    private const byte OperatorChatPermit = 4;

    private const byte AuthorityUser = 1;       // AUTHORITY_USER
    private const byte EndPermanentChat = 10;   // END_PERMANENT_CHAT
    private const byte AuthorityBlockedUser = 255; // AUTHORITY_BLOCK_USER

    /// <summary>CUser::OperatorCommand — WIZ_OPERATOR.</summary>
    public void OperatorCommand(ReadOnlySpan<byte> body)
    {
        if (UserData is not { Authority: GameConstants.AuthorityManager })
            return;

        var reader = new PacketReader(body);
        byte command = reader.GetByte();
        int idLen = reader.GetShort();
        if (idLen < 0 || idLen > MaxIdSize)
            return;

        string targetId = Encoding.Latin1.GetString(reader.GetString(idLen));

        GameUser? target = world.GetUserByCharId(targetId);
        if (target?.UserData is not { } targetData)
            return;

        switch (command)
        {
            case OperatorArrest:
                ZoneChange(targetData.Zone, targetData.CurX, targetData.CurZ);
                break;

            case OperatorForbidConnect:
                targetData.Authority = AuthorityBlockedUser;
                target.Close?.Invoke();
                break;

            case OperatorChatForbid:
                targetData.Authority = AuthorityNoChat;
                break;

            case OperatorChatPermit:
                targetData.Authority = AuthorityUser;
                break;
        }
    }

    /// <summary>OperationMessage::Process — the '+' GM chat commands.</summary>
    public bool OperationMessage(string command)
    {
        string[] args = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0)
            return false;

        switch (args[0])
        {
            case "+open":
                world.BattleZoneOpen(EbenezerWorld.BattlezoneOpen);
                return true;

            case "+snowopen":
                world.BattleZoneOpen(EbenezerWorld.SnowBattlezoneOpen);
                return true;

            case "+close":
                world.BanishFlag = 1;
                return true;

            case "+captain":
                // The C++ reloads the KNIGHTS_RATING table and announces the
                // captains; the port routes through the same rank-refresh hook.
                world.DailyKnightsRankRefresh?.Invoke();
                return true;

            case "+down":
                world.ServerDownFlag = true;
                world.KickOutAllUsers();
                return true;

            case "+discount":
                world.Discount = 1;
                return true;

            case "+alldiscount":
                world.Discount = 2;
                return true;

            case "+undiscount":
                world.Discount = 0;
                return true;

            case "+santa":
                world.Santa = 1;
                return true;

            case "+angel":
                world.Santa = 2;
                return true;

            case "+offsanta":
                world.Santa = 0;
                return true;

            case "+zonechange":
            {
                if (args.Length < 2 || UserData is not { } user)
                    return true; // handled, argument errors only log upstream

                if (!int.TryParse(args[1], out int zoneId))
                {
                    logger.LogWarning("OperationMessage: argument could not be parsed [command='{Command}']", command);
                    return true;
                }

                float x = user.CurX;
                float z = user.CurZ;
                if (args.Length >= 4
                    && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                    && float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float pz))
                {
                    x = px;
                    z = pz;
                }

                ZoneChange(zoneId, x, z);
                return true;
            }

            case "+permanent":
                world.PermanentChatMode = true;
                world.PermanentChatFlag = true;
                return true;

            case "+offpermanent":
            {
                world.PermanentChatMode = false;
                world.PermanentChatFlag = false;

                var buffer = new byte[8];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_CHAT);
                writer.SetByte(EndPermanentChat);
                writer.SetByte(0x01); // nation
                writer.SetShort(-1);  // sid
                writer.SetByte(0);    // sender name length
                writer.SetShort(0);   // empty SetString2
                world.SendAll(writer.Written);
                // The STS_CHAT UDP forward to sibling servers is not ported.
                return true;
            }

            default:
                return false;
        }
    }
}
