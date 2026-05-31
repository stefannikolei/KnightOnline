using OpenKO.IO;
using OpenKO.Numerics;

namespace OpenKO.N3;

/// <summary>
/// Port of CN3UIButton (Client/N3Base/N3UIButton.cpp) — an interactive button.
/// After the base data: RECT m_rcClick (4 x int32) followed by two optional
/// sound filenames — hover-over ("on") and click — each prefixed by int32 length.
/// The image-state references (normal/hover/pressed/disabled) are bound from
/// <see cref="N3UIBase.Children"/> at render time via each child's Reserved value.
/// </summary>
public class N3UIButton : N3UIBase
{
    public N3UIButton() { Type = UiType.Button; }

    /// <summary>Click-sensitive hit-test region (port of m_rcClick).</summary>
    public Rect ClickRegion { get; private set; }

    /// <summary>Sound played when the cursor enters the button (port of m_pSnd_On).</summary>
    public string HoverSound { get; private set; } = string.Empty;

    /// <summary>Sound played on click (port of m_pSnd_Click).</summary>
    public string ClickSound { get; private set; } = string.Empty;

    public override void Release()
    {
        base.Release();
        ClickRegion = default;
        HoverSound = string.Empty;
        ClickSound = string.Empty;
    }

    public override bool Load(IFile file)
    {
        if (!base.Load(file))
            return false;

        var reader = (FileReader)file;

        ClickRegion = new Rect(
            reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32());

        HoverSound = ReadOptionalString(reader);
        ClickSound = ReadOptionalString(reader);
        return true;
    }

    private static string ReadOptionalString(FileReader r)
    {
        int len = r.ReadInt32();
        return len > 0 ? r.ReadFixedString(len) : string.Empty;
    }
}
