namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// e_UIWND (Client/WarFare/N3UIWndBase.h) — which icon-owning window a dragged icon
/// belongs to.
/// </summary>
public enum UiWnd
{
    Inventory = 0,
    Transaction,
    DropItem,
    PerTrade,
    SkillTree,
    Hotkey,
    PerTradeEdit,
    WareHouse,
    Unknown,
}

/// <summary>
/// e_UIWND_DISTRICT (Client/WarFare/N3UIWndBase.h) — the sub-region of an icon window a
/// dragged icon sits in (a window may host several districts, e.g. inventory slot vs inv).
/// </summary>
public enum UiWndDistrict
{
    InventorySlot = 0,
    InventoryInv,
    TradeNpc,
    PerTradeMy,
    PerTradeOther,
    DropItem,
    SkillTree,
    SkillHotkey,
    TradeMy,
    PerTradeInv,
    Unknown,
}

/// <summary>__UIWndIconInfo — a fully-qualified icon slot address (window/district/order).</summary>
public struct UiWndIconInfo
{
    public UiWnd Wnd;
    public UiWndDistrict District;
    public int Order;
}

/// <summary>
/// __InfoSelectedIcon (N3UIWndBase.h) — the icon currently picked up by the cursor: its
/// slot address, its opaque item/skill payload, and the source control it was lifted from.
/// </summary>
public sealed class SelectedIconInfo
{
    /// <summary>UIWndSelect — where the icon was picked up from.</summary>
    public UiWndIconInfo Location;

    /// <summary>pItemSelect — the opaque item/skill data (the __IconItemSkill analog).</summary>
    public object? Item;

    /// <summary>The icon control being dragged (convenience; not in the C++ struct).</summary>
    public UiIconControl? Icon;

    public bool IsActive => Item != null || Icon != null;

    public void Clear()
    {
        Location = default;
        Item = null;
        Icon = null;
    }
}

/// <summary>
/// __RecoveryJobInfo (N3UIWndBase.h) — the pending move's source/target slot addresses and
/// payloads, kept so a server rejection can roll the icons back to their start positions.
/// </summary>
public sealed class RecoveryJobInfo
{
    public object? ItemSource;
    public UiWndIconInfo SourceStart;
    public UiWndIconInfo SourceEnd;
    public object? ItemTarget;
    public UiWndIconInfo TargetStart;
    public UiWndIconInfo TargetEnd;
    public int Page;

    public void Clear()
    {
        ItemSource = null;
        SourceStart = default;
        SourceEnd = default;
        ItemTarget = null;
        TargetStart = default;
        TargetEnd = default;
        Page = 0;
    }
}

/// <summary>
/// The C# analog of the <c>CN3UIWndBase</c> shared drag statics
/// (<c>s_sSelectedIconInfo</c>, <c>s_sRecoveryJobInfo</c>, <c>s_bWaitFromServer</c>). One
/// instance is owned by <see cref="UiManager"/> and shared by every icon window, preserving
/// the original global-singleton semantics without actual global state.
/// </summary>
public sealed class IconDragState
{
    /// <summary>s_sSelectedIconInfo — the icon currently held by the cursor.</summary>
    public SelectedIconInfo SelectedIcon { get; } = new();

    /// <summary>s_sRecoveryJobInfo — the in-flight move awaiting server confirmation.</summary>
    public RecoveryJobInfo RecoveryJob { get; } = new();

    /// <summary>
    /// s_bWaitFromServer — global input lock while a move is pending on the server. While set,
    /// <see cref="UiIconControl.MouseProc"/> freezes (returns immediately).
    /// </summary>
    public bool WaitFromServer { get; set; }

    public void Reset()
    {
        SelectedIcon.Clear();
        RecoveryJob.Clear();
        WaitFromServer = false;
    }
}
