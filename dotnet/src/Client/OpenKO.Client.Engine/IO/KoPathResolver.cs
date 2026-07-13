using System.Collections.Concurrent;

namespace OpenKO.Client.Engine.IO;

/// <summary>
/// Resolves the Windows-style asset references stored in the N3 files
/// ("item\mir_fork.dxt", arbitrary casing) against the on-disk corpus on a
/// case-sensitive file system: backslashes are normalized and every path
/// segment is matched case-insensitively (directory listings cached).
/// </summary>
public sealed class KoPathResolver(string rootPath)
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _listings = new();
    private readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    public string RootPath { get; } = rootPath;

    /// <summary>Absolute path for a KO asset reference, or null if missing.</summary>
    public string? Resolve(string koPath)
    {
        if (string.IsNullOrEmpty(koPath))
            return null;

        return _resolved.GetOrAdd(koPath, ResolveUncached);
    }

    private string? ResolveUncached(string koPath)
    {
        string[] segments = koPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = RootPath;
        foreach (string segment in segments)
        {
            Dictionary<string, string> listing = _listings.GetOrAdd(current, ListDirectory);
            if (!listing.TryGetValue(segment, out string? actual))
                return null;
            current = Path.Combine(current, actual);
        }

        return File.Exists(current) ? current : null;
    }

    private static Dictionary<string, string> ListDirectory(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(dir))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
            {
                string name = Path.GetFileName(entry);
                map[name] = name; // first wins on case-duplicates
            }
        }

        return map;
    }
}
