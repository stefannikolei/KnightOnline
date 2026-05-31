using System.Buffers.Binary;
using System.Text;
using OpenKO.IO;
using OpenKO.N3;
using Xunit;

namespace OpenKO.Tests;

public class N3UiTests
{
    /// <summary>Helper that writes the raw bytes of a .uif stream in the original layout.</summary>
    private sealed class UifWriter
    {
        private readonly MemoryStream _ms = new();

        public byte[] ToArray() => _ms.ToArray();

        public void Int32(int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, v);
            _ms.Write(b);
        }

        public void UInt32(uint v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            _ms.Write(b);
        }

        public void Int16(short v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(b, v);
            _ms.Write(b);
        }

        public void Single(float v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(b, v);
            _ms.Write(b);
        }

        /// <summary>int32 length prefix + raw bytes (the N3 resource-name / id convention).</summary>
        public void LenString(string s)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(s);
            Int32(bytes.Length);
            _ms.Write(bytes);
        }

        public void Rect(int l, int t, int r, int b)
        {
            Int32(l); Int32(t); Int32(r); Int32(b);
        }

        /// <summary>Writes the trailing common fields of a base UI node (id..sounds), format >= 1264.</summary>
        public void BaseTail(string id, int l, int t, int r, int b, uint style = 0, uint reserved = 0,
            string tooltip = "", string sndOpen = "", string sndClose = "")
        {
            LenString(id);
            Rect(l, t, r, b);          // region
            Rect(0, 0, 0, 0);          // movable
            UInt32(style);
            UInt32(reserved);
            LenString(tooltip);
            LenString(sndOpen);
            LenString(sndClose);
        }
    }

    private static FileReader OpenReader(byte[] data)
    {
        var reader = new FileReader();
        reader.OpenFromMemory(data);
        return reader;
    }

    [Fact]
    public void ParsesRootWithImageAndAreaChildren()
    {
        var w = new UifWriter();

        // --- root resource name header (N3BaseFileAccess) ---
        w.LenString("login_dialog");

        // --- child count (format >= 1264: int16 + int16 padding) ---
        w.Int16(2);
        w.Int16(0);

        // --- child 0: an Image ---
        w.Int32((int)UiType.Image);
        w.LenString("");                 // image: resource name (empty)
        w.Int16(0); w.Int16(0);          // image: 0 children
        w.BaseTail("logo", 10, 20, 110, 70, style: 0x00010000); // image base tail
        w.LenString("Texture\\UI\\logo.dxt"); // texture file name
        w.Single(0f); w.Single(0f); w.Single(1f); w.Single(1f); // uv rect
        w.Single(8.0f);                  // anim frame

        // --- child 1: an Area ---
        w.Int32((int)UiType.Area);
        w.LenString("");                 // area: resource name
        w.Int16(0); w.Int16(0);          // area: 0 children
        w.BaseTail("click_zone", 0, 0, 200, 200);
        w.Int32((int)UiAreaType.Slot);   // area type

        // --- root base tail ---
        w.BaseTail("root", 0, 0, 640, 480);

        var root = new N3UIBase { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(root.Load(reader));

        Assert.Equal("login_dialog", root.Name);
        Assert.Equal("root", root.Id);
        Assert.Equal(640, root.Width);
        Assert.Equal(480, root.Height);
        Assert.Equal(2, root.Children.Count);

        var image = root.FindById<N3UIImage>("logo");
        Assert.NotNull(image);
        Assert.Equal(UiType.Image, image!.Type);
        Assert.Equal("Texture\\UI\\logo.dxt", image.TextureFileName);
        Assert.Equal(1f, image.UvRect.Right);
        Assert.Equal(8.0f, image.AnimFrame);
        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);

        var area = root.FindById<N3UIArea>("click_zone");
        Assert.NotNull(area);
        Assert.Equal(UiAreaType.Slot, area!.AreaType);
    }

    [Fact]
    public void NestedChildrenParseRecursively()
    {
        var w = new UifWriter();
        w.LenString("panel");            // root name
        w.Int16(1); w.Int16(0);          // root: 1 child

        // child: an Image that itself has 1 Area child
        w.Int32((int)UiType.Image);
        w.LenString("");                 // image name
        w.Int16(1); w.Int16(0);          // image: 1 child
        //   grandchild: Area
        w.Int32((int)UiType.Area);
        w.LenString("");
        w.Int16(0); w.Int16(0);
        w.BaseTail("inner", 5, 5, 25, 25);
        w.Int32((int)UiAreaType.Inv);
        //   image base tail + image-specific
        w.BaseTail("outer", 0, 0, 100, 100);
        w.LenString("tex.dxt");
        w.Single(0); w.Single(0); w.Single(1); w.Single(1);
        w.Single(0);

        w.BaseTail("root", 0, 0, 300, 300); // root tail

        var root = new N3UIBase { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(root.Load(reader));

        var inner = root.FindById<N3UIArea>("inner");
        Assert.NotNull(inner);
        Assert.Equal(UiAreaType.Inv, inner!.AreaType);
        Assert.Equal("outer", inner.Parent!.Id);
    }

    [Fact]
    public void LegacyFormatUsesInt32ChildCount()
    {
        var w = new UifWriter();
        w.LenString("legacy");
        w.Int32(0);                       // legacy (< 1264): int32 child count
        w.BaseTail("root", 0, 0, 10, 10);

        var root = new N3UIBase { FileFormatVersion = N3FormatVersion.V1068 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(root.Load(reader));
        Assert.Equal("root", root.Id);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void UnportedControlTypeThrowsClearly()
    {
        var w = new UifWriter();
        w.LenString("dlg");
        w.Int16(1); w.Int16(0);
        w.Int32((int)UiType.Icon);        // Icon is not yet ported
        // factory throws before reading any further bytes

        var root = new N3UIBase { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        var ex = Assert.Throws<NotSupportedException>(() => root.Load(reader));
        Assert.Contains("Icon", ex.Message);
    }

    // ---- helpers shared by the new control tests ---------------------

    /// <summary>Writes a minimal base-node stream: name + no children + base tail.</summary>
    private static byte[] BaseOnly(string name, string id, int l, int t, int r, int b)
    {
        var w = new UifWriter();
        w.LenString(name);
        w.Int16(0); w.Int16(0);  // 0 children (v1298 encoding)
        w.BaseTail(id, l, t, r, b);
        return w.ToArray();
    }

    // ---- Progress -------------------------------------------------------

    [Fact]
    public void ProgressLoadsBaseFieldsOnly()
    {
        byte[] data = BaseOnly("hp_bar", "hp", 0, 0, 200, 20);
        var node = new N3UIProgress { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(data);
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.Progress, node.Type);
        Assert.Equal("hp", node.Id);
        Assert.Equal(200, node.Width);
    }

    // ---- ScrollBar & TrackBar -------------------------------------------

    [Fact]
    public void ScrollBarWithTrackBarChildLoads()
    {
        var w = new UifWriter();
        w.LenString("scroll");
        w.Int16(1); w.Int16(0);   // 1 child

        // TrackBar child
        w.Int32((int)UiType.TrackBar);
        w.LenString("");
        w.Int16(0); w.Int16(0);
        w.BaseTail("thumb", 0, 0, 20, 100);

        w.BaseTail("sb", 0, 0, 20, 200);  // scrollbar base tail

        var node = new N3UIScrollBar { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.ScrollBar, node.Type);
        Assert.Single(node.Children);
        Assert.IsType<N3UITrackBar>(node.Children[0]);
        Assert.Equal("thumb", node.Children[0].Id);
    }

    // ---- Static ---------------------------------------------------------

    [Fact]
    public void StaticReadsClickSound()
    {
        var w = new UifWriter();
        w.LenString("panel");
        w.Int16(0); w.Int16(0);
        w.BaseTail("pnl", 0, 0, 300, 50);
        w.LenString("Sound\\UI\\click.wav");   // click sound

        var node = new N3UIStatic { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.Static, node.Type);
        Assert.Equal("Sound\\UI\\click.wav", node.ClickSound);
    }

    [Fact]
    public void StaticWithNoSoundHasEmptyClickSound()
    {
        var w = new UifWriter();
        w.LenString("x");
        w.Int16(0); w.Int16(0);
        w.BaseTail("x", 5, 5, 15, 15);
        w.Int32(0);   // snd length = 0

        var node = new N3UIStatic { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(string.Empty, node.ClickSound);
    }

    // ---- Tooltip --------------------------------------------------------

    [Fact]
    public void TooltipIsSubtypeOfStatic()
    {
        var w = new UifWriter();
        w.LenString("tip");
        w.Int16(0); w.Int16(0);
        w.BaseTail("tip", 0, 0, 100, 20);
        w.Int32(0);   // no click sound

        var node = new N3UITooltip { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.Tooltip, node.Type);
        Assert.IsAssignableFrom<N3UIStatic>(node);
    }

    // ---- Edit -----------------------------------------------------------

    [Fact]
    public void EditReadsClickAndTypingSounds()
    {
        var w = new UifWriter();
        w.LenString("username");
        w.Int16(0); w.Int16(0);
        w.BaseTail("user_edit", 10, 10, 200, 35);
        w.LenString("Sound\\UI\\focus.wav");   // click/focus sound (Static level)
        w.LenString("Sound\\UI\\type.wav");    // typing sound (Edit level)

        var node = new N3UIEdit { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.Edit, node.Type);
        Assert.Equal("Sound\\UI\\focus.wav", node.ClickSound);
        Assert.Equal("Sound\\UI\\type.wav", node.TypingSound);
    }

    [Fact]
    public void EditWithNoSoundsHasEmptyStrings()
    {
        var w = new UifWriter();
        w.LenString("e");
        w.Int16(0); w.Int16(0);
        w.BaseTail("e", 0, 0, 100, 20);
        w.Int32(0);  // no click sound
        w.Int32(0);  // no typing sound

        var node = new N3UIEdit { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(string.Empty, node.ClickSound);
        Assert.Equal(string.Empty, node.TypingSound);
    }

    // ---- Button ---------------------------------------------------------

    [Fact]
    public void ButtonReadsClickRectAndSounds()
    {
        var w = new UifWriter();
        w.LenString("login_btn");
        w.Int16(0); w.Int16(0);
        w.BaseTail("btn_login", 400, 350, 500, 380);
        // click rect (may differ from base region)
        w.Int32(402); w.Int32(352); w.Int32(498); w.Int32(378);
        w.LenString("Sound\\UI\\hover.wav");  // hover sound
        w.LenString("Sound\\UI\\press.wav"); // click sound

        var node = new N3UIButton { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.Button, node.Type);
        Assert.Equal(402, node.ClickRegion.Left);
        Assert.Equal(498, node.ClickRegion.Right);
        Assert.Equal("Sound\\UI\\hover.wav", node.HoverSound);
        Assert.Equal("Sound\\UI\\press.wav", node.ClickSound);
    }

    [Fact]
    public void ButtonWithNoSoundsLoads()
    {
        var w = new UifWriter();
        w.LenString("btn");
        w.Int16(0); w.Int16(0);
        w.BaseTail("b", 0, 0, 80, 25);
        w.Int32(0); w.Int32(0); w.Int32(80); w.Int32(25);  // click rect = base
        w.Int32(0);  // no hover sound
        w.Int32(0);  // no click sound

        var node = new N3UIButton { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(string.Empty, node.HoverSound);
        Assert.Equal(string.Empty, node.ClickSound);
    }

    // ---- String ---------------------------------------------------------

    [Fact]
    public void StringReadsAllFontAndTextFields()
    {
        var w = new UifWriter();
        w.LenString("lbl");
        w.Int16(0); w.Int16(0);
        w.BaseTail("lbl_id", 50, 10, 200, 30);
        // font
        w.LenString("Gulim");   // font name (int32 len + bytes via LenString)
        w.UInt32(12);            // height
        w.UInt32(0x0001);        // bold flag
        // color + text
        w.UInt32(0xFFFFFFFF);    // white (ARGB)
        w.LenString("Hello KO");
        w.Int32(0);              // m_iIdk0 (format >= 1264 extra int)

        var node = new N3UIString { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.String, node.Type);
        Assert.Equal("Gulim", node.FontName);
        Assert.Equal(12u, node.FontHeight);
        Assert.True(node.IsBold);
        Assert.False(node.IsItalic);
        Assert.Equal(0xFFFFFFFFu, node.Color);
        Assert.Equal("Hello KO", node.Text);
    }

    [Fact]
    public void StringLegacyFormatSkipsIdk0()
    {
        var w = new UifWriter();
        w.LenString("s");
        w.Int32(0);               // legacy child count (< 1264: int32)
        w.BaseTail("s", 0, 0, 50, 15);
        w.LenString("Arial");
        w.UInt32(10); w.UInt32(0);
        w.UInt32(0xFF000000u);    // black
        w.LenString("Test");
        // no m_iIdk0 — format < 1264

        var node = new N3UIString { FileFormatVersion = N3FormatVersion.V1068 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal("Test", node.Text);
    }

    // ---- List -----------------------------------------------------------

    [Fact]
    public void ListReadsFontFields()
    {
        var w = new UifWriter();
        w.LenString("chatlist");
        w.Int16(0); w.Int16(0);
        w.BaseTail("lst", 0, 100, 300, 400);
        // font block
        w.LenString("Gulim");
        w.UInt32(10);             // height
        w.UInt32(0xFF00FF00u);    // green
        w.UInt32(0);              // bold = false
        w.UInt32(0);              // italic = false

        var node = new N3UIList { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.Equal(UiType.List, node.Type);
        Assert.Equal("Gulim", node.FontName);
        Assert.Equal(10u, node.FontHeight);
        Assert.Equal(0xFF00FF00u, node.FontColor);
        Assert.False(node.FontBold);
        Assert.False(node.FontItalic);
    }

    [Fact]
    public void ListWithBoldItalicFontLoads()
    {
        var w = new UifWriter();
        w.LenString("l");
        w.Int16(0); w.Int16(0);
        w.BaseTail("l", 0, 0, 100, 200);
        w.LenString("Dotum");
        w.UInt32(12);
        w.UInt32(0xFF888888u);
        w.UInt32(1);  // bold
        w.UInt32(1);  // italic

        var node = new N3UIList { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(node.Load(reader));
        Assert.True(node.FontBold);
        Assert.True(node.FontItalic);
    }

    // ---- Login dialog composite -----------------------------------------

    /// <summary>
    /// Smoke-test: a minimal login dialog with Button, Edit (x2) and String children —
    /// the kind of tree a real login.uif would produce.
    /// </summary>
    [Fact]
    public void LoginDialogCompositeLoads()
    {
        var w = new UifWriter();
        w.LenString("login.uif");

        // 4 children: String label, Edit username, Edit password, Button login
        w.Int16(4); w.Int16(0);

        // child 0 — String
        w.Int32((int)UiType.String);
        w.LenString("");
        w.Int16(0); w.Int16(0);
        w.BaseTail("lbl_id", 10, 10, 200, 30);
        w.LenString("Gulim"); w.UInt32(12); w.UInt32(0);
        w.UInt32(0xFFFFFFFFu);
        w.LenString("Account");
        w.Int32(0); // idk0

        // child 1 — Edit (username)
        w.Int32((int)UiType.Edit);
        w.LenString("");
        w.Int16(0); w.Int16(0);
        w.BaseTail("edit_id", 10, 35, 200, 55);
        w.Int32(0);  // no click sound
        w.Int32(0);  // no typing sound

        // child 2 — Edit (password)
        w.Int32((int)UiType.Edit);
        w.LenString("");
        w.Int16(0); w.Int16(0);
        w.BaseTail("edit_pw", 10, 60, 200, 80);
        w.Int32(0);
        w.Int32(0);

        // child 3 — Button
        w.Int32((int)UiType.Button);
        w.LenString("");
        w.Int16(0); w.Int16(0);
        w.BaseTail("btn_ok", 70, 100, 150, 125);
        w.Int32(70); w.Int32(100); w.Int32(150); w.Int32(125);
        w.Int32(0); w.Int32(0);  // no sounds

        // root tail
        w.BaseTail("login_dialog", 0, 0, 640, 480);

        var root = new N3UIBase { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        Assert.True(root.Load(reader));

        Assert.Equal(4, root.Children.Count);
        Assert.NotNull(root.FindById<N3UIString>("lbl_id"));
        Assert.NotNull(root.FindById<N3UIEdit>("edit_id"));
        Assert.NotNull(root.FindById<N3UIEdit>("edit_pw"));
        Assert.NotNull(root.FindById<N3UIButton>("btn_ok"));

        var btn = root.FindById<N3UIButton>("btn_ok")!;
        Assert.Equal(70, btn.ClickRegion.Left);
        Assert.Equal(150, btn.ClickRegion.Right);
    }
}
