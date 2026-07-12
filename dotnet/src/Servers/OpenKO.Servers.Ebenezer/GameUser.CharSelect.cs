using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The pre-game character flow of <c>CUser</c> (User.cpp) merged with the Aujard
/// queue handlers (AujardApp.cpp) and the read-queue replies: nation select,
/// character create/delete, the all-characters info blob and character select.
/// </summary>
public sealed partial class GameUser
{
    private const byte AuthorityBlockUser = 255; // AUTHORITY_BLOCK_USER
    private const byte FameChief = 0x01;         // CHIEF (knights authority)
    private const byte FameCommandCaptain = 100; // COMMAND_CAPTAIN
    private const byte NoBattle = 0;             // NO_BATTLE
    private const byte NationBattle = 1;         // NATION_BATTLE
    private const byte SnowBattle = 2;           // SNOW_BATTLE
    private const int ZoneBattle = 101;          // ZONE_BATTLE
    private const int ZoneSnowBattle = 111;      // ZONE_SNOW_BATTLE
    private const int ZoneFrontier = 201;        // ZONE_FRONTIER

    /// <summary>_USER_DATA of the selected character (dbAgent.Users[socketId]).</summary>
    public UserData? UserData;

    /// <summary>
    /// CUser::SelNationToAgent + AujardApp::SelectNation: validates the nation,
    /// runs NationSelect and replies [WIZ_SEL_NATION][nation | 0x00].
    /// </summary>
    public async ValueTask SelNationToAgentAsync(ReadOnlyMemory<byte> body)
    {
        int nation = body.Span[0];

        byte result = 0x00;
        if (nation <= 2)
        {
            bool ok = await dbAgent.NationSelectAsync(AccountId, nation);
            if (ok)
                result = (byte)nation;
        }

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SEL_NATION);
        writer.SetByte(result);
        Send(writer.Written);
    }

    /// <summary>
    /// CUser::NewCharToAgent + AujardApp::CreateNewChar: validates index/name/
    /// class/stats, creates the character and replies [WIZ_NEW_CHAR][result].
    /// </summary>
    public async ValueTask NewCharToAgentAsync(ReadOnlyMemory<byte> body)
    {
        int charIndex, race, cls, face, hair, str, sta, dex, intel, cha;
        string charId;
        {
            var reader = new PacketReader(body.Span);

            charIndex = reader.GetByte();
            int idLen = reader.GetShort();
            if (idLen > MaxIdSize || idLen <= 0)
            {
                SendNewCharResult(0x05);
                return;
            }

            charId = Encoding.Latin1.GetString(reader.GetString(idLen));
            race = reader.GetByte();
            cls = reader.GetShort();
            face = reader.GetByte();
            hair = reader.GetByte();
            str = reader.GetByte();
            sta = reader.GetByte();
            dex = reader.GetByte();
            intel = reader.GetByte();
            cha = reader.GetByte();
        }

        if (charIndex is > 4 or < 0)
        {
            SendNewCharResult(0x01);
            return;
        }

        if (!IsValidName(charId))
        {
            SendNewCharResult(0x05);
            return;
        }

        if (!world.CoefficientTable.ContainsKey((short)cls))
        {
            SendNewCharResult(0x02);
            return;
        }

        if (str + sta + dex + intel + cha > 300)
        {
            SendNewCharResult(0x02);
            return;
        }

        if (str < 50 || sta < 50 || dex < 50 || intel < 50 || cha < 50)
        {
            SendNewCharResult(0x11);
            return;
        }

        // AujardApp::CreateNewChar → CDBAgent::CreateNewChar(…, hair, face, …).
        NewCharResult result = await dbAgent.CreateNewCharAsync(
            AccountId, charIndex, charId, race, cls, hair, face, str, sta, dex, intel, cha);

        SendNewCharResult((byte)result);
    }

    private void SendNewCharResult(byte result)
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NEW_CHAR);
        writer.SetByte(result);
        Send(writer.Written);
    }

    /// <summary>
    /// CUser::DelCharToAgent + AujardApp::DeleteChar + CUser::RecvDeleteChar.
    /// Character deletion is not implemented upstream (result stays 0), so both
    /// the validation failures and the "success" path reply [0x00][0xFF].
    /// </summary>
    public ValueTask DelCharToAgentAsync(ReadOnlyMemory<byte> body)
    {
        var reader = new PacketReader(body.Span);

        bool valid = true;

        int charIndex = reader.GetByte();
        if (charIndex > 4)
            valid = false;

        if (valid)
        {
            int idLen = reader.GetShort();
            if (idLen > MaxIdSize || idLen <= 0)
            {
                valid = false;
            }
            else
            {
                reader.GetString(idLen); // charId
                int socLen = reader.GetShort();
                if (socLen > 14 || socLen <= 0)
                    valid = false;
                else
                    reader.GetString(socLen); // social number

                // Clan chiefs must leave their clan before deleting.
                if (valid && UserData is { Knights: > 0, Fame: FameChief })
                    valid = false;
            }
        }

        _ = valid; // both paths reply identically while deletion is unimplemented

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_DEL_CHAR);
        writer.SetByte(0x00);
        writer.SetByte(0xFF);
        Send(writer.Written);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// CUser::AllCharInfoToAgent + AujardApp::AllCharInfoReq: loads the three
    /// character slots and streams the byte-exact info blob back:
    /// [WIZ_ALLCHAR_INFO_REQ][0x01][3 × charinfo].
    /// </summary>
    public async ValueTask AllCharInfoToAgentAsync()
    {
        AllCharIds? ids = await dbAgent.GetAllCharIdsAsync(AccountId);

        var slots = new CharInfo[3];
        string[] charIds = [ids?.CharId1 ?? "", ids?.CharId2 ?? "", ids?.CharId3 ?? ""];
        for (int i = 0; i < 3; i++)
        {
            CharInfo? info = charIds[i].Length > 0 ? await dbAgent.LoadCharInfoAsync(charIds[i]) : null;
            // DB errors leave the C++ blob half-filled; treat as an empty slot.
            slots[i] = info ?? CharInfo.Empty(charIds[i]);
        }

        var buffer = new byte[1024];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ALLCHAR_INFO_REQ);
        writer.SetByte(0x01); // result

        foreach (CharInfo info in slots)
            WriteCharInfo(ref writer, info);

        Send(writer.Written);
    }

    /// <summary>CDBAgent::LoadCharInfo blob: id, race/class/level/face/hair/zone + 8 visible items.</summary>
    private static void WriteCharInfo(ref PacketWriter writer, CharInfo info)
    {
        writer.SetString2(Encoding.Latin1.GetBytes(info.CharId));
        writer.SetByte(info.Race);
        writer.SetShort(info.Class);
        writer.SetByte(info.Level);
        writer.SetByte(info.Face);
        writer.SetByte(info.HairColor);
        writer.SetByte(info.Zone);

        // HEAD, BREAST, SHOULDER, LEG, GLOVE, FOOT, LEFTHAND, RIGHTHAND in slot order.
        for (int i = 0; i < 8; i++)
        {
            (int itemId, short duration) = i < info.VisibleEquipment.Count ? info.VisibleEquipment[i] : default;
            writer.SetDWord((uint)itemId);
            writer.SetShort(duration);
        }
    }

    /// <summary>
    /// CUser::SelCharToAgent + AujardApp::SelectCharacter + CUser::SelectCharacter:
    /// duplicate kicks, zone/server routing, the user-data load and the
    /// [WIZ_SEL_CHAR][1][zone][x*10][z*10][y*10][victory] reply.
    /// </summary>
    public async ValueTask SelCharToAgentAsync(ReadOnlyMemory<byte> body)
    {
        string accountId;
        string charId;
        byte init;
        int zoneId;
        {
            var reader = new PacketReader(body.Span);

            int idLen1 = reader.GetShort();
            if (idLen1 > MaxIdSize || idLen1 <= 0)
            {
                SendSelCharFail();
                return;
            }

            accountId = Encoding.Latin1.GetString(reader.GetString(idLen1));

            int idLen2 = reader.GetShort();
            if (idLen2 > MaxIdSize || idLen2 <= 0)
            {
                SendSelCharFail();
                return;
            }

            charId = Encoding.Latin1.GetString(reader.GetString(idLen2));
            init = reader.GetByte();
            zoneId = reader.GetByte();
        }

        // Zone moves skip the login procedure, so adopt the account id here.
        if (!string.Equals(accountId, AccountId, StringComparison.OrdinalIgnoreCase))
        {
            GameUser? sameAccount = world.GetUserByAccount(accountId);
            if (sameAccount is not null && sameAccount.SocketId != SocketId)
            {
                sameAccount.Close?.Invoke();
                SendSelCharFail();
                return;
            }

            AccountId = accountId;
        }

        GameUser? sameChar = world.GetUserByCharId(charId);
        if (sameChar is not null && sameChar.SocketId != SocketId)
        {
            sameChar.Close?.Invoke();
            SendSelCharFail();
            return;
        }

        if (zoneId <= 0)
        {
            logger.LogError("SelCharToAgent: invalid zoneId={ZoneId}", zoneId);
            SendSelCharFail();
            return;
        }

        GameZone? zone = world.GetZoneById(zoneId);
        if (zone is null)
        {
            logger.LogError("SelCharToAgent: no map found for zoneId={ZoneId}", zoneId);
            SendSelCharFail();
            return;
        }

        if (world.ServerNo != zone.ServerNo)
        {
            SendServerChange(zone.ServerNo, init, (byte)zoneId);
            return;
        }

        world.PacketCount++;

        // ---- agent side (AujardApp::SelectCharacter) ----

        if (accountId.Length == 0 || charId.Length == 0)
        {
            SendSelCharFail();
            return;
        }

        // The character is still loaded in another slot: log that slot out
        // (save + reset); the C++ never answers the requester in this case.
        UserData? loaded = dbAgent.Users.FindByCharId(charId, out int loadedUserId);
        if (loaded is not null)
        {
            await LogoutSlotAsync(loadedUserId);
            return;
        }

        if (!await dbAgent.LoadUserDataAsync(accountId, charId, SocketId)
            || !await dbAgent.LoadWarehouseAsync(accountId, SocketId))
        {
            SendSelCharFail();
            return;
        }

        UserData? user = dbAgent.Users.Get(SocketId);
        if (user is null)
        {
            SendSelCharFail();
            return;
        }

        user.AccountId = accountId;
        UserData = user;

        await SelectCharacterAsync(result: 0x01, init);
    }

    /// <summary>
    /// CUser::SelectCharacter (the read-queue continuation): battle/zone
    /// validation, login-info write and the position reply.
    /// </summary>
    public async ValueTask SelectCharacterAsync(byte result, byte init)
    {
        UserData? user = UserData;

        if (result == 0 || user is null || user.Zone == 0)
        {
            SendSelCharFail();
            return;
        }

        GameZone? zone = world.GetZoneById(user.Zone);
        if (zone is null)
        {
            SendSelCharFail();
            return;
        }

        if (world.ServerNo != zone.ServerNo)
        {
            SendServerChange(zone.ServerNo, init, user.Zone);
            return;
        }

        if (user.Authority == AuthorityBlockUser)
        {
            Close?.Invoke();
            return;
        }

        // Outside of wars a commander reverts to a plain clan chief.
        if (world.BattleOpen == NoBattle && user.Fame == FameCommandCaptain)
            user.Fame = FameChief;

        if (user.Zone != user.Nation && user.Zone < 3 && world.BattleOpen == NoBattle)
        {
            NativeZoneReturn();
            Close?.Invoke();
            return;
        }

        if (user.Zone == ZoneBattle && world.BattleOpen != NationBattle)
        {
            NativeZoneReturn();
            Close?.Invoke();
            return;
        }

        if (user.Zone == ZoneSnowBattle && world.BattleOpen != SnowBattle)
        {
            NativeZoneReturn();
            Close?.Invoke();
            return;
        }

        if (user.Zone == ZoneFrontier && world.BattleOpen != NoBattle)
        {
            NativeZoneReturn();
            Close?.Invoke();
            return;
        }

        await SetLoginInfoToDbAsync(init);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SEL_CHAR);
        writer.SetByte(result);
        writer.SetByte(user.Zone);
        writer.SetShort((short)(ushort)(user.CurX * 10));
        writer.SetShort((short)(ushort)(user.CurZ * 10));
        writer.SetShort((short)(user.CurY * 10));
        writer.SetByte(world.OldVictory);
        Send(writer.Written);

        SetDetailData();

        // Knights bookkeeping attaches with the KnightsManager slice; banished
        // members already reset here.
        if (user.Knights == -1)
        {
            user.Knights = 0;
            user.Fame = 0;
            return;
        }

        // WIZ_DATASAVE login entry for the ItemManager log.
        SendItemLogDataSave(loginFlag: 0x01);
    }

    /// <summary>The WIZ_SERVER_CHANGE redirect to another game server.</summary>
    private void SendServerChange(short serverNo, byte init, byte zoneId)
    {
        ZoneServerInfo? info = world.ServerInfos.GetValueOrDefault(serverNo);
        if (info is null)
        {
            logger.LogError("SelChar: serverId={ServerNo} not registered [zoneId={ZoneId}]", serverNo, zoneId);
            SendSelCharFail();
            return;
        }

        byte[] ip = Encoding.Latin1.GetBytes(info.ServerIp);
        var buffer = new byte[16 + ip.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SERVER_CHANGE);
        writer.SetString2(ip);
        writer.SetShort(info.Port);
        writer.SetByte(init);
        writer.SetByte(zoneId);
        writer.SetByte(world.OldVictory);
        Send(writer.Written);
    }

    private void SendSelCharFail()
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SEL_CHAR);
        writer.SetByte(0x00);
        Send(writer.Written);
    }

    /// <summary>
    /// AujardApp::HandleUserLogout for a store slot whose character collided:
    /// account logout + USERDATA/WAREHOUSE save + slot reset, then closing the
    /// session that owns it.
    /// </summary>
    private async ValueTask LogoutSlotAsync(int userId)
    {
        UserData? slot = dbAgent.Users.Get(userId);
        if (slot is null || slot.CharId.Length == 0)
            return;

        if (slot.Logout != 2)
            await dbAgent.AccountLogoutAsync(slot.AccountId);

        await dbAgent.UpdateUserAsync(slot.CharId, userId, UserUpdateType.Logout);
        await dbAgent.UpdateWarehouseAsync(slot.AccountId, userId, UserUpdateType.Logout);
        dbAgent.Users.Reset(userId);

        if (userId >= 0 && userId < world.Users.Length)
            world.Users[userId]?.Close?.Invoke();
    }

    /// <summary>CUser::SetLogInInfoToDB — CURRENTUSER insert/update for kicking/billing.</summary>
    private async ValueTask SetLoginInfoToDbAsync(byte init)
    {
        if (UserData is not { } user)
            return;

        ZoneServerInfo? self = world.ServerInfos.GetValueOrDefault(world.ServerNo);
        await dbAgent.SetLoginInfoAsync(
            user.AccountId, user.CharId, self?.ServerIp ?? "127.0.0.1", world.ServerNo,
            RemoteIp, init);
    }

    /// <summary>
    /// CUser::NativeZoneReturn — sends the character back to its home zone.
    /// Needs the HOME table (stage 4.3); until then only the position reset is
    /// skipped and the session still closes.
    /// </summary>
    private void NativeZoneReturn()
    {
        logger.LogDebug("NativeZoneReturn: HOME relocation not yet ported [charId={CharId}]",
            UserData?.CharId);
    }

    /// <summary>The WIZ_DATASAVE line CUser::SelectCharacter pushes to the ItemManager.</summary>
    private void SendItemLogDataSave(byte loginFlag)
    {
        if (world.ItemLogSink is null || UserData is not { } user)
            return;

        var buffer = new byte[128];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_DATASAVE);
        writer.SetString2(Encoding.Latin1.GetBytes(user.AccountId));
        writer.SetString2(Encoding.Latin1.GetBytes(user.CharId));
        writer.SetByte(loginFlag);
        writer.SetByte(user.Level);
        writer.SetDWord((uint)user.Exp);
        writer.SetDWord((uint)user.Loyalty);
        writer.SetDWord((uint)user.Gold);
        world.ItemLogSink(writer.Written.ToArray());
    }

    /// <summary>CUser::IsValidName — rejects names containing the blocked substrings.</summary>
    public static bool IsValidName(string name)
    {
        string[] invalids =
        [
            "~", "`", "!", "@", "#", "$", "%", "^", "&", "*",
            "(", ")", "-", "+", "=", "|", "\\", "<", ">", ",",
            ".", "?", "/", "{", "[", "}", "]", "\"", "'", " ", "　",
            "Knight", "Noahsystem", "Wizgate", "Mgame",
        ];

        foreach (string invalid in invalids)
        {
            if (name.Contains(invalid, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
