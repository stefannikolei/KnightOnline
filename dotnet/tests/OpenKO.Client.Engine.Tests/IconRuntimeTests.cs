using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Sub-slice 9.5-1 pins: the icon/area UI runtime — <see cref="UiIconControl"/> mouse
/// handshake (port of CN3UIIcon::MouseProc), the WaitFromServer input lock, area order
/// parsing and <see cref="UiControl.GetChildAreaByOrder"/>.
/// </summary>
public class IconRuntimeTests
{
    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiIcon IconNode(string id) => new()
    {
        Id = id,
        Region = Rect(0, 0, 32, 32),
        Movable = Rect(0, 0, 32, 32),
        TexFileName = @"ui\icon.dxt",
    };

    /// <summary>Parent window + one icon; returns the captured (sender, msg) list.</summary>
    private static (UiControl parent, UiIconControl icon, List<uint> msgs) Setup()
    {
        var parent = new UiControl(new N3UiBase { Id = "WND", Region = Rect(0, 0, 400, 400) });
        var icon = new UiIconControl(IconNode("5"));
        parent.AddChild(icon);
        var msgs = new List<uint>();
        parent.Message += (_, m) => msgs.Add(m);
        return (parent, icon, msgs);
    }

    private static readonly UiPoint Inside = new(10, 10);
    private static readonly UiPoint Outside = new(200, 200);

    // ---- Left button: press / release handshake --------------------------

    [Fact]
    public void Press_SetsParentMovingAndPostsDownFirst()
    {
        (UiControl parent, UiIconControl icon, List<uint> msgs) = Setup();

        UiMouseProc r = icon.MouseProc(UiMouse.LbClick, Inside, Inside);

        Assert.True((r & UiMouseProc.DoneSomething) != 0);
        Assert.Equal(UiState.IconMoving, parent.State);
        Assert.Equal([UiMsg.IconDownFirst], msgs);
    }

    [Fact]
    public void Release_WhenMoving_ResetsParentAndPostsUp()
    {
        (UiControl parent, UiIconControl icon, List<uint> msgs) = Setup();
        icon.MouseProc(UiMouse.LbClick, Inside, Inside); // pick up
        msgs.Clear();

        UiMouseProc r = icon.MouseProc(UiMouse.LbClicked, Inside, Inside); // drop

        Assert.True((r & UiMouseProc.DoneSomething) != 0);
        Assert.Equal(UiState.CommonNone, parent.State);
        Assert.Equal([UiMsg.IconUp], msgs);
    }

    [Fact]
    public void Release_WhenNotMoving_PostsNothing()
    {
        (UiControl parent, UiIconControl icon, List<uint> msgs) = Setup();

        UiMouseProc r = icon.MouseProc(UiMouse.LbClicked, Inside, Inside);

        Assert.True((r & UiMouseProc.DoneSomething) == 0);
        Assert.Empty(msgs);
        Assert.Equal(UiState.CommonNone, parent.State);
    }

    [Fact]
    public void Held_PostsIconDown()
    {
        (_, UiIconControl icon, List<uint> msgs) = Setup();
        icon.MouseProc(UiMouse.LbDown, Inside, Inside);
        Assert.Equal([UiMsg.IconDown], msgs);
    }

    [Fact]
    public void LeftDoubleClick_PostsIconDblClk()
    {
        (_, UiIconControl icon, List<uint> msgs) = Setup();
        icon.MouseProc(UiMouse.LbDblClk, Inside, Inside);
        Assert.Equal([UiMsg.IconDblClk], msgs);
    }

    // ---- Right button ----------------------------------------------------

    [Fact]
    public void RightPress_PostsRDownFirst()
    {
        (_, UiIconControl icon, List<uint> msgs) = Setup();
        icon.MouseProc(UiMouse.RbClick, Inside, Inside);
        Assert.Equal([UiMsg.IconRDownFirst], msgs);
    }

    [Fact]
    public void RightRelease_PostsRUp()
    {
        (_, UiIconControl icon, List<uint> msgs) = Setup();
        icon.MouseProc(UiMouse.RbClicked, Inside, Inside);
        Assert.Equal([UiMsg.IconRUp], msgs);
    }

    [Fact]
    public void RightDoubleClick_PostsRDblClk()
    {
        (_, UiIconControl icon, List<uint> msgs) = Setup();
        icon.MouseProc(UiMouse.RbDblClk, Inside, Inside);
        Assert.Equal([UiMsg.IconRDblClk], msgs);
    }

    [Fact]
    public void LeftPress_WhileRightDown_DoesNotPickUp()
    {
        (UiControl parent, UiIconControl icon, List<uint> msgs) = Setup();
        icon.MouseProc(UiMouse.LbClick | UiMouse.RbDown, Inside, Inside);
        Assert.NotEqual(UiState.IconMoving, parent.State);
        Assert.DoesNotContain(UiMsg.IconDownFirst, msgs);
    }

    // ---- WaitFromServer input lock ---------------------------------------

    [Fact]
    public void WaitFromServer_FreezesInput()
    {
        (UiControl parent, UiIconControl icon, List<uint> msgs) = Setup();
        var drag = new IconDragState { WaitFromServer = true };
        icon.DragState = drag;

        UiMouseProc r = icon.MouseProc(UiMouse.LbClick, Inside, Inside);

        Assert.Equal(UiMouseProc.None, r);
        Assert.Empty(msgs);
        Assert.Equal(UiState.CommonNone, parent.State);

        // Unlock — input flows again.
        drag.WaitFromServer = false;
        icon.MouseProc(UiMouse.LbClick, Inside, Inside);
        Assert.Equal(UiState.IconMoving, parent.State);
        Assert.Equal([UiMsg.IconDownFirst], msgs);
    }

    // ---- Hover highlight -------------------------------------------------

    [Fact]
    public void Hover_TogglesHighlight_OnlyWhenWindowIdle()
    {
        (UiControl parent, UiIconControl icon, _) = Setup();

        icon.MouseProc(UiMouse.None, Inside, Inside);
        Assert.True(icon.Highlight);

        icon.MouseProc(UiMouse.None, Outside, Outside);
        Assert.False(icon.Highlight);

        // While the window is moving an icon, no hover highlight is applied.
        parent.State = UiState.IconMoving;
        icon.MouseProc(UiMouse.None, Inside, Inside);
        Assert.False(icon.Highlight);
    }

    [Fact]
    public void MouseProc_OutsideMoveRect_PostsNothing()
    {
        (_, UiIconControl icon, List<uint> msgs) = Setup();
        UiMouseProc r = icon.MouseProc(UiMouse.LbClick, Outside, Outside);
        Assert.Empty(msgs);
        Assert.True((r & UiMouseProc.DoneSomething) == 0);
    }

    // ---- Area order parsing + GetChildAreaByOrder ------------------------

    [Fact]
    public void Area_ParsesTypeAndOrderFromId()
    {
        var area = new UiAreaControl(new N3UiArea { Id = "7", AreaType = (int)UiAreaType.SkillTree, Region = Rect(0, 0, 32, 32) });
        Assert.Equal(UiAreaType.SkillTree, area.AreaType);
        Assert.Equal(7, area.Order);

        var noOrder = new UiAreaControl(new N3UiArea { Id = "NAMED", AreaType = 0 });
        Assert.Equal(-1, noOrder.Order);
    }

    [Fact]
    public void GetChildAreaByOrder_ResolvesMatchingTypeAndOrder()
    {
        var wnd = new UiControl(new N3UiBase { Id = "WND", Region = Rect(0, 0, 400, 400) });
        for (int i = 0; i < 4; i++)
            wnd.AddChild(new UiAreaControl(new N3UiArea { Id = i.ToString(), AreaType = (int)UiAreaType.Inv }));
        wnd.AddChild(new UiAreaControl(new N3UiArea { Id = "0", AreaType = (int)UiAreaType.Slot }));

        UiAreaControl? hit = wnd.GetChildAreaByOrder(UiAreaType.Inv, 2);
        Assert.NotNull(hit);
        Assert.Equal(2, hit!.Order);
        Assert.Equal(UiAreaType.Inv, hit.AreaType);

        // Type must match, not just order.
        UiAreaControl? slot0 = wnd.GetChildAreaByOrder(UiAreaType.Slot, 0);
        Assert.NotNull(slot0);
        Assert.Equal(UiAreaType.Slot, slot0!.AreaType);

        Assert.Null(wnd.GetChildAreaByOrder(UiAreaType.Inv, 99));
    }

    // ---- Factory + UiManager wiring --------------------------------------

    [Fact]
    public void Factory_BuildsIconAndArea_AndManagerBindsDragState()
    {
        var root = new N3UiBase { Id = "WND", Region = Rect(0, 0, 400, 400) };
        root.Children.Add(IconNode("0"));
        root.Children.Add(new N3UiArea { Id = "3", AreaType = (int)UiAreaType.Inv, Region = Rect(0, 0, 32, 32) });

        var mgr = new UiManager();
        UiControl dialog = mgr.AddFromLayout(root);

        var icon = Assert.IsType<UiIconControl>(dialog.Children[0]);
        Assert.IsType<UiAreaControl>(dialog.Children[1]);
        Assert.Same(mgr.IconDrag, icon.DragState); // manager handed its shared instance to the icon
    }
}
