using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Top-level dialog dispatcher — port of <c>CUIManager</c> (Client/WarFare/UIManager.cpp).
/// Holds the open dialogs in z-order (front = topmost = gets input first, drawn last),
/// routes mouse input, tracks the single focused edit (s_pFocusedEdit), and enforces a
/// modal lock (the generalisation of the C++ trade/warehouse/per-trade "only base_tradeedit
/// works" rules). Pure/headless — the executable feeds it input and draws its dialogs.
/// </summary>
public sealed class UiManager
{
    /// <summary>Dialogs in z-order: index 0 = front (topmost).</summary>
    public List<UiControl> Dialogs { get; } = [];

    public UiTooltipControl? Tooltip { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>The single focused edit box (CN3UIBase::s_pFocusedEdit).</summary>
    public UiEditControl? FocusedEdit { get; private set; }

    /// <summary>Last MouseProc result flags (CUIManager::m_dwMouseFlagsCur).</summary>
    public UiMouseProc LastResult { get; private set; }

    /// <summary>
    /// When non-null, only the dialog with this id (and the modal itself) receives input —
    /// the general form of the C++ "base_tradeedit"-only locks during trade/warehouse.
    /// </summary>
    public string? ModalId { get; set; }

    public void Add(UiControl dialog)
    {
        if (!Dialogs.Contains(dialog))
            Dialogs.Insert(0, dialog);
    }

    public void Remove(UiControl dialog)
    {
        Dialogs.Remove(dialog);
        if (FocusedEdit != null && ReferenceEquals(FocusedEdit.Parent, dialog))
            FocusedEdit = null;
    }

    /// <summary>Build a dialog from a parsed .uif root and register it.</summary>
    public UiControl AddFromLayout(N3UiBase layoutRoot)
    {
        UiControl dialog = UiControlFactory.Build(layoutRoot);
        Add(dialog);
        return dialog;
    }

    /// <summary>Bring a dialog to the front (CUIManager::SetFocusedUI z-order move).</summary>
    public void SetFocusedUi(UiControl dialog)
    {
        if (Dialogs.Remove(dialog))
            Dialogs.Insert(0, dialog);
    }

    /// <summary>CUIManager::MouseProc — dispatch to dialogs front-first, honour modal + focus.</summary>
    public UiMouseProc MouseProc(UiMouse flags, UiPoint cur, UiPoint old)
    {
        LastResult = UiMouseProc.None;
        if (!Enabled)
            return LastResult;

        Tooltip?.Clear();

        // A fresh left-press drops edit focus; the clicked edit re-acquires below.
        if ((flags & UiMouse.LbClick) != 0)
        {
            foreach (UiEditControl edit in AllEdits())
                edit.KillFocus();
        }

        // Snapshot: dispatch may reorder Dialogs via SetFocusedUi.
        foreach (UiControl dialog in Dialogs.ToArray())
        {
            if (!dialog.Visible)
                continue;
            if (ModalId != null && dialog.Id != ModalId)
                continue;

            UiMouseProc ret = dialog.MouseProc(flags, cur, old, Tooltip);

            if ((ret & UiMouseProc.DoneSomething) != 0)
            {
                SetFocusedUi(dialog);
                LastResult = UiMouseProc.DoneSomething | UiMouseProc.ChildDoneSomething;
                FocusedEdit = FindActiveEdit();
                return LastResult;
            }

            if ((flags & UiMouse.LbClick) != 0 && (ret & UiMouseProc.InRegion) != 0)
            {
                // Clicked inside a dialog region — consume as a focus grab.
                SetFocusedUi(dialog);
                LastResult = UiMouseProc.DialogFocus;
                FocusedEdit = FindActiveEdit();
                return LastResult;
            }
        }

        FocusedEdit = FindActiveEdit();
        return LastResult;
    }

    /// <summary>Route a mouse-wheel delta to the topmost dialog under the cursor.</summary>
    public bool MouseWheel(int delta, UiPoint cur)
    {
        foreach (UiControl dialog in Dialogs)
        {
            if (!dialog.Visible || !dialog.IsIn(cur.X, cur.Y))
                continue;
            foreach (UiControl c in dialog.Descendants())
            {
                if (c.Visible && c.IsIn(cur.X, cur.Y) && c.OnMouseWheel(delta))
                    return true;
            }

            if (dialog.OnMouseWheel(delta))
                return true;
        }

        return false;
    }

    public void Tick()
    {
        foreach (UiControl dialog in Dialogs)
        {
            if (dialog.Visible)
                dialog.Tick();
        }
    }

    /// <summary>Dialogs in draw order (back-to-front): topmost drawn last.</summary>
    public IEnumerable<UiControl> DialogsInDrawOrder()
    {
        for (int i = Dialogs.Count - 1; i >= 0; i--)
        {
            if (Dialogs[i].Visible)
                yield return Dialogs[i];
        }
    }

    private IEnumerable<UiEditControl> AllEdits()
    {
        foreach (UiControl dialog in Dialogs)
        {
            if (dialog is UiEditControl de)
                yield return de;
            foreach (UiControl c in dialog.Descendants())
            {
                if (c is UiEditControl e)
                    yield return e;
            }
        }
    }

    private UiEditControl? FindActiveEdit()
    {
        foreach (UiEditControl edit in AllEdits())
        {
            if (edit.Focused)
                return edit;
        }

        return null;
    }
}
