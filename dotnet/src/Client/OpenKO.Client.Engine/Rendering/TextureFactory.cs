using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;

namespace OpenKO.Client.Engine.Rendering;

/// <summary>Device layer: turns upload plans into GPU textures.</summary>
public static class TextureFactory
{
    public static Texture2D Create(GraphicsDevice device, TextureUploadPlan plan)
    {
        var texture = new Texture2D(device, plan.Width, plan.Height, plan.MipMap, plan.Format);
        for (int level = 0; level < plan.Levels.Count; level++)
            texture.SetData(level, null, plan.Levels[level], 0, plan.Levels[level].Length);
        return texture;
    }

    public static Texture2D FromN3Texture(GraphicsDevice device, N3Texture n3Texture)
        => Create(device, TextureUploadPlan.FromTexture(n3Texture));
}

/// <summary>
/// The engine analog of s_MngTex: resolves KO texture references through the
/// <see cref="KoPathResolver"/>, loads and uploads on first use, and shares
/// GPU textures by reference path. Not thread-safe (game loop only).
/// </summary>
public sealed class TextureCache(GraphicsDevice device, KoPathResolver resolver) : IDisposable
{
    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Texture for a KO reference ("item\...dxt"), or null when missing/broken.</summary>
    public Texture2D? Get(string koPath)
    {
        if (string.IsNullOrEmpty(koPath))
            return null;

        if (_cache.TryGetValue(koPath, out Texture2D? cached))
            return cached;

        Texture2D? texture = null;
        string? fullPath = resolver.Resolve(koPath);
        if (fullPath != null)
        {
            try
            {
                var n3 = new N3Texture();
                n3.LoadFromFile(fullPath);
                texture = TextureFactory.FromN3Texture(device, n3);
            }
            catch (Exception)
            {
                // Like s_MngTex: a broken file yields a null reference and the
                // caller renders untextured.
                texture = null;
            }
        }

        _cache[koPath] = texture;
        return texture;
    }

    public void Dispose()
    {
        foreach (Texture2D? texture in _cache.Values)
            texture?.Dispose();
        _cache.Clear();
    }
}
