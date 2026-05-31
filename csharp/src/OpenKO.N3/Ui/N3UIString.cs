using OpenKO.IO;

namespace OpenKO.N3;

/// <summary>
/// Port of CN3UIString (Client/N3Base/N3UIString.cpp) — a text-rendering control.
/// After the base data:
/// <code>
///   int32 fontNameLen; byte fontName[fontNameLen]
///   uint32 fontHeight
///   uint32 fontFlags      // D3DFONT_BOLD = 0x0001, D3DFONT_ITALIC = 0x0002
///   uint32 color          // D3DCOLOR (ARGB)
///   int32 textLen; byte text[textLen]
///   [format >= 1264] int32 reserved
/// </code>
/// </summary>
public class N3UIString : N3UIBase
{
    public N3UIString() { Type = UiType.String; }

    public string FontName { get; private set; } = string.Empty;
    public uint FontHeight { get; private set; }
    /// <summary>D3DFONT flags: bit 0 = bold, bit 1 = italic.</summary>
    public uint FontFlags { get; private set; }
    /// <summary>Text color as D3DCOLOR (ARGB).</summary>
    public uint Color { get; private set; }
    public string Text { get; private set; } = string.Empty;

    public bool IsBold => (FontFlags & 0x0001) != 0;
    public bool IsItalic => (FontFlags & 0x0002) != 0;

    public override void Release()
    {
        base.Release();
        FontName = string.Empty;
        FontHeight = 0;
        FontFlags = 0;
        Color = 0;
        Text = string.Empty;
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
            FontFlags = reader.ReadUInt32();
        }

        Color = reader.ReadUInt32();

        int textLen = reader.ReadInt32();
        if (textLen > 0)
            Text = reader.ReadFixedString(textLen);

        if ((uint)FileFormatVersion >= (uint)N3FormatVersion.V1264)
            reader.ReadInt32(); // m_iIdk0 — purpose unknown; consumed to keep stream in sync

        return true;
    }
}
