using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenKO.Client;

/// <summary>
/// Launches the standalone settings tool (OpenKO.Client.Settings) — the C# counterpart
/// of WarFare.exe's <c>ShellExecute("Option.exe")</c> from the in-game exit menu
/// (UIExitMenu.cpp). The tool is passed <c>--path &lt;game base dir&gt;</c> so its
/// <c>options.json</c> lands exactly where the game reads it; the settings take effect at
/// the next game start (a resolution change needs a restart, as in the original).
/// </summary>
public static class SettingsLauncher
{
    private const string ToolName = "OpenKO.Client.Settings";

    /// <summary>
    /// Starts the settings tool, writing its <c>options.json</c> into
    /// <paramref name="settingsDirectory"/> (the game's base directory). Returns false and
    /// never throws if the tool cannot be found or started (the caller logs the outcome).
    /// </summary>
    public static bool Launch(string settingsDirectory)
    {
        try
        {
            ProcessStartInfo? psi = BuildStartInfo(settingsDirectory);
            if (psi == null)
                return false;

            return Process.Start(psi) != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves how to start the tool: the native apphost beside the game exe when present
    /// (shipped layout), an <c>OPENKO_SETTINGS_EXE</c> override, else <c>dotnet &lt;dll&gt;</c>
    /// (found beside the game, via the override, or a dev sibling bin). Null when nothing matches.
    /// </summary>
    private static ProcessStartInfo? BuildStartInfo(string settingsDirectory)
    {
        string[] pathArgs = ["--path", settingsDirectory];
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string exeName = windows ? ToolName + ".exe" : ToolName;

        // 1) An explicit override (native exe or a .dll) wins.
        string? overridePath = Environment.GetEnvironmentVariable("OPENKO_SETTINGS_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return StartFor(overridePath, pathArgs);

        // 2) The native apphost sitting next to the game exe (the shipped side-by-side layout).
        string baseDir = AppContext.BaseDirectory;
        string nativeBeside = Path.Combine(baseDir, exeName);
        if (File.Exists(nativeBeside))
            return WithArgs(new ProcessStartInfo(nativeBeside) { UseShellExecute = false }, pathArgs);

        // 3) The managed dll beside the game, or a dev-time sibling bin, run via `dotnet`.
        string? dll = FindToolDll(baseDir);
        if (dll != null)
            return StartFor(dll, pathArgs);

        return null;
    }

    /// <summary>Starts a native exe directly or a managed dll via <c>dotnet</c>.</summary>
    private static ProcessStartInfo StartFor(string path, string[] extraArgs)
    {
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            foreach (string a in extraArgs)
                psi.ArgumentList.Add(a);
            return psi;
        }

        return WithArgs(new ProcessStartInfo(path) { UseShellExecute = false }, extraArgs);
    }

    private static ProcessStartInfo WithArgs(ProcessStartInfo psi, string[] extraArgs)
    {
        foreach (string a in extraArgs)
            psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>
    /// Looks for <c>OpenKO.Client.Settings.dll</c> beside the game, then in a dev-time sibling
    /// build output (…/OpenKO.Client.Settings/bin/&lt;config&gt;/&lt;tfm&gt;/) discovered by walking up
    /// from the game's base directory. Best-effort; null when not found.
    /// </summary>
    private static string? FindToolDll(string baseDir)
    {
        string beside = Path.Combine(baseDir, ToolName + ".dll");
        if (File.Exists(beside))
            return beside;

        // Dev layout: …/OpenKO.Client(.Dev)/bin/<config>/<tfm>/ → find the sibling tool bin.
        for (var dir = new DirectoryInfo(baseDir); dir?.Parent?.Parent?.Parent != null; dir = dir.Parent)
        {
            // Recognise a …/bin/<config>/<tfm> tail and pivot to the sibling project.
            if (!string.Equals(dir.Parent?.Parent?.Name, "bin", StringComparison.OrdinalIgnoreCase))
                continue;

            string config = dir.Parent!.Name;   // Debug / Release
            string tfm = dir.Name;               // net10.0
            DirectoryInfo? clientProj = dir.Parent.Parent.Parent; // …/OpenKO.Client(.Dev)
            DirectoryInfo? clientsRoot = clientProj?.Parent;      // …/Client
            if (clientsRoot == null)
                continue;

            string candidate = Path.Combine(
                clientsRoot.FullName, ToolName, "bin", config, tfm, ToolName + ".dll");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
