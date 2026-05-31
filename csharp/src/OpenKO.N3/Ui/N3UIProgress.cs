namespace OpenKO.N3;

/// <summary>
/// Port of CN3UIProgress (Client/N3Base/N3UIProgress.cpp) — a fill-bar control.
/// No additional serialized data beyond <see cref="N3UIBase"/>; the background
/// and foreground image children are resolved from <see cref="N3UIBase.Children"/>
/// at render time via their reserved field (IMAGETYPE_BKGND / IMAGETYPE_FRGND).
/// </summary>
public class N3UIProgress : N3UIBase
{
    public N3UIProgress() { Type = UiType.Progress; }
}
