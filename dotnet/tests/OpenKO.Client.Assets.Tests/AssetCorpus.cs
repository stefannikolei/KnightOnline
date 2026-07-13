namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Locates the real 1298 asset corpus (Client/Data — the ko-client-assets
/// submodule). Corpus tests are skipped when the submodule is not checked out.
/// </summary>
public static class AssetCorpus
{
    public static string? Root { get; } = Find();

    /// <summary>
    /// Case-insensitive recursive enumeration — the corpus mixes upper- and
    /// lower-case extensions (.dxt/.DXT etc.) and Linux globbing is
    /// case-sensitive by default.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string pattern)
        => Directory.EnumerateFiles(Root!, pattern, new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
        });

    private static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Client", "Data");
            // An empty directory means the submodule exists but isn't checked out.
            if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
                return candidate;
        }

        return null;
    }
}
