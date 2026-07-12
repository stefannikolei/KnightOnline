using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Aujard;

namespace OpenKO.Servers.Tests;

/// <summary>Configurable IDbAgent stand-in for the Ebenezer pre-game flow tests.</summary>
internal sealed class FakeDbAgent : IDbAgent
{
    public Func<string, string, int> AccountLogin = (_, _) => -1;
    public List<(string Account, string Password)> LoginCalls = [];

    public Func<string, int, bool> NationSelect = (_, _) => true;
    public List<(string Account, int Nation)> NationSelectCalls = [];

    public NewCharResult CreateNewCharResult = NewCharResult.Success;
    public List<(string Account, int Index, string CharId, int Race, int Class)> CreateNewCharCalls = [];

    public AllCharIds? AllCharIds;
    public Dictionary<string, CharInfo> CharInfos = [];

    /// <summary>Fills Users[userId] on LoadUserDataAsync; null → load failure.</summary>
    public Action<UserData>? PopulateUserData;

    public List<(string Account, string CharId, byte Init)> SetLoginInfoCalls = [];
    public List<int> UpdateUserCalls = [];
    public List<string> AccountLogoutCalls = [];

    public UserDataStore Users { get; } = new();

    public Task<bool> InitAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<int> AccountLoginAsync(string accountId, string password, CancellationToken ct = default)
    {
        LoginCalls.Add((accountId, password));
        return Task.FromResult(AccountLogin(accountId, password));
    }

    public Task<bool> NationSelectAsync(string accountId, int nation, CancellationToken ct = default)
    {
        NationSelectCalls.Add((accountId, nation));
        return Task.FromResult(NationSelect(accountId, nation));
    }

    public Task<NewCharResult> CreateNewCharAsync(
        string accountId, int index, string charId, int race, int cls,
        int hair, int face, int str, int sta, int dex, int intel, int cha,
        CancellationToken ct = default)
    {
        CreateNewCharCalls.Add((accountId, index, charId, race, cls));
        return Task.FromResult(CreateNewCharResult);
    }

    public Task<AllCharIds?> GetAllCharIdsAsync(string accountId, CancellationToken ct = default)
        => Task.FromResult(AllCharIds);

    public Task<CharInfo?> LoadCharInfoAsync(string charId, CancellationToken ct = default)
        => Task.FromResult(CharInfos.GetValueOrDefault(charId));

    public Task<bool> LoadUserDataAsync(string accountId, string charId, int userId, CancellationToken ct = default)
    {
        if (PopulateUserData is null)
            return Task.FromResult(false);

        UserData? slot = Users.Get(userId);
        if (slot is null)
            return Task.FromResult(false);

        slot.AccountId = accountId;
        slot.CharId = charId;
        PopulateUserData(slot);
        return Task.FromResult(true);
    }

    public Task<bool> LoadWarehouseAsync(string accountId, int userId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> UpdateUserAsync(string charId, int userId, UserUpdateType t, CancellationToken ct = default)
    {
        UpdateUserCalls.Add(userId);
        return Task.FromResult(true);
    }

    public Task<bool> UpdateWarehouseAsync(string accountId, int userId, UserUpdateType t, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> SetLoginInfoAsync(
        string accountId, string charId, string serverIp, int serverId, string clientIp, byte init,
        CancellationToken ct = default)
    {
        SetLoginInfoCalls.Add((accountId, charId, init));
        return Task.FromResult(true);
    }

    public Task<bool> AccountLogoutAsync(string accountId, int logoutCode = 0, CancellationToken ct = default)
    {
        AccountLogoutCalls.Add(accountId);
        return Task.FromResult(true);
    }

    public Task<bool> CheckUserDataAsync(string a, string c, int t, int updateTime, int compare, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> UpdateConcurrentUserCountAsync(int s, int z, int u, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<short> CreateKnightsAsync(int k, int n, string na, string c, int f = 1, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<short> UpdateKnightsAsync(int t, string c, int k, int d, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<short> DeleteKnightsAsync(int k, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<KnightsInfo?> LoadKnightsInfoAsync(int k, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<KnightsMember>> LoadKnightsMembersAsync(int k, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<KnightsRankingEntry>> LoadKnightsRankingAsync(int n, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> UpdateBattleEventAsync(string c, int n, CancellationToken ct = default) => throw new NotImplementedException();
}
