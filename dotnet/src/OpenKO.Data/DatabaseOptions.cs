using System.ComponentModel.DataAnnotations;

namespace OpenKO.Data;

/// <summary>
/// The component form of the game-database connection (the modern replacement
/// for the legacy <c>[ODBC]</c> INI keys). Used only when no
/// <c>ConnectionStrings:GameDb</c> is present — otherwise the standard
/// connection string wins. <see cref="Dsn"/> is the database name (the repo's
/// docker MSSQL uses <c>KN_online</c>); <see cref="Server"/> is the host, which
/// the original ODBC DSN carried out of band.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Database name (InitialCatalog) — the C++ <c>[ODBC] *_DSN</c> value.</summary>
    [Required]
    public string Dsn { get; set; } = "KN_online";

    /// <summary>SQL login user — the C++ <c>[ODBC] *_UID</c> value.</summary>
    [Required]
    public string Uid { get; set; } = "knight";

    /// <summary>SQL login password — the C++ <c>[ODBC] *_PWD</c> value.</summary>
    [Required]
    public string Pwd { get; set; } = "knight";

    /// <summary>
    /// SQL host. Empty falls back to the <c>OPENKO_DB_SERVER</c> environment
    /// variable, then <c>localhost</c> (matching the docker-compose setup).
    /// </summary>
    public string? Server { get; set; }
}
