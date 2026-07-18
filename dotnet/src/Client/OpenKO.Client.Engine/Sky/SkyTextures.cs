using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.IO;

namespace OpenKO.Client.Engine.Sky;

/// <summary>
/// The four sky-body textures (CN3SkyMng::InitToDefaultHardCoding): the moon
/// phase strip plus the three sun parts. All are plain image files
/// (misc\sky\phases.tga, sundisk/sunglow/sunflare.bmp) rather than NTF .dxt
/// containers, so they load through <see cref="Texture2D.FromStream"/> (the
/// terrain BaseDetail path). Any field may be null when the asset is absent.
/// </summary>
public readonly record struct SkyBodyTextures(
    Texture2D? SunDisk,
    Texture2D? SunGlow,
    Texture2D? SunFlare,
    Texture2D? Moon)
{
    /// <summary>Load the four sky-body textures via the resolver; each is best-effort.</summary>
    public static SkyBodyTextures Load(GraphicsDevice device, KoPathResolver resolver)
        => new(
            LoadImage(device, resolver, @"misc\sky\sundisk.bmp"),
            LoadImage(device, resolver, @"misc\sky\sunglow.bmp"),
            LoadImage(device, resolver, @"misc\sky\sunflare.bmp"),
            LoadImage(device, resolver, @"misc\sky\phases.tga"));

    /// <summary>
    /// Resolve a KO image reference and upload it via <see cref="Texture2D.FromStream"/>.
    /// Returns null when the file is missing or cannot be decoded (null-safe sky).
    /// </summary>
    public static Texture2D? LoadImage(GraphicsDevice device, KoPathResolver resolver, string koPath)
    {
        string? full = resolver.Resolve(koPath);
        if (full == null || !File.Exists(full))
            return null;

        try
        {
            using FileStream stream = File.OpenRead(full);
            return Texture2D.FromStream(device, stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
