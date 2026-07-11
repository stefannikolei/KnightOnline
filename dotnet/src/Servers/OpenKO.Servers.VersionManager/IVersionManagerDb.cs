using OpenKO.Core.Protocol;
using OpenKO.Data.Models;

namespace OpenKO.Servers.VersionManager;

/// <summary>
/// Port of the CDBProcess surface the login flow needs
/// (Server/VersionManager/DBProcess.cpp). Abstracted for tests.
/// </summary>
public interface IVersionManagerDb
{
    /// <summary>LoadVersionList: full VERSION table; null on DB error, empty list is a startup error.</summary>
    Task<List<VersionRow>?> LoadVersionListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// AccountLogin: app-side ordinal password compare; wrong password deliberately
    /// returns AUTH_NOT_FOUND, Authority==255 returns AUTH_BANNED, DB errors AUTH_FAILED.
    /// </summary>
    Task<AuthResult> AccountLoginAsync(string accountId, string password, CancellationToken cancellationToken = default);

    /// <summary>IsCurrentUser: CURRENTUSER lookup; null when not in game (or DB error).</summary>
    Task<CurrentUser?> GetCurrentUserAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>LoadUserCountList: CONCURRENT table; null on DB error (counts then stay stale).</summary>
    Task<List<ConcurrentRow>?> LoadUserCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>LoadPremiumServiceUser: remaining premium days, or null on DB error (caller sends -1).</summary>
    Task<short?> LoadPremiumServiceUserAsync(string accountId, CancellationToken cancellationToken = default);
}
