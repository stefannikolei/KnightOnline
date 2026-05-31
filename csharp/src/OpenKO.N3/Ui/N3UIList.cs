using OpenKO.IO;

namespace OpenKO.N3;

/// <summary>
/// Port of CN3UIList (Client/N3Base/N3UIList.cpp) — a scrollable list control.
/// After the base data:
/// <code>
///   int32 fontNameLen; byte fontName[fontNameLen]
///   uint32 fontHeight
///   uint32 fontColor    // D3DCOLOR (ARGB)
///   uint32 fontBold     // BOOL (non-zero = bold)
///   uint32 fontItalic   // BOOL (non-zero = italic)
/// </code>
/// The ScrollBar child is referenced from <see cref="N3UIBase.Children"/> at render time.
/// </summary>
public class N3UIList : N3UIBase
{
    public N3UIList() { Type = UiType.List; }

    public string FontName { get; private set; } = string.Empty;
    public uint FontHeight { get; private set; }
    /// <summary>Row text color as D3DCOLOR (ARGB).</summary>
    public uint FontColor { get; private set; }
    public bool FontBold { get; private set; }
    public bool FontItalic { get; private set; }

    public override void Release()
    {
        base.Release();
        FontName = string.Empty;
        FontHeight = 0;
        FontColor = 0;
        FontBold = false;
        FontItalic = false;
    }

    public override bool Load(IFile file)
    {
        if (!base.Load(file))
            return false;

        var reader = (FileReader)file;

        int fontNameLen = reader.ReadInt32();
        if (fontNameLen > 0)
        {
            FontName = reader.ReadFixedString(fontNameLen);
            FontHeight = reader.ReadUInt32();
            FontColor = reader.ReadUInt32();
            FontBold = reader.ReadUInt32() != 0;
            FontItalic = reader.ReadUInt32() != 0;
        }

        return true;
    }
}
