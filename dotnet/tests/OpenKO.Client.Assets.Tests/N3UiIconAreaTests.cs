using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Sub-slice 9.5-1 pins: the ICON/AREA/ICONMNG/ICONSLOT/TOOLTIP layout nodes now parse
/// (previously <c>CreateByType</c> threw) and round-trip byte-for-byte.
/// </summary>
public class N3UiIconAreaTests
{
    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    [Fact]
    public void IconAreaNodes_RoundTrip()
    {
        var root = new N3UiBase
        {
            FileFormatVersion = N3FormatVersion.V1298,
            Id = "UI_INV",
        };

        // CN3UIIcon == CN3UIImage on the wire (texture/uv/animframe).
        var icon = new N3UiIcon
        {
            Id = "5",
            TexFileName = @"ui\icon_sword.dxt",
            UvRect = new N3UiRectF { Left = 0.1f, Top = 0.2f, Right = 0.3f, Bottom = 0.4f },
            AnimFrame = 3f,
        };
        // CN3UITooltip == CN3UIStatic on the wire (one click sound).
        var tip = new N3UiTooltip { Id = "TIP", ClickSoundFileName = @"snd\tip.wav" };
        var mng = new N3UiIconMng { Id = "MNG" };
        var slot = new N3UiIconSlot { Id = "SLOT" };
        var area = new N3UiArea { Id = "12", AreaType = (int)UiAreaType.Inv };

        root.Children.AddRange([icon, tip, mng, slot, area]);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
            root.Save(writer);

        stream.Position = 0;
        var loaded = new N3UiBase { FileFormatVersion = N3FormatVersion.V1298 };
        loaded.Load(new BinaryReader(stream));

        Assert.Equal(stream.Length, stream.Position); // whole file consumed — byte-exact
        Assert.Equal(5, loaded.Children.Count);

        var li = Assert.IsType<N3UiIcon>(loaded.Children[0]);
        Assert.Equal(N3UiType.Icon, li.UiType);
        Assert.Equal(@"ui\icon_sword.dxt", li.TexFileName);
        Assert.Equal(0.3f, li.UvRect.Right);
        Assert.Equal(3f, li.AnimFrame);

        var lt = Assert.IsType<N3UiTooltip>(loaded.Children[1]);
        Assert.Equal(N3UiType.Tooltip, lt.UiType);
        Assert.Equal(@"snd\tip.wav", lt.ClickSoundFileName);

        Assert.Equal(N3UiType.IconManager, Assert.IsType<N3UiIconMng>(loaded.Children[2]).UiType);
        Assert.Equal(N3UiType.IconSlot, Assert.IsType<N3UiIconSlot>(loaded.Children[3]).UiType);

        var la = Assert.IsType<N3UiArea>(loaded.Children[4]);
        Assert.Equal((int)UiAreaType.Inv, la.AreaType);
        Assert.Equal(UiAreaType.Inv, la.AreaTypeEnum);
        Assert.Equal("12", la.Id);
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void AreaBearingUif_ParsesWithAreaTypesAndOrders()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out

        // No shipped .uif serializes ICON/TOOLTIP nodes (icons are built at runtime), but the
        // inventory-family windows (transaction, dropped-item, …) do serialize AREA slot nodes.
        int filesWithAreas = 0, areaNodes = 0;
        foreach (string path in AssetCorpus.EnumerateFiles("*.uif"))
        {
            using var stream = File.OpenRead(path);
            var ui = new N3UiBase { FileFormatVersion = N3FormatVersion.Default };
            try
            {
                ui.Load(new BinaryReader(stream));
            }
            catch
            {
                continue; // legacy char_select.uif etc.
            }

            int here = CountAreas(ui);
            if (here > 0)
            {
                filesWithAreas++;
                areaNodes += here;
            }
        }

        Assert.True(filesWithAreas > 0, "expected at least one .uif with AREA nodes");
        Assert.True(areaNodes > 100, $"only {areaNodes} area nodes parsed — implausible");
    }

    private static int CountAreas(N3UiBase n)
    {
        int c = n is N3UiArea ? 1 : 0;
        foreach (N3UiBase child in n.Children)
            c += CountAreas(child);
        return c;
    }
}
