using FontStashSharp;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Replaces the GDI CDFont path: a FontStashSharp font system over the
/// bundled Noto fonts (Noto Sans KR first — KO text is CP949 Korean; Noto
/// Sans as Latin fallback). Glyphs rasterize into dynamic atlases on demand;
/// font sizes map from the .uif dwFontHeight values.
/// </summary>
public sealed class FontService : IDisposable
{
    private readonly FontSystem _fontSystem = new();

    public FontService(IEnumerable<string> fontFiles)
    {
        foreach (string file in fontFiles)
        {
            if (File.Exists(file))
                _fontSystem.AddFont(File.ReadAllBytes(file));
        }
    }

    /// <summary>Locates the bundled fonts next to the executable.</summary>
    public static FontService FromBaseDirectory(string baseDirectory)
    {
        string dir = Path.Combine(baseDirectory, "Assets", "Fonts");
        return new FontService(
        [
            Path.Combine(dir, "NotoSansKR-Regular.ttf"),
            Path.Combine(dir, "NotoSans-Regular.ttf"),
        ]);
    }

    /// <summary>
    /// Font for a .uif dwFontHeight. The C++ CreateFont maps height via
    /// -MulDiv(h, LOGPIXELSY, 72) ≈ h·(96/72) px at standard DPI.
    /// </summary>
    public DynamicSpriteFont GetUiFont(uint fontHeight)
        => _fontSystem.GetFont(MathF.Max(fontHeight * 96f / 72f, 6f));

    public void Dispose() => _fontSystem.Dispose();
}
