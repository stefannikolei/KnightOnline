namespace OpenKO.N3;

/// <summary>
/// Port of CN3UITrackBar (Client/N3Base/N3UITrackBar.cpp) — the draggable thumb
/// inside a scrollbar. No additional serialized data beyond <see cref="N3UIBase"/>.
/// </summary>
public class N3UITrackBar : N3UIBase
{
    public N3UITrackBar() { Type = UiType.TrackBar; }
}
