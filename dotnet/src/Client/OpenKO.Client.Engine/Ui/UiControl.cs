using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime UI control — the interactive counterpart of a parsed <see cref="N3UiBase"/>
/// layout node (port of <c>CN3UIBase</c>, Client/N3Base/N3UIBase.cpp). Holds mutable
/// runtime state (state/visible/parent/geometry) and the mouse/message dispatch model,
/// while the immutable layout/data (texture, UV, text, fonts) stays on <see cref="Node"/>.
///
/// This layer is pure (no GraphicsDevice) so it is headless-testable; the device-side
/// <see cref="UiRenderer"/> turns a control tree into draw plans.
/// </summary>
public class UiControl
{
    public UiControl(N3UiBase node)
    {
        Node = node;
        Region = node.Region;
        Movable = node.Movable;
    }

    /// <summary>The parsed layout/data node backing this control.</summary>
    public N3UiBase Node { get; }

    public string Id => Node.Id;

    public uint Style => Node.Style;

    /// <summary>Screen-space region (mutable — MoveOffset/SetPos shift it).</summary>
    public N3UiRect Region { get; protected set; }

    /// <summary>Drag region (screen-space); empty = not draggable.</summary>
    public N3UiRect Movable { get; protected set; }

    public UiState State { get; set; } = UiState.CommonNone;

    public bool Visible { get; protected set; } = true;

    public UiControl? Parent { get; private set; }

    public List<UiControl> Children { get; } = [];

    /// <summary>
    /// Raised when a child posts a message to this control (port of the per-dialog
    /// <c>ReceiveMessage</c> override). Dialog controllers subscribe on the root.
    /// </summary>
    public event Action<UiControl, uint>? Message;

    public int Width => UiRectMath.Width(Region);

    public int Height => UiRectMath.Height(Region);

    public void AddChild(UiControl child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>CN3UIBase::IsIn — inclusive on all edges.</summary>
    public bool IsIn(int x, int y) => UiRectMath.IsIn(Region, x, y);

    /// <summary>
    /// ReceiveMessage — raise <see cref="Message"/>, then bubble to the parent
    /// (CN3UIBase::ReceiveMessage forwards to m_pParent), so a dialog controller can
    /// subscribe on the root and still see messages from deeply nested widgets.
    /// </summary>
    public virtual bool ReceiveMessage(UiControl sender, uint msg)
    {
        Message?.Invoke(sender, msg);
        Parent?.ReceiveMessage(sender, msg);
        return true;
    }

    /// <summary>
    /// CN3UIBase::MouseProc — walks children front-first (topmost), then applies the
    /// drag-move behaviour. Returns the UI_MOUSEPROC_* flags verbatim (quirks preserved:
    /// INREGION is OR'd even when the cursor left but was previously inside).
    /// </summary>
    public virtual UiMouseProc MouseProc(UiMouse flags, UiPoint cur, UiPoint old, UiTooltipControl? tooltip = null)
    {
        var ret = UiMouseProc.None;
        if (!Visible)
            return ret;

        // Dragging this control around.
        if (State == UiState.CommonMove)
        {
            if ((flags & UiMouse.LbClicked) != 0)
                State = UiState.CommonNone;
            else
                MoveOffset(cur.X - old.X, cur.Y - old.Y);
            return UiMouseProc.DoneSomething;
        }

        if (!IsIn(cur.X, cur.Y))
        {
            if (!IsIn(old.X, old.Y))
                return ret; // cursor never in region
            ret |= UiMouseProc.PrevInRegion;
        }
        else
        {
            tooltip?.SetText(Node.ToolTip);
        }

        ret |= UiMouseProc.InRegion;

        // Dispatch to children (front-first = topmost first).
        foreach (UiControl child in Children)
        {
            UiMouseProc childRet = child.MouseProc(flags, cur, old, tooltip);
            if ((childRet & UiMouseProc.DoneSomething) != 0)
            {
                ret |= UiMouseProc.ChildDoneSomething | UiMouseProc.DoneSomething;
                return ret;
            }
        }

        // Begin dragging if the press landed inside the movable rect.
        if (State != UiState.CommonMove
            && !UiRectMath.IsEmpty(Movable)
            && UiRectMath.PtInRect(Movable, cur.X, cur.Y)
            && (flags & UiMouse.LbClick) != 0)
        {
            State = UiState.CommonMove;
            ret |= UiMouseProc.DoneSomething;
        }

        return ret;
    }

    /// <summary>Per-frame tick (default recurses into children).</summary>
    public virtual void Tick()
    {
        foreach (UiControl child in Children)
            child.Tick();
    }

    /// <summary>CN3UIBase::OnKeyPress — return true if handled.</summary>
    public virtual bool OnKeyPress(int key) => false;

    /// <summary>CN3UIBase::OnKeyPressed — return true if handled.</summary>
    public virtual bool OnKeyPressed(int key) => false;

    /// <summary>CN3UIBase::OnMouseWheelEvent — return true if handled.</summary>
    public virtual bool OnMouseWheel(int delta) => false;

    /// <summary>CN3UIBase::MoveOffset — shift region, movable rect, and all children.</summary>
    public virtual bool MoveOffset(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return false;

        Region = UiRectMath.Offset(Region, dx, dy);
        if (!UiRectMath.IsEmpty(Movable))
            Movable = UiRectMath.Offset(Movable, dx, dy);

        foreach (UiControl child in Children)
            child.MoveOffset(dx, dy);

        return true;
    }

    /// <summary>CN3UIBase::SetPos — move so the region's top-left lands at (x,y).</summary>
    public void SetPos(int x, int y) => MoveOffset(x - Region.Left, y - Region.Top);

    /// <summary>CN3UIBase::SetPosCenter — centre within the given screen size.</summary>
    public void SetPosCenter(int screenWidth, int screenHeight)
        => SetPos((screenWidth - Width) / 2, (screenHeight - Height) / 2);

    /// <summary>CN3UIBase::SetVisible.</summary>
    public virtual void SetVisible(bool visible) => Visible = visible;

    /// <summary>CN3UIBase::GetChildByID — first control (this subtree) matching the id.</summary>
    public UiControl? GetChildById(string id)
    {
        foreach (UiControl child in Children)
        {
            if (child.Id == id)
                return child;
            UiControl? hit = child.GetChildById(id);
            if (hit != null)
                return hit;
        }

        return null;
    }

    /// <summary>Typed GetChildByID — first control matching id and type T.</summary>
    public T? GetChildById<T>(string id) where T : UiControl
    {
        foreach (UiControl child in Children)
        {
            if (child is T typed && child.Id == id)
                return typed;
            T? hit = child.GetChildById<T>(id);
            if (hit != null)
                return hit;
        }

        return null;
    }

    /// <summary>
    /// CN3UIWndBase::GetChildAreaByiOrder — the first <see cref="UiAreaControl"/> in this
    /// subtree whose area type and slot order both match. (The C++ scans direct children;
    /// descendants is a superset that resolves identically for the usual flat window layouts.)
    /// </summary>
    public UiAreaControl? GetChildAreaByOrder(UiAreaType type, int order)
    {
        foreach (UiControl d in Descendants())
        {
            if (d is UiAreaControl area && area.AreaType == type && area.Order == order)
                return area;
        }

        return null;
    }

    /// <summary>Enumerate this control and all descendants (depth-first, front order).</summary>
    public IEnumerable<UiControl> Descendants()
    {
        foreach (UiControl child in Children)
        {
            yield return child;
            foreach (UiControl d in child.Descendants())
                yield return d;
        }
    }
}
