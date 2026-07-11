using OpenKO.Data.Models;

namespace OpenKO.Servers.VersionManager;

/// <summary>
/// Shared server state: version list + last version (swapped atomically like
/// <c>VersionList.Swap</c> in the C++), server list, FTP info and news blob.
/// </summary>
public sealed class VersionManagerState
{
    private volatile List<VersionRow> _versionList = [];
    private volatile int _lastVersion;

    public required IReadOnlyList<ServerInfo> Servers { get; init; }

    public required byte[] FtpUrl { get; init; }

    public required byte[] FtpPath { get; init; }

    public required byte[] News { get; init; }

    public IReadOnlyList<VersionRow> VersionList => _versionList;

    public short LastVersion => (short)_lastVersion;

    /// <summary>Port of VersionManagerApp::LoadVersionList's swap + max computation.</summary>
    public void SwapVersionList(List<VersionRow> versionList)
    {
        int lastVersion = 0;
        foreach (VersionRow row in versionList)
        {
            if (lastVersion < row.Number)
                lastVersion = row.Number;
        }

        _versionList = versionList;
        _lastVersion = lastVersion;
    }

    public static VersionManagerState FromConfig(VersionManagerConfig config) => new()
    {
        Servers = config.Servers,
        FtpUrl = config.FtpUrl,
        FtpPath = config.FtpPath,
        News = config.News,
    };
}
