using Avalonia;

namespace OpenKO.Client.Settings;

/// <summary>
/// Entry point for the settings tool. Accepts an optional <c>--path &lt;dir&gt;</c> giving
/// the directory that holds (and should receive) <c>options.json</c> — the game passes
/// its own base directory here so the file lands where the game reads it. Without it the
/// tool uses its own base directory (the shipped layout puts both exes side by side).
/// </summary>
internal static class Program
{
    /// <summary>The directory holding options.json (from <c>--path</c>, or the exe's own dir).</summary>
    public static string SettingsDirectory { get; private set; } = AppContext.BaseDirectory;

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [STAThread]
    public static int Main(string[] args)
    {
        SettingsDirectory = ParsePath(args) ?? AppContext.BaseDirectory;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static string? ParsePath(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--path" or "-p")
                return args[i + 1];
        }

        return null;
    }
}
