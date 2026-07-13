using Microsoft.Xna.Framework.Input;
using OpenKO.Client.Engine.Input;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.1 pins: the CLocalInput edge machine and the DIK mapping.</summary>
public class InputStateTests
{
    private static bool[] Keys(params int[] diks)
    {
        var keys = new bool[InputState.NumKeys];
        foreach (int dik in diks)
            keys[dik] = true;
        return keys;
    }

    private static readonly InputSnapshot NoMouse = new(0, 0, false, false, false);

    [Fact]
    public void KeyEdges_MatchLocalInputSemantics()
    {
        var input = new InputState();

        input.Tick(Keys(KeyMap.DIK_SPACE), NoMouse, 0.0);
        Assert.True(input.IsKeyDown(KeyMap.DIK_SPACE));
        Assert.True(input.IsKeyPress(KeyMap.DIK_SPACE));   // down edge
        Assert.False(input.IsKeyPressed(KeyMap.DIK_SPACE));

        input.Tick(Keys(KeyMap.DIK_SPACE), NoMouse, 0.016);
        Assert.True(input.IsKeyDown(KeyMap.DIK_SPACE));
        Assert.False(input.IsKeyPress(KeyMap.DIK_SPACE));  // held, no edge

        input.Tick(Keys(), NoMouse, 0.032);
        Assert.False(input.IsKeyDown(KeyMap.DIK_SPACE));
        Assert.True(input.IsKeyPressed(KeyMap.DIK_SPACE)); // up edge
        Assert.True(input.IsNoKeyDown());
    }

    [Fact]
    public void MouseFlags_ClickDownClickedAndDoubleClick()
    {
        var input = new InputState();
        var none = new bool[InputState.NumKeys];

        input.Tick(none, new InputSnapshot(10, 20, LeftDown: true, false, false), 0.0);
        Assert.True(input.Mouse.HasFlag(MouseFlags.LbClick));
        Assert.True(input.Mouse.HasFlag(MouseFlags.LbDown));
        Assert.False(input.Mouse.HasFlag(MouseFlags.LbDoubleClick));
        Assert.Equal((10, 20), input.MousePos);

        input.Tick(none, new InputSnapshot(11, 21, LeftDown: false, false, false), 0.1);
        Assert.True(input.Mouse.HasFlag(MouseFlags.LbClicked)); // release edge
        Assert.False(input.Mouse.HasFlag(MouseFlags.LbDown));
        Assert.Equal((10, 20), input.MousePosOld);

        // Second press inside the double-click window.
        input.Tick(none, new InputSnapshot(11, 21, LeftDown: true, false, false), 0.3);
        Assert.True(input.Mouse.HasFlag(MouseFlags.LbClick));
        Assert.True(input.Mouse.HasFlag(MouseFlags.LbDoubleClick));

        // A press much later is a plain click again.
        input.Tick(none, new InputSnapshot(11, 21, LeftDown: false, false, false), 0.4);
        input.Tick(none, new InputSnapshot(11, 21, LeftDown: true, false, false), 2.0);
        Assert.True(input.Mouse.HasFlag(MouseFlags.LbClick));
        Assert.False(input.Mouse.HasFlag(MouseFlags.LbDoubleClick));
    }

    [Fact]
    public void KeyMap_MapsMonoGameKeysToDikCodes()
    {
        Assert.Equal(KeyMap.DIK_ESCAPE, KeyMap.ToDik(Microsoft.Xna.Framework.Input.Keys.Escape));
        Assert.Equal(KeyMap.DIK_W, KeyMap.ToDik(Microsoft.Xna.Framework.Input.Keys.W));
        Assert.Equal(KeyMap.DIK_F12, KeyMap.ToDik(Microsoft.Xna.Framework.Input.Keys.F12));
        Assert.Equal(KeyMap.DIK_UP, KeyMap.ToDik(Microsoft.Xna.Framework.Input.Keys.Up));
        Assert.Equal(-1, KeyMap.ToDik(Microsoft.Xna.Framework.Input.Keys.Kana)); // unmapped

        var dik = new bool[InputState.NumKeys];
        KeyMap.FillDikArray(
            [Microsoft.Xna.Framework.Input.Keys.W, Microsoft.Xna.Framework.Input.Keys.LeftShift], dik);
        Assert.True(dik[KeyMap.DIK_W]);
        Assert.True(dik[KeyMap.DIK_LSHIFT]);
        Assert.False(dik[KeyMap.DIK_A]);
    }
}
