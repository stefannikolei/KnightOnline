using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OpenKO.Data;
using OpenKO.Data.Models;

namespace OpenKO.Servers.Aujard;

/// <summary>
/// SqlClient port of <c>CDBAgent</c> (Server/Aujard/DBAgent.cpp) against the
/// unchanged KN_online schema and stored procedures. Stored procedures are
/// invoked positionally (like the ODBC reference) so parameter names don't matter.
/// </summary>
public sealed class DbAgent(SqlConnectionFactory connectionFactory, ILogger<DbAgent> logger) : IDbAgent
{
    private Dictionary<int, ItemRow> _itemTable = [];

    public UserDataStore Users { get; } = new();

    private ItemRow? LookupItem(int itemId) => _itemTable.GetValueOrDefault(itemId);

    public async Task<bool> InitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT Num, Countable FROM ITEM", connection);

            var items = new Dictionary<int, ItemRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int num = reader.GetInt32(0);
                byte countable = Convert.ToByte(reader.GetValue(1));
                items[num] = new ItemRow(num, countable);
            }

            // Load_ForbidEmpty: an empty ITEM table is a startup failure.
            if (items.Count == 0)
            {
                logger.LogError("Item Table Load Fail!!");
                return false;
            }

            _itemTable = items;
            logger.LogInformation("Item table loaded: {Count} items", items.Count);
            return true;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "DbAgent.Init failed");
            return false;
        }
    }

    // ------------------------------------------------------------- account/login

    public async Task<int> AccountLoginAsync(string accountId, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC ACCOUNT_LOGIN @accountId, @password, @ret OUTPUT", connection);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@password", password);
            var ret = command.Parameters.Add("@ret", SqlDbType.SmallInt);
            ret.Direction = ParameterDirection.Output;

            await command.ExecuteNonQueryAsync(cancellationToken);

            short retCode = ret.Value is short s ? s : (short)0;
            return retCode - 1;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "AccountLogin failed");
            // NOTE: the C++ returns `false` (0) from this int function on DB error,
            // not -1; replicated for behavioral fidelity.
            return 0;
        }
    }

    public async Task<bool> NationSelectAsync(string accountId, int nation, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC NATION_SELECT @ret OUTPUT, @accountId, @nation", connection);
            var ret = command.Parameters.Add("@ret", SqlDbType.SmallInt);
            ret.Direction = ParameterDirection.Output;
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@nation", nation);

            await command.ExecuteNonQueryAsync(cancellationToken);

            return ret.Value is short retCode && retCode == 1;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "NationSelect failed");
            return false;
        }
    }

    public async Task<NewCharResult> CreateNewCharAsync(
        string accountId, int index, string charId, int race, int cls,
        int hair, int face, int str, int sta, int dex, int intel, int cha,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC CREATE_NEW_CHAR @ret OUTPUT, @accountId, @index, @charId, @race, @class, @hair, @face, @str, @sta, @dex, @intel, @cha",
                connection);
            var ret = command.Parameters.Add("@ret", SqlDbType.SmallInt);
            ret.Direction = ParameterDirection.Output;
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@index", index);
            command.Parameters.AddWithValue("@charId", charId);
            command.Parameters.AddWithValue("@race", race);
            command.Parameters.AddWithValue("@class", cls);
            command.Parameters.AddWithValue("@hair", hair);
            command.Parameters.AddWithValue("@face", face);
            command.Parameters.AddWithValue("@str", str);
            command.Parameters.AddWithValue("@sta", sta);
            command.Parameters.AddWithValue("@dex", dex);
            command.Parameters.AddWithValue("@intel", intel);
            command.Parameters.AddWithValue("@cha", cha);

            await command.ExecuteNonQueryAsync(cancellationToken);

            return ret.Value is short retCode ? (NewCharResult)retCode : NewCharResult.Error;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "CreateNewChar failed");
            return NewCharResult.Error;
        }
    }

    public async Task<AllCharIds?> GetAllCharIdsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "DECLARE @rc INT; EXEC @rc = LOAD_ACCOUNT_CHARID @accountId", connection);
            command.Parameters.AddWithValue("@accountId", accountId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                logger.LogError("GetAllCharIds: no rows for accountId={AccountId}", accountId);
                return null;
            }

            return new AllCharIds(
                ReadTrimmedString(reader, 0),
                ReadTrimmedString(reader, 1),
                ReadTrimmedString(reader, 2));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "GetAllCharIds failed");
            return null;
        }
    }

    // ------------------------------------------------------------- character data

    public async Task<CharInfo?> LoadCharInfoAsync(string charId, CancellationToken cancellationToken = default)
    {
        // Requested for all 3 slots; empty slots are answered locally with zeroes.
        if (charId.Length == 0)
            return CharInfo.Empty(charId);

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC LOAD_CHAR_INFO @charId, @rowCount OUTPUT", connection);
            command.Parameters.AddWithValue("@charId", charId);
            var rowCount = command.Parameters.Add("@rowCount", SqlDbType.SmallInt);
            rowCount.Direction = ParameterDirection.Output;

            byte race, hairColor, level, face, zone;
            short cls;
            byte[] itemsBlob = [];

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    logger.LogError("LoadCharInfo: expected row for charId={CharId}", charId);
                    return null;
                }

                race = Convert.ToByte(reader.GetValue(0));
                cls = Convert.ToInt16(reader.GetValue(1));
                hairColor = Convert.ToByte(reader.GetValue(2));
                level = Convert.ToByte(reader.GetValue(3));
                face = Convert.ToByte(reader.GetValue(4));
                zone = Convert.ToByte(reader.GetValue(5));

                if (!reader.IsDBNull(6))
                    itemsBlob = ReadBlob(reader, 6);
            }

            // Walk the 14 equip slots; the 8 visible ones go out in slot order.
            var items = new Core.IO.ByteBuffer(Math.Max(itemsBlob.Length, 1));
            items.Append(itemsBlob);
            items.SyncForRead();

            var visible = new List<(int ItemId, short Duration)>(8);
            for (int i = 0; i < GameConstants.SlotMax; i++)
            {
                int itemId = items.ReadInt32();
                short duration = items.ReadInt16();
                items.ReadInt16(); // count, unused

                if (i is GameConstants.SlotHead or GameConstants.SlotBreast or GameConstants.SlotShoulder
                    or GameConstants.SlotLeg or GameConstants.SlotGlove or GameConstants.SlotFoot
                    or GameConstants.SlotLeftHand or GameConstants.SlotRightHand)
                {
                    visible.Add((itemId, duration));
                }
            }

            return new CharInfo(charId, race, cls, level, face, hairColor, zone, visible);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadCharInfo failed");
            return null;
        }
    }

    public async Task<bool> LoadUserDataAsync(string accountId, string charId, int userId, CancellationToken cancellationToken = default)
    {
        UserData? user = Users.Get(userId);
        if (user is null)
        {
            logger.LogError("LoadUserData: UserData[{UserId}] not found for charId={CharId}", userId, charId);
            return false;
        }

        if (user.Logout != 0)
        {
            logger.LogError("LoadUserData: logout error: charId={CharId}, logout={Logout}", charId, user.Logout);
            return false;
        }

        byte nation, race, hairColor, rank, title, level, face, city, fame, authority, points, zone;
        uint exp, loyalty, gold, px, pz, py, dwTime, mannerPoint, loyaltyMonthly;
        short hp, mp, sp, cls, bind = 0, knights, questCount;
        byte str, sta, dex, intel, cha;
        byte[] skillsBlob = [], itemsBlob = [], serialsBlob = [], questsBlob = [];

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC LOAD_USER_DATA @accountId, @charId, @rowCount OUTPUT", connection);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@charId", charId);
            var rowCount = command.Parameters.Add("@rowCount", SqlDbType.SmallInt);
            rowCount.Direction = ParameterDirection.Output;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                logger.LogError("LoadUserData: expected row for charId={CharId}", charId);
                return false;
            }

            nation = Convert.ToByte(reader.GetValue(0));
            race = Convert.ToByte(reader.GetValue(1));
            cls = Convert.ToInt16(reader.GetValue(2));
            hairColor = Convert.ToByte(reader.GetValue(3));
            rank = Convert.ToByte(reader.GetValue(4));
            title = Convert.ToByte(reader.GetValue(5));
            level = Convert.ToByte(reader.GetValue(6));
            exp = Convert.ToUInt32(reader.GetValue(7));
            loyalty = Convert.ToUInt32(reader.GetValue(8));
            face = Convert.ToByte(reader.GetValue(9));
            city = Convert.ToByte(reader.GetValue(10));
            knights = Convert.ToInt16(reader.GetValue(11));
            fame = Convert.ToByte(reader.GetValue(12));
            hp = Convert.ToInt16(reader.GetValue(13));
            mp = Convert.ToInt16(reader.GetValue(14));
            sp = Convert.ToInt16(reader.GetValue(15));
            str = Convert.ToByte(reader.GetValue(16));
            sta = Convert.ToByte(reader.GetValue(17));
            dex = Convert.ToByte(reader.GetValue(18));
            intel = Convert.ToByte(reader.GetValue(19));
            cha = Convert.ToByte(reader.GetValue(20));
            authority = Convert.ToByte(reader.GetValue(21));
            points = Convert.ToByte(reader.GetValue(22));
            gold = Convert.ToUInt32(reader.GetValue(23));
            zone = Convert.ToByte(reader.GetValue(24));
            if (!reader.IsDBNull(25))
                bind = Convert.ToInt16(reader.GetValue(25));
            px = Convert.ToUInt32(reader.GetValue(26));
            pz = Convert.ToUInt32(reader.GetValue(27));
            py = Convert.ToUInt32(reader.GetValue(28));
            dwTime = Convert.ToUInt32(reader.GetValue(29));

            if (!reader.IsDBNull(30))
                skillsBlob = ReadBlob(reader, 30);
            if (!reader.IsDBNull(31))
                itemsBlob = ReadBlob(reader, 31);
            if (!reader.IsDBNull(32))
                serialsBlob = ReadBlob(reader, 32);

            questCount = Convert.ToInt16(reader.GetValue(33));

            if (!reader.IsDBNull(34))
                questsBlob = ReadBlob(reader, 34);

            mannerPoint = Convert.ToUInt32(reader.GetValue(35));
            loyaltyMonthly = Convert.ToUInt32(reader.GetValue(36));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadUserData failed");
            return false;
        }

        user.CharId = charId;
        user.Zone = zone;
        // Integer division before the float conversion, as in the C++.
        user.CurX = px / 100;
        user.CurZ = pz / 100;
        user.CurY = py / 100;
        user.Nation = nation;
        user.Race = race;
        user.Class = cls;
        user.HairColor = hairColor;
        user.Rank = rank;
        user.Title = title;
        user.Level = level;
        user.Exp = (int)exp;
        user.Loyalty = (int)loyalty;
        user.Face = face;
        user.City = city;
        user.Knights = knights;
        user.Fame = fame;
        user.Hp = hp;
        user.Mp = mp;
        user.Sp = sp;
        user.Str = str;
        user.Sta = sta;
        user.Dex = dex;
        user.Intel = intel;
        user.Cha = cha;
        user.Authority = authority;
        user.Points = points;
        user.Gold = (int)gold;
        user.Bind = bind;
        user.Time = dwTime + 1;
        user.MannerPoint = (int)mannerPoint;
        user.LoyaltyMonthly = (int)loyaltyMonthly;

        UserDataBlobCodec.ApplySkillsBlob(user, skillsBlob);
        UserDataBlobCodec.ApplyInventoryBlobs(user, itemsBlob, serialsBlob, LookupItem,
            droppedItemId => logger.LogError("LoadUserData: Item Drop [charId={CharId} itemId={ItemId}]", charId, droppedItemId));

        short questTotal = UserDataBlobCodec.ApplyQuestBlob(user, questsBlob);
        if (questCount != questTotal)
            user.QuestCount = questTotal;

        UserDataBlobCodec.ApplyStarterWeapon(user);

        return true;
    }

    public async Task<bool> UpdateUserAsync(string charId, int userId, UserUpdateType updateType, CancellationToken cancellationToken = default)
    {
        UserData? user = Users.Get(userId);
        if (user is null)
            return false;

        if (!string.Equals(user.CharId, charId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (updateType == UserUpdateType.PacketSave)
            user.Time++;
        else if (updateType is UserUpdateType.Logout or UserUpdateType.AllSave)
            user.Time = 0;

        (byte[] questsBlob, short questTotal) = UserDataBlobCodec.BuildQuestBlob(user);
        if (questTotal != user.QuestCount)
            user.QuestCount = questTotal;

        foreach (ref readonly ItemData item in user.Items.AsSpan())
        {
            if (item.Num > 0 && LookupItem(item.Num) is null)
                logger.LogDebug("UpdateUser: Item Drop Saved: {ItemId} ({CharId})", item.Num, user.CharId);
        }

        (byte[] itemsBlob, byte[] serialsBlob) = UserDataBlobCodec.BuildInventoryBlobs(user);

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC UPDATE_USER_DATA @id, @nation, @race, @class, @hair, @rank, @title, @level, @exp, @loyalty, " +
                "@face, @city, @knights, @fame, @hp, @mp, @sp, @str, @sta, @dex, @intel, @cha, @authority, @points, " +
                "@gold, @zone, @bind, @px, @pz, @py, @dwTime, @questCount, @skills, @items, @serials, @quests, " +
                "@mannerPoint, @loyaltyMonthly",
                connection);
            command.Parameters.AddWithValue("@id", user.CharId);
            command.Parameters.AddWithValue("@nation", user.Nation);
            command.Parameters.AddWithValue("@race", user.Race);
            command.Parameters.AddWithValue("@class", user.Class);
            command.Parameters.AddWithValue("@hair", user.HairColor);
            command.Parameters.AddWithValue("@rank", user.Rank);
            command.Parameters.AddWithValue("@title", user.Title);
            command.Parameters.AddWithValue("@level", user.Level);
            command.Parameters.AddWithValue("@exp", user.Exp);
            command.Parameters.AddWithValue("@loyalty", user.Loyalty);
            command.Parameters.AddWithValue("@face", user.Face);
            command.Parameters.AddWithValue("@city", user.City);
            command.Parameters.AddWithValue("@knights", user.Knights);
            command.Parameters.AddWithValue("@fame", user.Fame);
            command.Parameters.AddWithValue("@hp", user.Hp);
            command.Parameters.AddWithValue("@mp", user.Mp);
            command.Parameters.AddWithValue("@sp", user.Sp);
            command.Parameters.AddWithValue("@str", user.Str);
            command.Parameters.AddWithValue("@sta", user.Sta);
            command.Parameters.AddWithValue("@dex", user.Dex);
            command.Parameters.AddWithValue("@intel", user.Intel);
            command.Parameters.AddWithValue("@cha", user.Cha);
            command.Parameters.AddWithValue("@authority", user.Authority);
            command.Parameters.AddWithValue("@points", user.Points);
            command.Parameters.AddWithValue("@gold", user.Gold);
            command.Parameters.AddWithValue("@zone", user.Zone);
            command.Parameters.AddWithValue("@bind", user.Bind);
            command.Parameters.AddWithValue("@px", (int)(user.CurX * 100));
            command.Parameters.AddWithValue("@pz", (int)(user.CurZ * 100));
            command.Parameters.AddWithValue("@py", (int)(user.CurY * 100));
            command.Parameters.AddWithValue("@dwTime", (long)user.Time);
            command.Parameters.AddWithValue("@questCount", questTotal);
            command.Parameters.Add("@skills", SqlDbType.VarBinary, -1).Value = user.Skills;
            command.Parameters.Add("@items", SqlDbType.VarBinary, -1).Value = itemsBlob;
            command.Parameters.Add("@serials", SqlDbType.VarBinary, -1).Value = serialsBlob;
            command.Parameters.Add("@quests", SqlDbType.VarBinary, -1).Value = questsBlob;
            command.Parameters.AddWithValue("@mannerPoint", user.MannerPoint);
            command.Parameters.AddWithValue("@loyaltyMonthly", user.LoyaltyMonthly);

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                logger.LogError("UpdateUser: No rows affected for charId={CharId}", charId);
                return false;
            }
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "UpdateUser failed");
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- warehouse

    public async Task<bool> LoadWarehouseAsync(string accountId, int userId, CancellationToken cancellationToken = default)
    {
        UserData? user = Users.Get(userId);
        if (user is null || user.CharId.Length == 0)
        {
            logger.LogError("LoadWarehouse: called for inactive userId={UserId}", userId);
            return false;
        }

        byte[] itemsBlob = [], serialsBlob = [];
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT nMoney, WarehouseData, strSerial FROM WAREHOUSE WHERE strAccountID = @accountId", connection);
            command.Parameters.AddWithValue("@accountId", accountId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                logger.LogError("LoadWarehouse: No rows selected for accountId={AccountId}", accountId);
                return false;
            }

            user.Bank = Convert.ToInt32(reader.GetValue(0));
            if (!reader.IsDBNull(1))
                itemsBlob = ReadBlob(reader, 1);
            if (!reader.IsDBNull(2))
                serialsBlob = ReadBlob(reader, 2);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadWarehouse failed");
            return false;
        }

        UserDataBlobCodec.ApplyWarehouseBlobs(user, itemsBlob, serialsBlob, LookupItem,
            droppedItemId => logger.LogError("LoadWarehouse: item dropped itemId={ItemId} accountId={AccountId}", droppedItemId, accountId));

        return true;
    }

    public async Task<bool> UpdateWarehouseAsync(string accountId, int userId, UserUpdateType updateType, CancellationToken cancellationToken = default)
    {
        UserData? user = Users.Get(userId);
        if (user is null || accountId.Length == 0)
        {
            logger.LogError("UpdateWarehouse: called with inactive userId={UserId} accountId={AccountId}", userId, accountId);
            return false;
        }

        if (!string.Equals(user.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("UpdateWarehouse: accountId mismatch user.accountId={UserAccountId} accountId={AccountId}",
                user.AccountId, accountId);
            return false;
        }

        if (updateType is UserUpdateType.Logout or UserUpdateType.AllSave)
            user.Time = 0;

        (byte[] itemsBlob, byte[] serialsBlob) = UserDataBlobCodec.BuildWarehouseBlobs(user);

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC UPDATE_WAREHOUSE @accountId, @money, @dwTime, @items, @serials", connection);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@money", user.Bank);
            command.Parameters.AddWithValue("@dwTime", (long)user.Time);
            command.Parameters.Add("@items", SqlDbType.VarBinary, -1).Value = itemsBlob;
            command.Parameters.Add("@serials", SqlDbType.VarBinary, -1).Value = serialsBlob;

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                logger.LogError("UpdateWarehouse: No rows affected for accountId={AccountId}", accountId);
                return false;
            }
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "UpdateWarehouse failed");
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- session/misc

    public async Task<bool> SetLoginInfoAsync(
        string accountId, string charId, string serverIp, int serverId, string clientIp, byte init,
        CancellationToken cancellationToken = default)
    {
        string query;
        if (init == 0x01)
        {
            query = "INSERT INTO CURRENTUSER (strAccountID, strCharID, nServerNo, strServerIP, strClientIP) " +
                    "VALUES (@accountId, @charId, @serverId, @serverIp, @clientIp)";
        }
        else if (init == 0x02)
        {
            query = "UPDATE CURRENTUSER SET nServerNo = @serverId, strServerIP = @serverIp WHERE strAccountID = @accountId";
        }
        else
        {
            logger.LogError("SetLoginInfo: invalid init code {Init} for accountId={AccountId}", init, accountId);
            return false;
        }

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@serverId", serverId);
            command.Parameters.AddWithValue("@serverIp", serverIp);
            if (init == 0x01)
            {
                command.Parameters.AddWithValue("@charId", charId);
                command.Parameters.AddWithValue("@clientIp", clientIp);
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                logger.LogError("SetLoginInfo: No rows affected for accountId={AccountId}", accountId);
                return false;
            }
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "SetLoginInfo failed");
            return false;
        }

        return true;
    }

    public async Task<bool> AccountLogoutAsync(string accountId, int logoutCode = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC ACCOUNT_LOGOUT @accountId, @logoutCode, @ret1 OUTPUT, @ret2 OUTPUT", connection);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@logoutCode", logoutCode);
            var ret1 = command.Parameters.Add("@ret1", SqlDbType.SmallInt);
            ret1.Direction = ParameterDirection.Output;
            var ret2 = command.Parameters.Add("@ret2", SqlDbType.SmallInt);
            ret2.Direction = ParameterDirection.Output;

            await command.ExecuteNonQueryAsync(cancellationToken);

            if (ret1.Value is not short r1 || r1 != 1)
                logger.LogDebug("AccountLogout: ret1 not updated by proc for accountId={AccountId}", accountId);

            return true;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "AccountLogout failed");
            return false;
        }
    }

    public async Task<bool> CheckUserDataAsync(
        string accountId, string charId, int checkType, int userUpdateTime, int compareData,
        CancellationToken cancellationToken = default)
    {
        string query = checkType == 1
            ? "SELECT dwTime, nMoney FROM WAREHOUSE WHERE strAccountID = @key"
            : "SELECT dwTime, Exp FROM USERDATA WHERE strUserID = @key";

        uint dbTime, dbData;
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@key", checkType == 1 ? accountId : charId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                logger.LogError("CheckUserData: No rows for accountId={AccountId} charId={CharId}", accountId, charId);
                return false;
            }

            dbTime = Convert.ToUInt32(reader.GetValue(0));
            dbData = Convert.ToUInt32(reader.GetValue(1));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "CheckUserData failed");
            return false;
        }

        if ((int)dbTime != userUpdateTime || (int)dbData != compareData)
        {
            logger.LogError(
                "CheckUserData: data mismatch dbTime(expected: {ExpectedTime}, actual: {ActualTime}) dbData(expected: {ExpectedData}, actual: {ActualData})",
                userUpdateTime, dbTime, compareData, dbData);
            return false;
        }

        return true;
    }

    public async Task<bool> UpdateConcurrentUserCountAsync(int serverId, int zoneId, int userCount, CancellationToken cancellationToken = default)
    {
        if (zoneId is < 1 or > 3)
            return false;

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                $"UPDATE CONCURRENT SET zone{zoneId}_count = @userCount WHERE serverid = @serverId", connection);
            command.Parameters.AddWithValue("@userCount", userCount);
            command.Parameters.AddWithValue("@serverId", serverId);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "UpdateConcurrentUserCount failed");
            return false;
        }
    }

    // ------------------------------------------------------------------ knights

    public async Task<short> CreateKnightsAsync(int knightsId, int nation, string name, string chief, int flag = 1, CancellationToken cancellationToken = default)
    {
        short retCode;
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC CREATE_KNIGHTS @ret OUTPUT, @knightsId, @nation, @flag, @name, @chief", connection);
            var ret = command.Parameters.Add("@ret", SqlDbType.SmallInt);
            ret.Direction = ParameterDirection.Output;
            command.Parameters.AddWithValue("@knightsId", knightsId);
            command.Parameters.AddWithValue("@nation", nation);
            command.Parameters.AddWithValue("@flag", flag);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@chief", chief);

            await command.ExecuteNonQueryAsync(cancellationToken);
            retCode = ret.Value is short s ? s : (short)6;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "CreateKnights failed");
            retCode = 6;
        }

        if (retCode == 6)
        {
            logger.LogError("CreateKnights: database error (knightsId={KnightsId}, nation={Nation}, name={Name}, chief={Chief}, flag={Flag})",
                knightsId, nation, name, chief, flag);
        }

        return retCode;
    }

    public async Task<short> UpdateKnightsAsync(int type, string charId, int knightsId, int domination, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "EXEC UPDATE_KNIGHTS @ret OUTPUT, @type, @charId, @knightsId, @domination", connection);
            var ret = command.Parameters.Add("@ret", SqlDbType.SmallInt);
            ret.Direction = ParameterDirection.Output;
            command.Parameters.AddWithValue("@type", type);
            command.Parameters.AddWithValue("@charId", charId);
            command.Parameters.AddWithValue("@knightsId", knightsId);
            command.Parameters.AddWithValue("@domination", domination);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return ret.Value is short s ? s : (short)2;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "UpdateKnights failed");
            return 2;
        }
    }

    public async Task<short> DeleteKnightsAsync(int knightsId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("EXEC DELETE_KNIGHTS @ret OUTPUT, @knightsId", connection);
            var ret = command.Parameters.Add("@ret", SqlDbType.SmallInt);
            ret.Direction = ParameterDirection.Output;
            command.Parameters.AddWithValue("@knightsId", knightsId);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return ret.Value is short s ? s : (short)7;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "DeleteKnights failed");
            return 7;
        }
    }

    public async Task<KnightsInfo?> LoadKnightsInfoAsync(int knightsId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT IDNum, Nation, IDName, Members, Points, Ranking FROM KNIGHTS WHERE IDNum = @knightsId", connection);
            command.Parameters.AddWithValue("@knightsId", knightsId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                logger.LogError("LoadKnightsInfo: No rows selected for knightsId={KnightsId}", knightsId);
                return null;
            }

            string name = ReadTrimmedString(reader, 2);
            if (name.Length > Core.Protocol.ProtocolConstants.MaxIdSize)
            {
                logger.LogError("LoadKnightsInfo: knights name too long: {Name}", name);
                return null;
            }

            return new KnightsInfo(
                Convert.ToInt16(reader.GetValue(0)),
                Convert.ToByte(reader.GetValue(1)),
                name,
                Convert.ToInt16(reader.GetValue(3)),
                Convert.ToUInt32(reader.GetValue(4)),
                Convert.ToByte(reader.GetValue(5)));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadKnightsInfo failed");
            return null;
        }
    }

    public async Task<List<KnightsMember>> LoadKnightsMembersAsync(int knightsId, CancellationToken cancellationToken = default)
    {
        var members = new List<KnightsMember>();
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("EXEC LOAD_KNIGHTS_MEMBERS @knightsId", connection);
            command.Parameters.AddWithValue("@knightsId", knightsId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                members.Add(new KnightsMember(
                    ReadTrimmedString(reader, 0),
                    Convert.ToByte(reader.GetValue(1)),
                    Convert.ToByte(reader.GetValue(2)),
                    Convert.ToInt16(reader.GetValue(3))));
            }
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadKnightsMembers failed");
            return [];
        }

        if (members.Count == 0)
            logger.LogError("LoadKnightsMembers: No rows selected for knightsId={KnightsId}", knightsId);

        return members;
    }

    public async Task<List<KnightsRankingEntry>> LoadKnightsRankingAsync(int nation, CancellationToken cancellationToken = default)
    {
        string where = nation == 3 // battle zone: all nations
            ? "Points <> 0"
            : "Nation = @nation AND Points <> 0";

        var entries = new List<KnightsRankingEntry>();
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                $"SELECT IDNum, Points, Ranking FROM KNIGHTS WHERE {where} ORDER BY Points DESC", connection);
            if (nation != 3)
                command.Parameters.AddWithValue("@nation", nation);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(new KnightsRankingEntry(
                    Convert.ToInt16(reader.GetValue(0)),
                    Convert.ToUInt32(reader.GetValue(1)),
                    Convert.ToByte(reader.GetValue(2))));
            }
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadKnightsRanking failed");
            return [];
        }

        return entries;
    }

    public async Task<bool> UpdateBattleEventAsync(string charId, int nation, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "UPDATE BATTLE SET byNation = @nation, strUserName = @charId WHERE sIndex = 1", connection);
            command.Parameters.AddWithValue("@nation", nation);
            command.Parameters.AddWithValue("@charId", charId);

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                logger.LogError("UpdateBattleEvent: No rows affected");
                return false;
            }
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "UpdateBattleEvent failed");
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Reads a binary blob column (char columns come back as Latin1 bytes).</summary>
    private static byte[] ReadBlob(SqlDataReader reader, int ordinal)
    {
        object value = reader.GetValue(ordinal);
        return value switch
        {
            byte[] bytes => bytes,
            string text => System.Text.Encoding.Latin1.GetBytes(text),
            _ => [],
        };
    }

    /// <summary>DB_COMPAT_PADDED_NAMES: char(N) columns are right-padded; trim like rtrim().</summary>
    private static string ReadTrimmedString(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal).TrimEnd();
}
