using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.IO;

/// <summary>
/// Generic engine-side analog of the C++ CN3Mng managers: one shared,
/// immutably-used asset instance per KO reference path. Note the deviation
/// for skeletons: the C++ shares CN3Joint trees too and re-ticks them per
/// character each frame; the engine loads joints per character instead
/// (see ChrRenderer) to avoid mutable shared state.
/// </summary>
public sealed class AssetCache<T>(KoPathResolver resolver) where T : N3BaseFile, new()
{
    private readonly Dictionary<string, T?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public T? Get(string koPath)
    {
        if (string.IsNullOrEmpty(koPath))
            return null;

        if (_cache.TryGetValue(koPath, out T? cached))
            return cached;

        T? asset = Load(koPath);
        _cache[koPath] = asset;
        return asset;
    }

    /// <summary>Loads WITHOUT caching (for mutable assets like joint trees).</summary>
    public T? Load(string koPath)
    {
        string? fullPath = resolver.Resolve(koPath);
        if (fullPath == null)
            return null;

        try
        {
            var asset = new T();
            asset.LoadFromFile(fullPath);
            return asset;
        }
        catch (Exception)
        {
            return null; // broken reference, caller degrades like the C++
        }
    }
}
