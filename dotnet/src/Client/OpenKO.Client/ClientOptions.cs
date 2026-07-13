namespace OpenKO.Client;

/// <summary>Parsed command-line options for the runnable client.</summary>
public sealed class ClientOptions
{
    /// <summary>Root of the client asset corpus (Client/Data). Auto-detected if null.</summary>
    public string? DataPath { get; set; }

    /// <summary>Login server host (enables the online flow).</summary>
    public string? ServerHost { get; set; }

    public int ServerPort { get; set; } = 15100;

    public string Account { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Offline zone name (e.g. "moradon") — renders the zone without a server.</summary>
    public string? OfflineZone { get; set; }

    /// <summary>Dump a screenshot after a few frames and exit.</summary>
    public string? ScreenshotPath { get; set; }

    public static ClientOptions Parse(string[] args)
    {
        var options = new ClientOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data" when i + 1 < args.Length:
                    options.DataPath = args[++i];
                    break;
                case "--server" when i + 1 < args.Length:
                    ParseServer(options, args[++i]);
                    break;
                case "--account" when i + 1 < args.Length:
                    options.Account = args[++i];
                    break;
                case "--password" when i + 1 < args.Length:
                    options.Password = args[++i];
                    break;
                case "--offline" when i + 1 < args.Length:
                    options.OfflineZone = args[++i];
                    break;
                case "--screenshot" when i + 1 < args.Length:
                    options.ScreenshotPath = args[++i];
                    break;
            }
        }

        options.DataPath ??= FindDataPath();
        return options;
    }

    private static void ParseServer(ClientOptions options, string value)
    {
        string[] parts = value.Split(':', 2);
        options.ServerHost = parts[0];
        if (parts.Length == 2 && int.TryParse(parts[1], out int port))
            options.ServerPort = port;
    }

    private static string? FindDataPath()
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
