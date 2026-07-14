using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>eUI_STATE (N3UIDef.h) — the runtime widget states.</summary>
public enum UiState
{
    CommonNone = 0,
    CommonMove,
    ButtonNormal,
    ButtonDown,
    ButtonDown2CheckDown,
    ButtonDown2CheckUp,
    ButtonOn,
    ButtonDisable,
    ButtonClick,
    ScrollBarNull,
    ScrollBarTopButtonDown,
    ScrollBarBottomButtonDown,
    EditActive,
    EditUnactive,
    TrackBarThumbDrag,
    ListEnable,
    ListDisable,
    IconMoving,
    IconWaitFromServer,
    IconDoSuccess,
    IconDoFail,
    IconDoRecovery,
}

/// <summary>UIMSG_* (N3UIDef.h) — messages a child posts to its parent via ReceiveMessage.</summary>
public static class UiMsg
{
    public const uint ButtonClick = 0x00000001;
    public const uint TrackBarPos = 0x00000010;
    public const uint ScrollBarPos = 0x00000100;
    public const uint EditReturn = 0x00001000;
    public const uint EditTab = 0x00002000;
    public const uint EditEscape = 0x00004000;
    public const uint IconDownFirst = 0x00010000;
    public const uint IconDown = 0x00020000;
    public const uint IconUp = 0x00040000;
    public const uint IconDblClk = 0x00080000;
    public const uint ListSelChange = 0x00200000;
    public const uint ListDblClk = 0x00400000;
    public const uint IconRDownFirst = 0x01000000;
    public const uint IconRDown = 0x02000000;
    public const uint IconRUp = 0x04000000;
    public const uint IconRDblClk = 0x08000000;
    public const uint StringLClick = 0x10000000;
    public const uint StringLDClick = 0x20000000;
}

/// <summary>UI_MOUSE_* (N3UIDef.h) — must match LocalInput / InputState mouse flags.</summary>
[Flags]
public enum UiMouse : uint
{
    None = 0,
    LbClick = 0x00000001,
    LbClicked = 0x00000002,
    LbDown = 0x00000004,
    MbClick = 0x00000008,
    MbClicked = 0x00000010,
    MbDown = 0x00000020,
    RbClick = 0x00000040,
    RbClicked = 0x00000080,
    RbDown = 0x00000100,
    LbDblClk = 0x00000200,
    MbDblClk = 0x00000400,
    RbDblClk = 0x00000800,
}

/// <summary>UI_MOUSEPROC_* (N3UIDef.h) — MouseProc return flags.</summary>
[Flags]
public enum UiMouseProc : uint
{
    None = 0x00000000,
    DoneSomething = 0x00000001,
    ChildDoneSomething = 0x00000002,
    InRegion = 0x00000004,
    PrevInRegion = 0x00000008,
    DialogFocus = 0x00000010,
}

/// <summary>UISTYLE_* (N3UIDef.h) — style bits the runtime needs.</summary>
public static class UiStyle
{
    public const uint None = 0x00000000;
    public const uint AlwaysTop = 0x00000001;
    public const uint Modal = 0x00000002;
    public const uint FocusUnable = 0x00000004;
    public const uint ShowMeAlone = 0x00000008;
    public const uint HideUnable = 0x00000010;
    public const uint UserMoveHide = 0x00000020;
    public const uint PosLeft = 0x00000040;
    public const uint PosRight = 0x00000080;

    // button
    public const uint BtnNormal = 0x00010000;
    public const uint BtnCheck = 0x00020000;

    // image
    public const uint ImageAnimate = 0x00010000;

    // string
    public const uint StringSingleLine = 0x00100000;
    public const uint StringAlignLeft = 0x00200000;
    public const uint StringAlignRight = 0x00400000;
    public const uint StringAlignCenter = 0x00800000;
    public const uint StringAlignTop = 0x01000000;
    public const uint StringAlignBottom = 0x02000000;
    public const uint StringAlignVCenter = 0x04000000;

    // edit
    public const uint EditPassword = 0x10000000;
    public const uint EditNumberOnly = 0x20000000;

    // scrollbar
    public const uint ScrollBarHorizontal = 0x00010000;
    public const uint ScrollBarVertical = 0x00020000;
}

/// <summary>A screen point (Win32 POINT).</summary>
public readonly record struct UiPoint(int X, int Y);

/// <summary>Rect helpers matching the two conventions the C++ UI uses.</summary>
public static class UiRectMath
{
    /// <summary>CN3UIBase::IsIn — inclusive on all four edges.</summary>
    public static bool IsIn(in N3UiRect r, int x, int y)
        => x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;

    /// <summary>Win32 PtInRect — left/top inclusive, right/bottom exclusive (used for click/movable rects).</summary>
    public static bool PtInRect(in N3UiRect r, int x, int y)
        => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    public static bool IsEmpty(in N3UiRect r) => r.Right - r.Left == 0 || r.Bottom - r.Top == 0;

    public static N3UiRect Offset(in N3UiRect r, int dx, int dy)
        => new() { Left = r.Left + dx, Top = r.Top + dy, Right = r.Right + dx, Bottom = r.Bottom + dy };

    public static int Width(in N3UiRect r) => r.Right - r.Left;

    public static int Height(in N3UiRect r) => r.Bottom - r.Top;
}
