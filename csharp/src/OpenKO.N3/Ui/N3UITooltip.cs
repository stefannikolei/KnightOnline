namespace OpenKO.N3;

/// <summary>
/// Port of CN3UITooltip (Client/N3Base/N3UITooltip.cpp) — pop-up tooltip overlay.
/// Inherits <see cref="N3UIStatic"/> and does not override <c>Load</c>, so the
/// on-disk format is identical to <see cref="N3UIStatic"/>.
/// </summary>
public class N3UITooltip : N3UIStatic
{
    public N3UITooltip() { Type = UiType.Tooltip; }
}
