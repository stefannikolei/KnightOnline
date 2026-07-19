namespace OpenKO.Client;

/// <summary>
/// Runtime configuration for the clean game client: the login-server endpoint and
/// the asset corpus root. Bound from <c>appsettings.json</c> (section "Client")
/// plus <c>Client__*</c> environment variables. The base <see cref="KnightOnlineGame"/>
/// uses only these three values — no CLI, offline or debug knobs.
/// </summary>
public sealed class ClientConfig
{
    /// <summary>Login server host (default loopback).</summary>
    public string ServerHost { get; set; } = "127.0.0.1";

    /// <summary>Login server port (VersionManager, default 15100).</summary>
    public int ServerPort { get; set; } = 15100;

    /// <summary>Root of the client asset corpus (Client/Data). Auto-detected if null.</summary>
    public string? DataPath { get; set; }

    /// <summary>
    /// Walk up from the current directory to locate the <c>Client/Data</c> asset
    /// corpus. Used to fill <see cref="DataPath"/> when configuration leaves it unset.
    /// </summary>
    public static string? FindDataPath()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Client", "Data");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
