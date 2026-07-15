using OpenKO.Client.Engine.Input;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Adapts the sampled <see cref="InputState"/> (CLocalInput edge machine) into the
/// UI layer's mouse model and drives the <see cref="UiManager"/>. Kept pure so the
/// mapping is headless-testable; the executable calls <see cref="Dispatch"/> once
/// per frame while any dialog is open.
/// </summary>
public static class UiInputBridge
{
    /// <summary>Map CLocalInput mouse flags to the UI mouse flags (named, layout-independent).</summary>
    public static UiMouse ToUiMouse(MouseFlags flags)
    {
        var ui = UiMouse.None;
        if ((flags & MouseFlags.LbClick) != 0) ui |= UiMouse.LbClick;
        if ((flags & MouseFlags.LbClicked) != 0) ui |= UiMouse.LbClicked;
        if ((flags & MouseFlags.LbDown) != 0) ui |= UiMouse.LbDown;
        if ((flags & MouseFlags.MbClick) != 0) ui |= UiMouse.MbClick;
        if ((flags & MouseFlags.MbClicked) != 0) ui |= UiMouse.MbClicked;
        if ((flags & MouseFlags.MbDown) != 0) ui |= UiMouse.MbDown;
        if ((flags & MouseFlags.RbClick) != 0) ui |= UiMouse.RbClick;
        if ((flags & MouseFlags.RbClicked) != 0) ui |= UiMouse.RbClicked;
        if ((flags & MouseFlags.RbDown) != 0) ui |= UiMouse.RbDown;
        if ((flags & MouseFlags.LbDoubleClick) != 0) ui |= UiMouse.LbDblClk;
        if ((flags & MouseFlags.MbDoubleClick) != 0) ui |= UiMouse.MbDblClk;
        if ((flags & MouseFlags.RbDoubleClick) != 0) ui |= UiMouse.RbDblClk;
        return ui;
    }

    public static UiPoint Cur(InputState input) => new(input.MousePos.X, input.MousePos.Y);

    public static UiPoint Old(InputState input) => new(input.MousePosOld.X, input.MousePosOld.Y);

    /// <summary>Route this frame's mouse (and wheel) through the manager.</summary>
    public static UiMouseProc Dispatch(UiManager manager, InputState input)
    {
        UiPoint cur = Cur(input);
        if (input.WheelDelta != 0)
            manager.MouseWheel(input.WheelDelta, cur);
        return manager.MouseProc(ToUiMouse(input.Mouse), cur, Old(input));
    }
}
