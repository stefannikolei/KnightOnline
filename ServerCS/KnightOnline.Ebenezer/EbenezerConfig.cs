using System.Text.Json.Serialization;

namespace KnightOnline.Ebenezer;

/// <summary>
/// Configuration for the Ebenezer game server
/// </summary>
public class EbenezerConfig
{
    /// <summary>
    /// Port the game server will listen on
    /// </summary>
    public int Port { get; set; } = 15000;

    /// <summary>
    /// UDP port for game communications
    /// </summary>
    public int UdpPort { get; set; } = 8888;

    /// <summary>
    /// Maximum number of concurrent users
    /// </summary>
    public int MaxUsers { get; set; } = 3000;

    /// <summary>
    /// Aujard database server connection
    /// </summary>
    public DatabaseServerConfig DatabaseServer { get; set; } = new DatabaseServerConfig
    {
        Host = "localhost",
        Port = 15001
    };

    /// <summary>
    /// AI Server configurations
    /// </summary>
    public AIServerConfig AIServers { get; set; } = new AIServerConfig();

    /// <summary>
    /// Server name displayed to clients
    /// </summary>
    public string ServerName { get; set; } = "Knight Online Server";

    /// <summary>
    /// Enable debug logging
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// Enable packet logging
    /// </summary>
    public bool EnablePacketLogging { get; set; } = false;

    /// <summary>
    /// Game time multiplier (1.0 = normal speed)
    /// </summary>
    public float GameTimeMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Weather update interval in seconds
    /// </summary>
    public int WeatherUpdateInterval { get; set; } = 300;

    /// <summary>
    /// Auto-save user data interval in seconds
    /// </summary>
    public int AutoSaveInterval { get; set; } = 300;
}

/// <summary>
/// Database server configuration
/// </summary>
public class DatabaseServerConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 15001;
    public int ReconnectInterval { get; set; } = 5000; // milliseconds
}

/// <summary>
/// AI Server configuration
/// </summary>
public class AIServerConfig
{
    public int KarusPort { get; set; } = 10020;
    public int ElmoradPort { get; set; } = 10030;
    public int BattlePort { get; set; } = 10040;
    public int MaxConnections { get; set; } = 10;
}