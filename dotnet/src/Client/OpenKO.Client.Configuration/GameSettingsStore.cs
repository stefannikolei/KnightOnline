using System.Text.Json;

namespace OpenKO.Client.Configuration;

/// <summary>
/// Loads/saves <see cref="GameSettings"/> as <c>options.json</c> next to the executable —
/// the C# counterpart of the C++ <c>Option.ini</c> that Option.exe writes and WarFare.exe
/// reads (both live in the install directory). The game and the settings tool agree on the
/// same file name in the same directory.
/// </summary>
public static class GameSettingsStore
{
    /// <summary>The settings file name (next to the executable).</summary>
    public const string FileName = "options.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The full path to the settings file in <paramref name="directory"/>.</summary>
    public static string PathIn(string directory) => Path.Combine(directory, FileName);

    /// <summary>
    /// Load <c>options.json</c> from <paramref name="directory"/> (defaults to the executable's
    /// base directory). A missing or unreadable file yields defaults; the result is always
    /// <see cref="GameSettings.Normalize">normalised</see>, mirroring WarFareMain's read+clamp.
    /// </summary>
    public static GameSettings Load(string? directory = null)
    {
        directory ??= AppContext.BaseDirectory;
        string path = PathIn(directory);

        GameSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), Json) ?? new GameSettings()
                : new GameSettings();
        }
        catch (Exception) // malformed json / IO → defaults, like a missing ini
        {
            settings = new GameSettings();
        }

        settings.Normalize();
        return settings;
    }

    /// <summary>
    /// Write <paramref name="settings"/> (normalised first) to <c>options.json</c> in
    /// <paramref name="directory"/> (defaults to the executable's base directory).
    /// </summary>
    public static void Save(GameSettings settings, string? directory = null)
    {
        directory ??= AppContext.BaseDirectory;
        settings.Normalize();
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathIn(directory), JsonSerializer.Serialize(settings, Json));
    }
}
