using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser chat slice (User.cpp): WIZ_CHAT with all chat modes and the
/// private-chat target selection.
/// </summary>
public sealed partial class GameUser
{
    // e_ChatMode (shared/packets.h).
    public const byte GeneralChat = 1;
    public const byte PrivateChat = 2;
    public const byte PartyChat = 3;
    public const byte ForceChat = 4;
    public const byte ShoutChat = 5;
    public const byte KnightsChat = 6;
    public const byte PublicChat = 7;
    public const byte WarSystemChat = 8;
    public const byte CommandChat = 13;
    public const byte AnnouncementChat = 17;

    private const byte AuthorityNoChat = 11;    // AUTHORITY_NOCHAT
    private const int AnnouncementResource = 126; // IDP_ANNOUNCEMENT ("#### NOTICE : %s ####")

    /// <summary>m_sPrivateChatUser.</summary>
    public short PrivateChatUser = -1;

    /// <summary>CUser::Chat — the WIZ_CHAT dispatch.</summary>
    public void Chat(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        if (user.Authority == AuthorityNoChat)
            return;

        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        int chatLength = reader.GetShort();
        if (chatLength > 512 || chatLength <= 0)
            return;

        ReadOnlySpan<byte> chat = reader.GetString(chatLength);

        // The '+' operator commands (OperationMessage) attach with the GM slice.

        byte[] finalText;
        if (type is PublicChat or AnnouncementChat)
        {
            if (user.Authority != GameConstants.AuthorityManager)
                return;

            finalText = Encoding.Latin1.GetBytes(
                world.FormatResource(AnnouncementResource, Encoding.Latin1.GetString(chat)));
        }
        else
        {
            finalText = chat.ToArray();
        }

        var buffer = new byte[32 + user.CharId.Length + finalText.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CHAT);
        writer.SetByte(type);
        writer.SetByte(user.Nation);
        writer.SetShort(SocketId);
        writer.SetString1(Encoding.Latin1.GetBytes(user.CharId));
        writer.SetString2(finalText);

        switch (type)
        {
            case GeneralChat:
                world.SendNearRegion(writer.Written, user.Zone, RegionX, RegionZ, user.CurX, user.CurZ);
                break;

            case PrivateChat:
            {
                if (PrivateChatUser == SocketId)
                    break;

                GameUser? target = PrivateChatUser >= 0 && PrivateChatUser < world.Users.Length
                    ? world.Users[PrivateChatUser]
                    : null;
                if (target is null || target.State != ConnectionState.GameStart)
                    break;

                target.Send(writer.Written);
                Send(writer.Written);
                break;
            }

            case PartyChat:
                world.SendPartyMember(PartyIndex, writer.Written);
                break;

            case ForceChat:
                break;

            case ShoutChat:
                if (user.Mp < MaxMp / 5)
                    break;

                MSpChange(-(MaxMp / 5));
                world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, except: null, direct: false);
                break;

            case KnightsChat:
                // Send_KnightsMember attaches with the knights slice.
                break;

            case PublicChat:
                world.SendAll(writer.Written);
                break;

            case CommandChat:
                // Send_CommandChat (war command channel) attaches with the battle slice.
                break;
        }
    }

    /// <summary>CUser::ChatTargetSelect — resolve the private-chat partner by name.</summary>
    public void ChatTargetSelect(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int idLength = reader.GetShort();
        if (idLength > 20 || idLength < 0) // MAX_ID_SIZE
            return;

        string chatId = Encoding.Latin1.GetString(reader.GetString(idLength));

        GameUser? found = null;
        foreach (GameUser? candidate in world.Users)
        {
            if (candidate is not null
                && candidate.State == ConnectionState.GameStart
                && candidate.UserData is { } data
                && string.Equals(data.CharId, chatId, StringComparison.OrdinalIgnoreCase))
            {
                PrivateChatUser = candidate.SocketId;
                found = candidate;
                break;
            }
        }

        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CHAT_TARGET);
        if (found is null)
            writer.SetShort(0);
        else
            writer.SetString2(Encoding.Latin1.GetBytes(chatId));

        Send(writer.Written);
    }
}
