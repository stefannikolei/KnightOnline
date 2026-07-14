using System.ComponentModel.DataAnnotations;

namespace OpenKO.Servers.VersionManager;

/// <summary>
/// Bound configuration for the version/login server (the modern replacement for
/// the legacy <c>Version.ini</c> sections). Validated on startup; the wire-byte
/// truncation and news-blob assembly happen in
/// <see cref="VersionManagerConfig.FromOptions"/>.
/// </summary>
public sealed class VersionManagerOptions
{
    public const string SectionName = "VersionManager";

    [Required]
    public DownloadOptions Download { get; set; } = new();

    /// <summary>At least one login-server entry must be configured.</summary>
    [MinLength(1)]
    public List<ServerListEntry> ServerList { get; set; } = [];

    public List<NewsEntry> News { get; set; } = [];
}

/// <summary>The patch-download endpoint (C++ <c>[DOWNLOAD]</c>).</summary>
public sealed class DownloadOptions
{
    [Required]
    public string Url { get; set; } = "127.0.0.1";

    [Required]
    public string Path { get; set; } = "/";
}

/// <summary>One server-list entry (C++ <c>[SERVER_LIST]</c> per-index keys).</summary>
public sealed class ServerListEntry
{
    public short Id { get; set; } = 1;

    [Required]
    public string Ip { get; set; } = "127.0.0.1";

    [Required]
    public string Name { get; set; } = "TEST|Server 1";

    public short UserLimit { get; set; } = VersionManagerConfig.MaxUser;
}

/// <summary>One news item (C++ <c>[NEWS]</c> TITLE/MESSAGE pair).</summary>
public sealed class NewsEntry
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
