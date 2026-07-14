using System.ComponentModel.DataAnnotations;

namespace OpenKO.Servers.AIServer;

/// <summary>
/// Bound configuration for the AI server (the modern replacement for the legacy
/// <c>server.ini</c> <c>[SERVER] ZONE</c> key). The zone type selects the listen
/// port (Karus/Elmorad/Battle); the database comes from AddGameDatabase.
/// </summary>
public sealed class AiServerOptions
{
    public const string SectionName = "AiServer";

    /// <summary>Zone type: 0/1 = Karus (unify), 2 = El Morad, 3 = Battle.</summary>
    [Range(0, 3)]
    public int Zone { get; set; } = 1;
}
