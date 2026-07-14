using System.ComponentModel.DataAnnotations;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Bound configuration for the Ebenezer game server (the modern replacement for
/// the legacy <c>server.ini</c> <c>[ZONE_INFO]</c>/<c>[AI_SERVER]</c> keys). The
/// listen port is <c>15000 + ServerNo</c>; the database comes from AddGameDatabase.
/// </summary>
public sealed class EbenezerOptions
{
    public const string SectionName = "Ebenezer";

    /// <summary>This zone's server number (C++ <c>MY_INFO</c>); listen port = 15000 + n.</summary>
    [Range(1, 255)]
    public int ServerNo { get; set; } = 1;

    [Required]
    public string AiServerIp { get; set; } = "127.0.0.1";

    /// <summary>The peer game servers (C++ <c>SERVER_XX</c>/<c>SERVER_IP_XX</c>).</summary>
    public List<ZoneServerEntry> Servers { get; set; } = [];
}

/// <summary>One peer game-server entry (port = 15000 + <see cref="No"/>).</summary>
public sealed class ZoneServerEntry
{
    public short No { get; set; } = 1;

    [Required]
    public string Ip { get; set; } = "127.0.0.1";
}
