namespace OpenKO.N3;

/// <summary>
/// Port of CN3UIScrollBar (Client/N3Base/N3UIScrollBar.cpp) — a scrollbar container.
/// No additional serialized data beyond <see cref="N3UIBase"/>; the TrackBar and
/// Button children are resolved from <see cref="N3UIBase.Children"/> at render time.
/// </summary>
public class N3UIScrollBar : N3UIBase
{
    public N3UIScrollBar() { Type = UiType.ScrollBar; }
}
