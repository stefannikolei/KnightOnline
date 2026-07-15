using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Input;
using OpenKO.Client.Engine.Ui;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-9.2 pins: input→UI bridge, mouse wheel, and CP949 byte-length editing.</summary>
public class UiInputAndEditTests
{
    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    // ---- MouseFlags -> UiMouse bridge -------------------------------------

    [Theory]
    [InlineData(MouseFlags.LbClick, UiMouse.LbClick)]
    [InlineData(MouseFlags.LbClicked, UiMouse.LbClicked)]
    [InlineData(MouseFlags.LbDown, UiMouse.LbDown)]
    [InlineData(MouseFlags.MbClick, UiMouse.MbClick)]
    [InlineData(MouseFlags.RbDown, UiMouse.RbDown)]
    [InlineData(MouseFlags.LbDoubleClick, UiMouse.LbDblClk)]
    [InlineData(MouseFlags.MbDoubleClick, UiMouse.MbDblClk)]
    [InlineData(MouseFlags.RbDoubleClick, UiMouse.RbDblClk)]
    public void ToUiMouse_MapsEachBit(MouseFlags input, UiMouse expected)
        => Assert.Equal(expected, UiInputBridge.ToUiMouse(input));

    [Fact]
    public void ToUiMouse_MapsCombinedFlags()
    {
        UiMouse ui = UiInputBridge.ToUiMouse(MouseFlags.LbClick | MouseFlags.LbDown | MouseFlags.LbDoubleClick);
        Assert.Equal(UiMouse.LbClick | UiMouse.LbDown | UiMouse.LbDblClk, ui);
    }

    [Fact]
    public void MouseFlagsAndUiMouseShareNumericLayout()
    {
        // The fidelity fix: the two enums now match LocalInput.h / N3UIDef.h bit-for-bit.
        Assert.Equal((uint)MouseFlags.LbDoubleClick, (uint)UiMouse.LbDblClk);
        Assert.Equal((uint)MouseFlags.RbDown, (uint)UiMouse.RbDown);
    }

    // ---- InputState wheel -------------------------------------------------

    [Fact]
    public void InputState_ExposesWheelDelta()
    {
        var input = new InputState();
        var keys = new bool[InputState.NumKeys];
        input.Tick(keys, new InputSnapshot(0, 0, false, false, false, 120), 0.0);
        Assert.Equal(120, input.WheelDelta);
        input.Tick(keys, new InputSnapshot(0, 0, false, false, false), 0.1);
        Assert.Equal(0, input.WheelDelta);
    }

    // ---- Bridge drives the manager ---------------------------------------

    [Fact]
    public void Dispatch_ClicksButtonUnderCursor()
    {
        var mgr = new UiManager();
        var dlg = new UiControl(new N3UiBase { Id = "DLG", Region = Rect(0, 0, 100, 40) });
        var button = new UiButton(new N3UiButton { Id = "B", Style = UiStyle.BtnNormal, Region = Rect(0, 0, 100, 40), ClickRect = Rect(0, 0, 100, 40) });
        dlg.AddChild(button);
        mgr.Add(dlg);

        var input = new InputState();
        var keys = new bool[InputState.NumKeys];
        input.Tick(keys, new InputSnapshot(10, 10, true, false, false), 0.0); // left press at (10,10)

        UiInputBridge.Dispatch(mgr, input);
        Assert.Equal(UiState.ButtonDown, button.State);
    }

    [Fact]
    public void Dispatch_WheelScrollsListUnderCursor()
    {
        var mgr = new UiManager();
        var list = new UiListControl(new N3UiList { Id = "L", Region = Rect(0, 0, 100, 32), FontName = "Gulim", FontHeight = 16 });
        for (int i = 0; i < 5; i++)
            list.AddString($"r{i}");
        mgr.Add(list);

        var input = new InputState();
        var keys = new bool[InputState.NumKeys];
        input.Tick(keys, new InputSnapshot(10, 10, false, false, false, -120), 0.0);

        UiInputBridge.Dispatch(mgr, input);
        Assert.Equal(1, list.ScrollTop);
    }

    // ---- CP949 byte-length editing (CN3UIEdit::SetMaxString) ---------------

    [Fact]
    public void Edit_MaxLength_CountsCp949Bytes()
    {
        // A Hangul glyph is 2 CP949 bytes; MaxLength=3 fits one glyph + one ASCII.
        var e = new UiEditControl(new N3UiEdit { Id = "E", Region = Rect(0, 0, 100, 20) }) { MaxLength = 3 };

        Assert.True(e.InsertChar('가'));   // 2 bytes
        Assert.False(e.InsertChar('나'));  // would be 4 bytes > 3
        Assert.True(e.InsertChar('x'));    // 2 + 1 = 3 bytes, fits
        Assert.Equal("가x", e.Text);
    }

    [Fact]
    public void Edit_BulkSet_TruncatesOnCharBoundary()
    {
        var e = new UiEditControl(new N3UiEdit { Id = "E", Region = Rect(0, 0, 100, 20) }) { MaxLength = 3 };
        e.Text = "가나"; // 4 bytes -> keep only the first glyph (2 bytes), never a half glyph
        Assert.Equal("가", e.Text);
    }
}
