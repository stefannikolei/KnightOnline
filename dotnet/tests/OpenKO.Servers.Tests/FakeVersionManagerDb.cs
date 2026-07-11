using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Servers.VersionManager;

namespace OpenKO.Servers.Tests;

public sealed class FakeVersionManagerDb : IVersionManagerDb
{
    public List<VersionRow>? VersionList { get; set; } = [];

    public Dictionary<string, (string Password, byte Authority)> Accounts { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, CurrentUser> CurrentUsers { get; } = new(StringComparer.Ordinal);

    public List<ConcurrentRow>? UserCounts { get; set; } = [];

    public Dictionary<string, short> PremiumDays { get; } = new(StringComparer.Ordinal);

    public bool FailLogin { get; set; }

    public Task<List<VersionRow>?> LoadVersionListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(VersionList is null ? null : new List<VersionRow>(VersionList));

    public Task<AuthResult> AccountLoginAsync(string accountId, string password, CancellationToken cancellationToken = default)
    {
        if (FailLogin)
            return Task.FromResult(AuthResult.AUTH_FAILED);

        if (!Accounts.TryGetValue(accountId, out var account))
            return Task.FromResult(AuthResult.AUTH_NOT_FOUND);

        if (!string.Equals(account.Password, password, StringComparison.Ordinal))
            return Task.FromResult(AuthResult.AUTH_NOT_FOUND);

        if (account.Authority == 255)
            return Task.FromResult(AuthResult.AUTH_BANNED);

        return Task.FromResult(AuthResult.AUTH_OK);
    }

    public Task<CurrentUser?> GetCurrentUserAsync(string accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(CurrentUsers.TryGetValue(accountId, out var user) ? user : null);

    public Task<List<ConcurrentRow>?> LoadUserCountsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(UserCounts is null ? null : new List<ConcurrentRow>(UserCounts));

    public Task<short?> LoadPremiumServiceUserAsync(string accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(PremiumDays.TryGetValue(accountId, out short days) ? days : (short?)null);
}
