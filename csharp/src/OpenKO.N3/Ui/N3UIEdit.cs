using OpenKO.IO;

namespace OpenKO.N3;

/// <summary>
/// Port of CN3UIEdit (Client/N3Base/N3UIEdit.cpp) — a single-line text-input control.
/// Inherits <see cref="N3UIStatic"/> (base data + click sound), then reads one
/// optional typing-sound filename (int32 len + bytes).
/// </summary>
public class N3UIEdit : N3UIStatic
{
    public N3UIEdit() { Type = UiType.Edit; }

    public string TypingSound { get; private set; } = string.Empty;

    public override void Release()
    {
        base.Release();
        TypingSound = string.Empty;
    }

    public override bool Load(IFile file)
    {
        if (!base.Load(file))
            return false;

        var reader = (FileReader)file;
        int len = reader.ReadInt32();
        if (len > 0)
            TypingSound = reader.ReadFixedString(len);
        return true;
    }
}
