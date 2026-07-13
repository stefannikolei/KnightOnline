namespace OpenKO.Client.Engine.Input;

/// <summary>CLocalInput mouse-flag bits (WarFare/LocalInput.h).</summary>
[Flags]
public enum MouseFlags : uint
{
    None = 0,
    LbClick = 0x1,      // left press edge (MOUSE_LBCLICK)
    LbClicked = 0x2,    // left release edge
    LbDown = 0x4,       // left held
    LbDoubleClick = 0x8,
    MbClick = 0x10,
    MbClicked = 0x20,
    MbDown = 0x40,
    MbDoubleClick = 0x80,
    RbClick = 0x100,
    RbClicked = 0x200,
    RbDown = 0x400,
    RbDoubleClick = 0x800,
}

/// <summary>One sampled device snapshot fed into <see cref="InputState.Tick"/>.</summary>
public readonly record struct InputSnapshot(
    int MouseX,
    int MouseY,
    bool LeftDown,
    bool MiddleDown,
    bool RightDown);

/// <summary>
/// Port of the <c>CLocalInput</c> edge machine as a pure class: the device
/// layer samples MonoGame keyboard/mouse each frame and feeds it in here;
/// game code keeps the C++ query semantics — IsKeyDown (held),
/// IsKeyPress (down edge), IsKeyPressed (up edge) — indexed by DIK scan code.
/// </summary>
public sealed class InputState
{
    public const int NumKeys = 256; // NUMDIKEYS

    /// <summary>
    /// Double-click window in seconds. The C++ uses Win32 GetDoubleClickTime
    /// (default 500 ms); fixed here — documented deviation.
    /// </summary>
    public const double DoubleClickSeconds = 0.5;

    private readonly bool[] _down = new bool[NumKeys];
    private readonly bool[] _pressEdge = new bool[NumKeys];
    private readonly bool[] _releaseEdge = new bool[NumKeys];
    private double _lastLbDownTime = double.NegativeInfinity;
    private double _lastMbDownTime = double.NegativeInfinity;
    private double _lastRbDownTime = double.NegativeInfinity;
    private InputSnapshot _prev;

    public MouseFlags Mouse { get; private set; }

    public MouseFlags MouseOld { get; private set; }

    public (int X, int Y) MousePos { get; private set; }

    public (int X, int Y) MousePosOld { get; private set; }

    public bool IsKeyDown(int dik) => (uint)dik < NumKeys && _down[dik];

    /// <summary>The frame the key went down (CLocalInput::IsKeyPress).</summary>
    public bool IsKeyPress(int dik) => (uint)dik < NumKeys && _pressEdge[dik];

    /// <summary>The frame the key was released (CLocalInput::IsKeyPressed).</summary>
    public bool IsKeyPressed(int dik) => (uint)dik < NumKeys && _releaseEdge[dik];

    public bool IsNoKeyDown()
    {
        foreach (bool down in _down)
        {
            if (down)
                return false;
        }

        return true;
    }

    /// <summary>
    /// One frame: <paramref name="keysDown"/> is the current held-state per
    /// DIK code; <paramref name="time"/> is the game clock in seconds
    /// (injected for testability, used for double-click detection).
    /// </summary>
    public void Tick(ReadOnlySpan<bool> keysDown, in InputSnapshot snapshot, double time)
    {
        for (int i = 0; i < NumKeys; i++)
        {
            bool now = i < keysDown.Length && keysDown[i];
            _pressEdge[i] = now && !_down[i];
            _releaseEdge[i] = !now && _down[i];
            _down[i] = now;
        }

        MouseOld = Mouse;
        MousePosOld = MousePos;
        MousePos = (snapshot.MouseX, snapshot.MouseY);

        MouseFlags flags = MouseFlags.None;
        flags |= Button(snapshot.LeftDown, _prev.LeftDown, ref _lastLbDownTime, time,
            MouseFlags.LbClick, MouseFlags.LbClicked, MouseFlags.LbDown, MouseFlags.LbDoubleClick);
        flags |= Button(snapshot.MiddleDown, _prev.MiddleDown, ref _lastMbDownTime, time,
            MouseFlags.MbClick, MouseFlags.MbClicked, MouseFlags.MbDown, MouseFlags.MbDoubleClick);
        flags |= Button(snapshot.RightDown, _prev.RightDown, ref _lastRbDownTime, time,
            MouseFlags.RbClick, MouseFlags.RbClicked, MouseFlags.RbDown, MouseFlags.RbDoubleClick);

        Mouse = flags;
        _prev = snapshot;
    }

    private static MouseFlags Button(
        bool now, bool before, ref double lastDownTime, double time,
        MouseFlags click, MouseFlags clicked, MouseFlags down, MouseFlags doubleClick)
    {
        MouseFlags flags = MouseFlags.None;
        if (now)
            flags |= down;

        if (now && !before)
        {
            flags |= click;
            if (time - lastDownTime <= DoubleClickSeconds)
                flags |= doubleClick;
            lastDownTime = time;
        }
        else if (!now && before)
        {
            flags |= clicked;
        }

        return flags;
    }
}
