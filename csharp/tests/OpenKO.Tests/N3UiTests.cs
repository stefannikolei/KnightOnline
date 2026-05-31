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
        w.Int32((int)UiType.Button);      // not yet ported
        // (no more bytes needed; the factory throws before reading)

        var root = new N3UIBase { FileFormatVersion = N3FormatVersion.V1298 };
        using FileReader reader = OpenReader(w.ToArray());
        var ex = Assert.Throws<NotSupportedException>(() => root.Load(reader));
        Assert.Contains("Button", ex.Message);
    }
}
