using OpenKO.Data;
using OpenKO.Data.Models;

namespace OpenKO.Servers.Aujard;

/// <summary>
/// Async surface of <c>CDBAgent</c> (Server/Aujard/DBAgent.h). In the modernized
/// topology Ebenezer calls this library directly instead of sending packets over
/// the KNIGHT_SEND/KNIGHT_RECV shared-memory queues; the business behavior of
/// each method maps 1:1 to the C++ implementation.
/// </summary>
public interface IDbAgent
{
    UserDataStore Users { get; }

    /// <summary>InitDatabase + LoadItemTable.</summary>
    Task<bool> InitAsync(CancellationToken cancellationToken = default);

    /// <summary>AccountLogInReq: -1 failure, 0 unselected nation, 1 karus, 2 elmorad.</summary>
    Task<int> AccountLoginAsync(string accountId, string password, CancellationToken cancellationToken = default);

    /// <summary>NationSelect: ensures ACCOUNT_CHAR/WAREHOUSE records exist.</summary>
    Task<bool> NationSelectAsync(string accountId, int nation, CancellationToken cancellationToken = default);

    /// <summary>CreateNewChar; returns a NEW_CHAR_* code.</summary>
    Task<NewCharResult> CreateNewCharAsync(
        string accountId, int index, string charId, int race, int cls,
        int hair, int face, int str, int sta, int dex, int intel, int cha,
        CancellationToken cancellationToken = default);

    /// <summary>GetAllCharID: the three character slots; null on error.</summary>
    Task<AllCharIds?> GetAllCharIdsAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>LoadCharInfo: character summary + visible equipment; null on DB error.</summary>
    Task<CharInfo?> LoadCharInfoAsync(string charId, CancellationToken cancellationToken = default);

    /// <summary>LoadUserData into Users[userId].</summary>
    Task<bool> LoadUserDataAsync(string accountId, string charId, int userId, CancellationToken cancellationToken = default);

    /// <summary>UpdateUser from Users[userId].</summary>
    Task<bool> UpdateUserAsync(string charId, int userId, UserUpdateType updateType, CancellationToken cancellationToken = default);

    /// <summary>LoadWarehouseData into Users[userId].</summary>
    Task<bool> LoadWarehouseAsync(string accountId, int userId, CancellationToken cancellationToken = default);

    /// <summary>UpdateWarehouseData from Users[userId].</summary>
    Task<bool> UpdateWarehouseAsync(string accountId, int userId, UserUpdateType updateType, CancellationToken cancellationToken = default);

    /// <summary>SetLogInInfo: init 0x01 inserts, 0x02 updates CURRENTUSER.</summary>
    Task<bool> SetLoginInfoAsync(
        string accountId, string charId, string serverIp, int serverId, string clientIp, byte init,
        CancellationToken cancellationToken = default);

    /// <summary>AccountLogout: removes the CURRENTUSER record.</summary>
    Task<bool> AccountLogoutAsync(string accountId, int logoutCode = 0, CancellationToken cancellationToken = default);

    /// <summary>CheckUserData: verifies USERDATA (checkType 0) or WAREHOUSE (1) sync.</summary>
    Task<bool> CheckUserDataAsync(
        string accountId, string charId, int checkType, int userUpdateTime, int compareData,
        CancellationToken cancellationToken = default);

    /// <summary>UpdateConCurrentUserCount for zone 1-3.</summary>
    Task<bool> UpdateConcurrentUserCountAsync(int serverId, int zoneId, int userCount, CancellationToken cancellationToken = default);

    /// <summary>CreateKnights: 0 success, 3 name in use, 6 db error.</summary>
    Task<short> CreateKnightsAsync(int knightsId, int nation, string name, string chief, int flag = 1, CancellationToken cancellationToken = default);

    /// <summary>UpdateKnights: 0 success, 2 charId not found/db error, 7 not found, 8 capacity.</summary>
    Task<short> UpdateKnightsAsync(int type, string charId, int knightsId, int domination, CancellationToken cancellationToken = default);

    /// <summary>DeleteKnights: 0 success, 7 not found.</summary>
    Task<short> DeleteKnightsAsync(int knightsId, CancellationToken cancellationToken = default);

    /// <summary>LoadKnightsInfo: clan metadata; null when missing or on error.</summary>
    Task<KnightsInfo?> LoadKnightsInfoAsync(int knightsId, CancellationToken cancellationToken = default);

    /// <summary>LoadKnightsAllMembers rows (caller adds online flags / wire format).</summary>
    Task<List<KnightsMember>> LoadKnightsMembersAsync(int knightsId, CancellationToken cancellationToken = default);

    /// <summary>
    /// LoadKnightsAllList: ranking entries ordered by points (nation 3 = all nations).
    /// The C++ streamed these in 40-entry batches over the SHM queue; batching is
    /// now the caller's concern.
    /// </summary>
    Task<List<KnightsRankingEntry>> LoadKnightsRankingAsync(int nation, CancellationToken cancellationToken = default);

    /// <summary>UpdateBattleEvent: war winner + commander killer.</summary>
    Task<bool> UpdateBattleEventAsync(string charId, int nation, CancellationToken cancellationToken = default);
}
