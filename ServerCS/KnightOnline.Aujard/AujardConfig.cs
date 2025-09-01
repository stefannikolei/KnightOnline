using KnightOnline.Shared.Database;
using System.Text.Json.Serialization;

namespace KnightOnline.Aujard;

/// <summary>
/// Configuration for the Aujard database server
/// </summary>
public class AujardConfig
{
    /// <summary>
    /// Port the database server will listen on
    /// </summary>
    public int Port { get; set; } = 15001;

    /// <summary>
    /// Maximum number of concurrent connections
    /// </summary>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>
    /// Database configuration
    /// </summary>
    public DatabaseConfig Database { get; set; } = new DatabaseConfig
    {
        Server = "localhost",
        Database = "KnightOnline",
        IntegratedSecurity = true,
        ConnectionTimeout = 30,
        CommandTimeout = 30
    };

    /// <summary>
    /// Auto-save interval in milliseconds (360 seconds = 6 minutes)
    /// </summary>
    public int AutoSaveInterval { get; set; } = 360000;

    /// <summary>
    /// Database process timeout in seconds
    /// </summary>
    public int DbProcessTimeout { get; set; } = 10;

    /// <summary>
    /// Enable detailed logging
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// Enable packet logging
    /// </summary>
    public bool EnablePacketLogging { get; set; } = false;

    /// <summary>
    /// Log file path (optional, uses console if not specified)
    /// </summary>
    public string? LogFilePath { get; set; }
}