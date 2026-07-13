using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// The engine analog of s_MngPMesh: shared N3PMesh data per KO reference
/// path (instances/renderers stay per object). Game-loop only.
/// </summary>
public sealed class PMeshCache(KoPathResolver resolver)
{
    private readonly Dictionary<string, N3PMesh?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public N3PMesh? Get(string koPath)
    {
        if (string.IsNullOrEmpty(koPath))
            return null;

        if (_cache.TryGetValue(koPath, out N3PMesh? cached))
            return cached;

        N3PMesh? mesh = null;
        string? fullPath = resolver.Resolve(koPath);
        if (fullPath != null)
        {
            try
            {
                mesh = new N3PMesh();
                mesh.LoadFromFile(fullPath);
            }
            catch (Exception)
            {
                mesh = null; // broken reference — part renders nothing
            }
        }

        _cache[koPath] = mesh;
        return mesh;
    }
}
