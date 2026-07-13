using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-5.6 pins (UI): the .uif widget-tree reader.</summary>
public class N3UiTests
{
    [Fact]
    public void UiTree_RoundTrips_AllWidgetTypes()
    {
        var root = new N3UiBase
        {
            Name = "dlg_inventory",
            FileFormatVersion = N3FormatVersion.V1298,
            Id = "UI_INVENTORY",
            Region = new N3UiRect { Left = 10, Top = 20, Right = 400, Bottom = 300 },
            Movable = new N3UiRect { Left = 10, Top = 20, Right = 400, Bottom = 40 },
            Style = 0x1,
            ToolTip = "Inventar",
            OpenSoundFileName = @"snd\open.wav",
        };

        var image = new N3UiImage
        {
            Id = "IMG_BG",
            TexFileName = @"ui\inv_bg.dxt",
            UvRect = new N3UiRectF { Left = 0f, Top = 0f, Right = 0.75f, Bottom = 0.5f },
            AnimFrame = 15f,
        };
        var text = new N3UiString
        {
            Id = "TXT_TITLE",
            FontName = "굴림",
            FontHeight = 14,
            FontFlags = 1,
            Color = 0xFFFFFFFF,
            Text = "Inventory",
            Idk0 = -1,
        };
        var button = new N3UiButton
        {
            Id = "BTN_CLOSE",
            ClickRect = new N3UiRect { Left = 1, Top = 2, Right = 3, Bottom = 4 },
            ClickSoundFileName = @"snd\click.wav",
        };
        button.Children.Add(new N3UiImage { Id = "BTN_IMG_NORMAL" });

        var edit = new N3UiEdit
        {
            Id = "EDT_AMOUNT",
            ClickSoundFileName = @"snd\click.wav",
            TypingSoundFileName = @"snd\type.wav",
        };
        var area = new N3UiArea { Id = "AREA_SLOT0", AreaType = 7 };
        var list = new N3UiList
        {
            Id = "LST_ITEMS",
            FontName = "굴림",
            FontHeight = 12,
            FontColor = 0xFF00FF00,
            FontBold = true,
        };
        var progress = new N3UiProgress { Id = "PRG_HP" };
        var scroll = new N3UiScrollBar { Id = "SCR_MAIN" };
        scroll.Children.Add(new N3UiTrackBar { Id = "TRK" });
        var stat = new N3UiStatic { Id = "STA_LABEL" };

        root.Children.AddRange([image, text, button, edit, area, list, progress, scroll, stat]);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            root.Save(writer);
        }

        stream.Position = 0;
        var loaded = new N3UiBase { FileFormatVersion = N3FormatVersion.V1298 };
        loaded.Load(new BinaryReader(stream));

        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal("UI_INVENTORY", loaded.Id);
        Assert.Equal(400, loaded.Region.Right);
        Assert.Equal("Inventar", loaded.ToolTip);
        Assert.Equal(@"snd\open.wav", loaded.OpenSoundFileName);
        Assert.Equal(9, loaded.Children.Count);

        var img = Assert.IsType<N3UiImage>(loaded.Children[0]);
        Assert.Equal(@"ui\inv_bg.dxt", img.TexFileName);
        Assert.Equal(0.75f, img.UvRect.Right);
        Assert.Equal(15f, img.AnimFrame);

        var str = Assert.IsType<N3UiString>(loaded.Children[1]);
        Assert.Equal("굴림", str.FontName);
        Assert.Equal(14u, str.FontHeight);
        Assert.Equal("Inventory", str.Text);
        Assert.Equal(-1, str.Idk0);

        var btn = Assert.IsType<N3UiButton>(loaded.Children[2]);
        Assert.Equal(4, btn.ClickRect.Bottom);
        Assert.Equal(@"snd\click.wav", btn.ClickSoundFileName);
        Assert.IsType<N3UiImage>(Assert.Single(btn.Children));

        var edt = Assert.IsType<N3UiEdit>(loaded.Children[3]);
        Assert.Equal(@"snd\type.wav", edt.TypingSoundFileName);

        Assert.Equal(7, Assert.IsType<N3UiArea>(loaded.Children[4]).AreaType);

        var lst = Assert.IsType<N3UiList>(loaded.Children[5]);
        Assert.True(lst.FontBold);
        Assert.False(lst.FontItalic);
        Assert.Equal(0xFF00FF00, lst.FontColor);

        Assert.IsType<N3UiProgress>(loaded.Children[6]);
        var scr = Assert.IsType<N3UiScrollBar>(loaded.Children[7]);
        Assert.IsType<N3UiTrackBar>(Assert.Single(scr.Children));
        Assert.IsType<N3UiStatic>(loaded.Children[8]);
    }

    [Fact]
    public void UiTree_Pre1264_UsesInt32ChildCount()
    {
        var root = new N3UiBase { FileFormatVersion = N3FormatVersion.V1068, Id = "OLD" };
        root.Children.Add(new N3UiImage { Id = "IMG" });

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            root.Save(writer);
        }

        stream.Position = 0;
        var loaded = new N3UiBase { FileFormatVersion = N3FormatVersion.V1068 };
        loaded.Load(new BinaryReader(stream));

        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal("OLD", loaded.Id);
        Assert.IsType<N3UiImage>(Assert.Single(loaded.Children));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryUifInCorpus_ParsesAndConsumesWholeFile()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out (e.g. CI)

        var failures = new List<string>();
        int count = 0, widgets = 0;

        foreach (string path in AssetCorpus.EnumerateFiles("*.uif"))
        {
            // char_select.uif is a pre-tooltip/sound-era layout that the 1298
            // C++ cannot parse either (and never references) — known legacy.
            if (Path.GetFileName(path).Equals("char_select.uif", StringComparison.OrdinalIgnoreCase))
            {
                Assert.ThrowsAny<Exception>(() =>
                {
                    using var s = File.OpenRead(path);
                    var legacy = new N3UiBase { FileFormatVersion = N3FormatVersion.Default };
                    legacy.Load(new BinaryReader(s));
                    if (s.Position != s.Length)
                        throw new InvalidDataException("legacy trailing bytes"); // either way: no clean parse
                });
                continue;
            }

            count++;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                var ui = new N3UiBase { FileFormatVersion = N3FormatVersion.Default };
                ui.Load(reader);

                if (stream.Position != stream.Length)
                {
                    failures.Add($"{path}: {stream.Length - stream.Position} trailing bytes");
                    continue;
                }

                widgets += CountWidgets(ui);
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count > 25)
                break;
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .uif files failed:\n{string.Join('\n', failures)}");
        Assert.True(count >= 170, $"Corpus scan found only {count} .uif files — checkout incomplete?");
        Assert.True(widgets > 1000, $"only {widgets} widgets parsed — implausible");
    }

    private static int CountWidgets(N3UiBase ui)
    {
        int count = 1;
        foreach (N3UiBase child in ui.Children)
            count += CountWidgets(child);
        return count;
    }
}
