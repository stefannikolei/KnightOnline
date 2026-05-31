using OpenKO.IO;

namespace OpenKO.N3;

/// <summary>
/// Port of CN3UIStatic (Client/N3Base/N3UIStatic.cpp) — a panel that references one
/// Image child as background and one String child as text output.
/// After the base data: one optional click-sound filename (int32 len + bytes).
/// </summary>
public class N3UIStatic : N3UIBase
{
    public N3UIStatic() { Type = UiType.Static; }

    public string ClickSound { get; private set; } = string.Empty;

    public override void Release()
    {
        base.Release();
        ClickSound = string.Empty;
    }

    public override bool Load(IFile file)
    {
        if (!base.Load(file))
            return false;

        var reader = (FileReader)file;
        int len = reader.ReadInt32();
        if (len > 0)
            ClickSound = reader.ReadFixedString(len);
        return true;
    }
}
