using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime text list (port of the row model in <c>CN3UIList</c>). Holds the dynamic
/// string rows, current selection and scroll offset, and hit-tests a clicked row to
/// post <see cref="UiMsg.ListSelChange"/> / <see cref="UiMsg.ListDblClk"/> to its
/// parent — matching <c>CN3UIList::MouseProc</c>. Row height comes from the list font.
/// </summary>
public sealed class UiListControl : UiControl
{
    private readonly int _rowHeight;

    public UiListControl(N3UiList node) : base(node)
    {
        State = UiState.ListEnable;
        _rowHeight = node.FontHeight > 0 ? (int)node.FontHeight : 16;
    }

    public List<string> Rows { get; } = [];

    public int CurSel { get; private set; } = -1;

    /// <summary>Index of the first visible row (scroll offset).</summary>
    public int ScrollTop { get; private set; }

    public int RowHeight => _rowHeight;

    public int VisibleRowCount => Math.Max(1, Height / Math.Max(1, _rowHeight));

    public int Count => Rows.Count;

    public void ResetContent()
    {
        Rows.Clear();
        CurSel = -1;
        ScrollTop = 0;
    }

    public int AddString(string text)
    {
        Rows.Add(text);
        return Rows.Count - 1;
    }

    public int AddStrings(IEnumerable<string> texts)
    {
        int last = Rows.Count - 1;
        foreach (string t in texts)
            last = AddString(t);
        return last;
    }

    public bool InsertString(int index, string text)
    {
        if (index < 0 || index > Rows.Count)
            return false;
        Rows.Insert(index, text);
        return true;
    }

    public bool DeleteString(int index)
    {
        if (index < 0 || index >= Rows.Count)
            return false;
        Rows.RemoveAt(index);
        if (CurSel == index)
            CurSel = -1;
        else if (CurSel > index)
            CurSel--;
        return true;
    }

    public bool GetString(int index, out string text)
    {
        if (index < 0 || index >= Rows.Count)
        {
            text = string.Empty;
            return false;
        }

        text = Rows[index];
        return true;
    }

    public bool SetString(int index, string text)
    {
        if (index < 0 || index >= Rows.Count)
            return false;
        Rows[index] = text;
        return true;
    }

    /// <summary>CN3UIList::SetCurSel — out-of-range selects nothing (-1).</summary>
    public bool SetCurSel(int index)
    {
        CurSel = index < 0 || index >= Rows.Count ? -1 : index;
        return CurSel >= 0;
    }

    public bool SetScrollPos(int top)
    {
        int maxTop = Math.Max(0, Rows.Count - VisibleRowCount);
        int clamped = Math.Clamp(top, 0, maxTop);
        if (clamped == ScrollTop)
            return false;
        ScrollTop = clamped;
        return true;
    }

    public override bool OnMouseWheel(int delta) => SetScrollPos(ScrollTop - Math.Sign(delta));

    public override UiMouseProc MouseProc(UiMouse flags, UiPoint cur, UiPoint old, UiTooltipControl? tooltip = null)
    {
        var ret = UiMouseProc.None;
        if (!Visible || State == UiState.ListDisable)
            return ret;

        bool click = (flags & UiMouse.LbClick) != 0;
        bool dblClk = (flags & UiMouse.LbDblClk) != 0;
        if (IsIn(cur.X, cur.Y) && (click || dblClk))
        {
            int row = ScrollTop + (cur.Y - Region.Top) / Math.Max(1, _rowHeight);
            if (row >= 0 && row < Rows.Count)
            {
                CurSel = row;
                uint msg = click ? UiMsg.ListSelChange : UiMsg.ListDblClk;
                Parent?.ReceiveMessage(this, msg);
                return ret | UiMouseProc.DoneSomething;
            }
        }

        return ret | base.MouseProc(flags, cur, old, tooltip);
    }
}
