using OpenKO.IO;

namespace OpenKO.N3;

/// <summary>
/// Port of the C++ <c>CN3UIArea</c> (Client/N3Base/N3UIArea.cpp) — an invisible interactive region
/// (inventory slots, trade areas, …). After the base data it stores a single int32 area type.
/// </summary>
public class N3UIArea : N3UIBase
{
    public N3UIArea()
    {
        Type = UiType.Area;
    }

    public UiAreaType AreaType { get; private set; } = UiAreaType.None;

    public override void Release()
    {
        base.Release();
        AreaType = UiAreaType.None;
    }

    public override bool Load(IFile file)
    {
        if (!base.Load(file))
            return false;

        var reader = (FileReader)file;
        AreaType = (UiAreaType)reader.ReadInt32();
        return true;
    }
}
