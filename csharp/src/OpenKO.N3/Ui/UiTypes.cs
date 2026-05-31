namespace OpenKO.N3;

/// <summary>UI control types (port of <c>eUI_TYPE</c> in Client/N3Base/N3UIDef.h).</summary>
public enum UiType
{
    Base = 0,
    Button = 1,
    Static = 2,
    Progress = 3,
    Image = 4,
    ScrollBar = 5,
    String = 6,
    TrackBar = 7,
    Edit = 8,
    Area = 9,
    Tooltip = 10,
    Icon = 11,
    IconManager = 12,
    IconSlot = 13,
    List = 14,
}

/// <summary>UI area kinds (port of <c>eUI_AREA_TYPE</c> in N3UIArea.h).</summary>
public enum UiAreaType
{
    None = 0,
    Slot,
    Inv,
    TradeNpc,
    PerTradeMy,
    PerTradeOther,
    DropItem,
    SkillTree,
    SkillHotkey,
    RepairInv,
    RepairNpc,
    TradeMy,
    PerTradeInv,
}

/// <summary>
/// Float-based rectangle (port of <c>__FLOAT_RECT</c> in N3UIDef.h), used for UV coordinates.
/// Stored as four contiguous floats: left, top, right, bottom.
/// </summary>
public struct FloatRect
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;

    public FloatRect(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
}
