using Microsoft.Xna.Framework.Graphics;

namespace OpenKO.Client.Viewer;

/// <summary>Back-buffer → PNG dump for the --screenshot verification flow.</summary>
public static class Screenshot
{
    public static void SaveBackBuffer(GraphicsDevice device, string path)
    {
        int w = device.PresentationParameters.BackBufferWidth;
        int h = device.PresentationParameters.BackBufferHeight;

        var pixels = new Microsoft.Xna.Framework.Color[w * h];
        device.GetBackBufferData(pixels);

        using var texture = new Texture2D(device, w, h);
        texture.SetData(pixels);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        texture.SaveAsPng(stream, w, h);
    }
}
