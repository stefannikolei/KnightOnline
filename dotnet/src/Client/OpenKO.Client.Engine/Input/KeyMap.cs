using Microsoft.Xna.Framework.Input;

namespace OpenKO.Client.Engine.Input;

/// <summary>
/// DirectInput scan codes (DIK_*, dinput.h) — the key space the C++ game code
/// queries — plus the mapping from MonoGame <see cref="Keys"/>. The device
/// layer builds the per-DIK held array from the MonoGame keyboard state.
/// </summary>
public static class KeyMap
{
    // The DIK constants the game code references (subset used by WarFare).
    public const int DIK_ESCAPE = 0x01;
    public const int DIK_1 = 0x02;
    public const int DIK_2 = 0x03;
    public const int DIK_3 = 0x04;
    public const int DIK_4 = 0x05;
    public const int DIK_5 = 0x06;
    public const int DIK_6 = 0x07;
    public const int DIK_7 = 0x08;
    public const int DIK_8 = 0x09;
    public const int DIK_9 = 0x0A;
    public const int DIK_0 = 0x0B;
    public const int DIK_MINUS = 0x0C;
    public const int DIK_EQUALS = 0x0D;
    public const int DIK_BACK = 0x0E;
    public const int DIK_TAB = 0x0F;
    public const int DIK_Q = 0x10;
    public const int DIK_W = 0x11;
    public const int DIK_E = 0x12;
    public const int DIK_R = 0x13;
    public const int DIK_T = 0x14;
    public const int DIK_Y = 0x15;
    public const int DIK_U = 0x16;
    public const int DIK_I = 0x17;
    public const int DIK_O = 0x18;
    public const int DIK_P = 0x19;
    public const int DIK_RETURN = 0x1C;
    public const int DIK_LCONTROL = 0x1D;
    public const int DIK_A = 0x1E;
    public const int DIK_S = 0x1F;
    public const int DIK_D = 0x20;
    public const int DIK_F = 0x21;
    public const int DIK_G = 0x22;
    public const int DIK_H = 0x23;
    public const int DIK_J = 0x24;
    public const int DIK_K = 0x25;
    public const int DIK_L = 0x26;
    public const int DIK_LSHIFT = 0x2A;
    public const int DIK_Z = 0x2C;
    public const int DIK_X = 0x2D;
    public const int DIK_C = 0x2E;
    public const int DIK_V = 0x2F;
    public const int DIK_B = 0x30;
    public const int DIK_N = 0x31;
    public const int DIK_M = 0x32;
    public const int DIK_RSHIFT = 0x36;
    public const int DIK_SPACE = 0x39;
    public const int DIK_F1 = 0x3B;
    public const int DIK_F2 = 0x3C;
    public const int DIK_F3 = 0x3D;
    public const int DIK_F4 = 0x3E;
    public const int DIK_F5 = 0x3F;
    public const int DIK_F6 = 0x40;
    public const int DIK_F7 = 0x41;
    public const int DIK_F8 = 0x42;
    public const int DIK_F9 = 0x43;
    public const int DIK_F10 = 0x44;
    public const int DIK_F11 = 0x57;
    public const int DIK_F12 = 0x58;
    public const int DIK_RCONTROL = 0x9D;
    public const int DIK_HOME = 0xC7;
    public const int DIK_UP = 0xC8;
    public const int DIK_PRIOR = 0xC9; // PageUp
    public const int DIK_LEFT = 0xCB;
    public const int DIK_RIGHT = 0xCD;
    public const int DIK_END = 0xCF;
    public const int DIK_DOWN = 0xD0;
    public const int DIK_NEXT = 0xD1; // PageDown
    public const int DIK_INSERT = 0xD2;
    public const int DIK_DELETE = 0xD3;

    private static readonly (Keys Key, int Dik)[] Table =
    [
        (Keys.Escape, DIK_ESCAPE),
        (Keys.D1, DIK_1), (Keys.D2, DIK_2), (Keys.D3, DIK_3), (Keys.D4, DIK_4), (Keys.D5, DIK_5),
        (Keys.D6, DIK_6), (Keys.D7, DIK_7), (Keys.D8, DIK_8), (Keys.D9, DIK_9), (Keys.D0, DIK_0),
        (Keys.OemMinus, DIK_MINUS), (Keys.OemPlus, DIK_EQUALS),
        (Keys.Back, DIK_BACK), (Keys.Tab, DIK_TAB),
        (Keys.Q, DIK_Q), (Keys.W, DIK_W), (Keys.E, DIK_E), (Keys.R, DIK_R), (Keys.T, DIK_T),
        (Keys.Y, DIK_Y), (Keys.U, DIK_U), (Keys.I, DIK_I), (Keys.O, DIK_O), (Keys.P, DIK_P),
        (Keys.Enter, DIK_RETURN),
        (Keys.LeftControl, DIK_LCONTROL), (Keys.RightControl, DIK_RCONTROL),
        (Keys.A, DIK_A), (Keys.S, DIK_S), (Keys.D, DIK_D), (Keys.F, DIK_F), (Keys.G, DIK_G),
        (Keys.H, DIK_H), (Keys.J, DIK_J), (Keys.K, DIK_K), (Keys.L, DIK_L),
        (Keys.LeftShift, DIK_LSHIFT), (Keys.RightShift, DIK_RSHIFT),
        (Keys.Z, DIK_Z), (Keys.X, DIK_X), (Keys.C, DIK_C), (Keys.V, DIK_V), (Keys.B, DIK_B),
        (Keys.N, DIK_N), (Keys.M, DIK_M),
        (Keys.Space, DIK_SPACE),
        (Keys.F1, DIK_F1), (Keys.F2, DIK_F2), (Keys.F3, DIK_F3), (Keys.F4, DIK_F4),
        (Keys.F5, DIK_F5), (Keys.F6, DIK_F6), (Keys.F7, DIK_F7), (Keys.F8, DIK_F8),
        (Keys.F9, DIK_F9), (Keys.F10, DIK_F10), (Keys.F11, DIK_F11), (Keys.F12, DIK_F12),
        (Keys.Home, DIK_HOME), (Keys.End, DIK_END),
        (Keys.PageUp, DIK_PRIOR), (Keys.PageDown, DIK_NEXT),
        (Keys.Insert, DIK_INSERT), (Keys.Delete, DIK_DELETE),
        (Keys.Up, DIK_UP), (Keys.Down, DIK_DOWN), (Keys.Left, DIK_LEFT), (Keys.Right, DIK_RIGHT),
    ];

    private static readonly Dictionary<Keys, int> KeyToDikMap = Table.ToDictionary(t => t.Key, t => t.Dik);

    /// <summary>DIK code for a MonoGame key, or -1 when unmapped.</summary>
    public static int ToDik(Keys key) => KeyToDikMap.GetValueOrDefault(key, -1);

    /// <summary>Fills the per-DIK held array from the pressed MonoGame keys.</summary>
    public static void FillDikArray(ReadOnlySpan<Keys> pressedKeys, Span<bool> dikDown)
    {
        dikDown.Clear();
        foreach (Keys key in pressedKeys)
        {
            int dik = ToDik(key);
            if (dik >= 0 && dik < dikDown.Length)
                dikDown[dik] = true;
        }
    }
}
