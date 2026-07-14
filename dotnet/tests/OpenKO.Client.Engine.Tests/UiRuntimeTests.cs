using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-9.1 pins: the interactive UI runtime (controls, buttons, list, edit, manager).</summary>
public class UiRuntimeTests
{
    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiButton ButtonNode(string id, uint style) => new()
    {
        Id = id,
        Style = style,
        Region = Rect(0, 0, 100, 40),
        ClickRect = Rect(0, 0, 100, 40),
    };

    private static UiControl Container(params UiControl[] children)
    {
        var root = new UiControl(new N3UiBase { Id = "ROOT", Region = Rect(0, 0, 400, 400) });
        foreach (UiControl c in children)
            root.AddChild(c);
        return root;
    }

    // ---- Button (normal) --------------------------------------------------

    [Fact]
    public void NormalButton_ClickThenRelease_LatchesOnAndPostsMessage()
    {
        var button = new UiButton(ButtonNode("BTN", UiStyle.BtnNormal));
        UiControl root = Container(button);
        uint? received = null;
        UiControl? sender = null;
        root.Message += (s, m) => { sender = s; received = m; };

        var cur = new UiPoint(10, 10);
        UiMouseProc r1 = button.MouseProc(UiMouse.LbClick, cur, cur);
        Assert.Equal(UiState.ButtonDown, button.State);
        Assert.True((r1 & UiMouseProc.DoneSomething) != 0);

        UiMouseProc r2 = button.MouseProc(UiMouse.LbClicked, cur, cur);
        Assert.Equal(UiState.ButtonOn, button.State);
        Assert.True((r2 & UiMouseProc.DoneSomething) != 0);
        Assert.Equal(UiMsg.ButtonClick, received);
        Assert.Same(button, sender);
    }

    [Fact]
    public void NormalButton_HoverHighlights_WithoutDoneSomething()
    {
        var button = new UiButton(ButtonNode("BTN", UiStyle.BtnNormal));
        _ = Container(button);

        var cur = new UiPoint(10, 10);
        UiMouseProc r = button.MouseProc(UiMouse.None, cur, cur);

        Assert.Equal(UiState.ButtonOn, button.State);
        // Hover must NOT report DoneSomething (C++ note: avoids stale state on fast moves).
        Assert.True((r & UiMouseProc.DoneSomething) == 0);
        Assert.True((r & UiMouseProc.InRegion) != 0);
    }

    [Fact]
    public void NormalButton_LeavingRegion_ResetsToNormal()
    {
        var button = new UiButton(ButtonNode("BTN", UiStyle.BtnNormal));
        _ = Container(button);
        var inside = new UiPoint(10, 10);
        button.MouseProc(UiMouse.None, inside, inside); // -> On

        var outside = new UiPoint(500, 500);
        button.MouseProc(UiMouse.None, outside, inside); // cur out, old in

        Assert.Equal(UiState.ButtonNormal, button.State);
    }

    // ---- Button (check / toggle) ------------------------------------------

    [Fact]
    public void CheckButton_TogglesOnThenOffOverTwoClickCycles()
    {
        var button = new UiButton(ButtonNode("CHK", UiStyle.BtnCheck));
        UiControl root = Container(button);
        int clicks = 0;
        root.Message += (_, m) => { if (m == UiMsg.ButtonClick) clicks++; };
        var cur = new UiPoint(10, 10);

        button.MouseProc(UiMouse.LbClick, cur, cur);
        Assert.Equal(UiState.ButtonDown2CheckDown, button.State);
        button.MouseProc(UiMouse.LbClicked, cur, cur);
        Assert.Equal(UiState.ButtonDown, button.State);
        Assert.True(button.IsChecked);

        button.MouseProc(UiMouse.LbClick, cur, cur);
        Assert.Equal(UiState.ButtonDown2CheckUp, button.State);
        button.MouseProc(UiMouse.LbClicked, cur, cur);
        Assert.Equal(UiState.ButtonOn, button.State);
        Assert.False(button.IsChecked);

        Assert.Equal(2, clicks);
    }

    // ---- MoveOffset / drag ------------------------------------------------

    [Fact]
    public void MoveOffset_ShiftsRegionChildrenAndButtonClickRect()
    {
        var button = new UiButton(ButtonNode("BTN", UiStyle.BtnNormal));
        var root = new UiControl(new N3UiBase { Id = "ROOT", Region = Rect(0, 0, 200, 200) });
        root.AddChild(button);

        root.MoveOffset(10, 20);

        Assert.Equal(10, root.Region.Left);
        Assert.Equal(10, button.Region.Left);
        Assert.Equal(20, button.Region.Top);
        Assert.Equal(10, button.ClickRect.Left);
        Assert.Equal(20, button.ClickRect.Top);
    }

    [Fact]
    public void Drag_StartsOnMovablePressAndFollowsCursor()
    {
        var node = new N3UiBase { Id = "DLG", Region = Rect(0, 0, 100, 100), Movable = Rect(0, 0, 100, 20) };
        var dlg = new UiControl(node);

        dlg.MouseProc(UiMouse.LbClick, new UiPoint(10, 10), new UiPoint(10, 10)); // press in movable
        Assert.Equal(UiState.CommonMove, dlg.State);

        dlg.MouseProc(UiMouse.None, new UiPoint(40, 30), new UiPoint(10, 10)); // drag +30,+20
        Assert.Equal(30, dlg.Region.Left);
        Assert.Equal(20, dlg.Region.Top);

        dlg.MouseProc(UiMouse.LbClicked, new UiPoint(40, 30), new UiPoint(40, 30)); // release
        Assert.Equal(UiState.CommonNone, dlg.State);
    }

    [Fact]
    public void GetChildById_FindsNestedTypedControl()
    {
        var button = new UiButton(ButtonNode("OK", UiStyle.BtnNormal));
        UiControl root = Container(Container(button));

        Assert.Same(button, root.GetChildById<UiButton>("OK"));
        Assert.Null(root.GetChildById<UiListControl>("OK"));
        Assert.NotNull(root.GetChildById("OK"));
    }

    // ---- List -------------------------------------------------------------

    [Fact]
    public void List_ClickSelectsRowAndPostsSelChange()
    {
        var listNode = new N3UiList { Id = "LST", Region = Rect(0, 0, 100, 80), FontName = "Gulim", FontHeight = 16 };
        var list = new UiListControl(listNode);
        UiControl root = Container(list);
        uint? msg = null;
        root.Message += (_, m) => msg = m;

        list.AddString("alpha");
        list.AddString("beta");
        list.AddString("gamma");

        // Row height 16; row index 1 spans y in [16..32).
        list.MouseProc(UiMouse.LbClick, new UiPoint(10, 20), new UiPoint(10, 20));

        Assert.Equal(1, list.CurSel);
        Assert.Equal(UiMsg.ListSelChange, msg);
    }

    [Fact]
    public void List_WheelScrollsWithinBounds()
    {
        var listNode = new N3UiList { Id = "LST", Region = Rect(0, 0, 100, 32), FontName = "Gulim", FontHeight = 16 };
        var list = new UiListControl(listNode); // 2 visible rows
        for (int i = 0; i < 5; i++)
            list.AddString($"row{i}");

        Assert.True(list.OnMouseWheel(-1)); // one row per notch
        Assert.Equal(1, list.ScrollTop);
        list.OnMouseWheel(-1);
        list.OnMouseWheel(-1);
        Assert.Equal(3, list.ScrollTop);        // clamped to Count-Visible = 3
        Assert.False(list.OnMouseWheel(-1));    // already at bottom
        Assert.Equal(3, list.ScrollTop);
        list.OnMouseWheel(1);
        Assert.Equal(2, list.ScrollTop);
    }

    // ---- Edit -------------------------------------------------------------

    [Fact]
    public void Edit_TypeBackspaceReturn()
    {
        var edit = new UiEditControl(new N3UiEdit { Id = "E", Region = Rect(0, 0, 100, 20) });
        UiControl root = Container(edit);
        bool returned = false;
        root.Message += (_, m) => { if (m == UiMsg.EditReturn) returned = true; };

        edit.SetFocus();
        Assert.True(edit.Focused);
        foreach (char c in "ab1")
            edit.InsertChar(c);
        Assert.Equal("ab1", edit.Text);
        edit.Backspace();
        Assert.Equal("ab", edit.Text);
        edit.SubmitReturn();
        Assert.True(returned);
    }

    [Fact]
    public void Edit_PasswordMasksAndMaxLengthAndNumberOnly()
    {
        var pwd = new UiEditControl(new N3UiEdit { Id = "P", Style = UiStyle.EditPassword, Region = Rect(0, 0, 100, 20) })
        {
            MaxLength = 3,
        };
        foreach (char c in "abcd")
            pwd.InsertChar(c);
        Assert.Equal("abc", pwd.Text);       // capped at 3
        Assert.Equal("***", pwd.DisplayText); // masked

        var num = new UiEditControl(new N3UiEdit { Id = "N", Style = UiStyle.EditNumberOnly, Region = Rect(0, 0, 100, 20) });
        foreach (char c in "1a2")
            num.InsertChar(c);
        Assert.Equal("12", num.Text);
    }

    // ---- Manager ----------------------------------------------------------

    [Fact]
    public void Manager_DispatchesToTopmostAndBringsItToFront()
    {
        var mgr = new UiManager();
        var back = new UiButton(ButtonNode("BACK", UiStyle.BtnNormal));
        var frontDlg = new UiControl(new N3UiBase { Id = "FRONT", Region = Rect(0, 0, 100, 40) });
        // A dialog wrapping the button so the manager dispatches into it.
        var backDlg = new UiControl(new N3UiBase { Id = "BACKDLG", Region = Rect(0, 0, 100, 40) });
        backDlg.AddChild(back);

        mgr.Add(backDlg);
        mgr.Add(frontDlg); // front = index 0

        // Click hits both regions; the front dialog consumes it (DialogFocus) and stays front.
        UiMouseProc r = mgr.MouseProc(UiMouse.LbClick, new UiPoint(10, 10), new UiPoint(10, 10));
        Assert.True((r & UiMouseProc.DialogFocus) != 0 || (r & UiMouseProc.DoneSomething) != 0);
        Assert.Same(frontDlg, mgr.Dialogs[0]);
    }

    [Fact]
    public void Manager_ModalLock_RoutesOnlyToModalDialog()
    {
        var mgr = new UiManager();
        var other = new UiButton(ButtonNode("OTHER", UiStyle.BtnNormal));
        var otherDlg = new UiControl(new N3UiBase { Id = "OTHER_DLG", Region = Rect(0, 0, 100, 40) });
        otherDlg.AddChild(other);
        var modal = new UiControl(new N3UiBase { Id = "MODAL", Region = Rect(0, 0, 100, 40) });

        mgr.Add(otherDlg);
        mgr.Add(modal);
        mgr.ModalId = "MODAL";

        mgr.MouseProc(UiMouse.LbClick, new UiPoint(10, 10), new UiPoint(10, 10));
        // The non-modal button never saw the click.
        Assert.Equal(UiState.ButtonNormal, other.State);
    }

    [Fact]
    public void Manager_TracksSingleFocusedEdit()
    {
        var mgr = new UiManager();
        var e1 = new UiEditControl(new N3UiEdit { Id = "E1", Region = Rect(0, 0, 100, 20) });
        var e2 = new UiEditControl(new N3UiEdit { Id = "E2", Region = Rect(0, 30, 100, 50) });
        var dlg = new UiControl(new N3UiBase { Id = "DLG", Region = Rect(0, 0, 100, 60) });
        dlg.AddChild(e1);
        dlg.AddChild(e2);
        mgr.Add(dlg);

        mgr.MouseProc(UiMouse.LbClick, new UiPoint(10, 10), new UiPoint(10, 10)); // click e1
        Assert.Same(e1, mgr.FocusedEdit);
        Assert.True(e1.Focused);

        mgr.MouseProc(UiMouse.LbClick, new UiPoint(10, 40), new UiPoint(10, 40)); // click e2
        Assert.Same(e2, mgr.FocusedEdit);
        Assert.False(e1.Focused);
        Assert.True(e2.Focused);
    }

    // ---- State-aware renderer --------------------------------------------

    [Fact]
    public void Renderer_ButtonDownState_DrawsDownImage()
    {
        var node = ButtonNode("BTN", UiStyle.BtnNormal);
        var normal = new N3UiImage { Id = "n", TexFileName = "n.dxt", Reserved = 0, Region = Rect(0, 0, 100, 40), UvRect = new N3UiRectF { Right = 1, Bottom = 1 } };
        var down = new N3UiImage { Id = "d", TexFileName = "d.dxt", Reserved = 1, Region = Rect(0, 0, 100, 40), UvRect = new N3UiRectF { Right = 1, Bottom = 1 } };
        node.Children.Add(normal);
        node.Children.Add(down);
        var button = (UiButton)UiControlFactory.Build(node);

        button.State = UiState.ButtonDown;
        (List<UiQuadPlan> quads, _) = UiRenderer.BuildPlans(button);
        Assert.Equal("d.dxt", Assert.Single(quads).TexFileName);
    }

    [Fact]
    public void Renderer_ListRows_RenderAtRowHeightOffsets()
    {
        var listNode = new N3UiList { Id = "LST", Region = Rect(5, 100, 105, 180), FontName = "Gulim", FontHeight = 16, FontColor = 0xFFFFFFFF };
        var list = new UiListControl(listNode);
        list.AddString("one");
        list.AddString("two");

        (_, List<UiTextPlan> texts) = UiRenderer.BuildPlans(list);

        Assert.Equal(2, texts.Count);
        Assert.Equal("one", texts[0].Text);
        Assert.Equal(100, texts[0].Region.Top);
        Assert.Equal("two", texts[1].Text);
        Assert.Equal(116, texts[1].Region.Top);
    }
}
