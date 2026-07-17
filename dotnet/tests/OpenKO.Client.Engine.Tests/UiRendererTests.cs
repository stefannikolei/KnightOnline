using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.5 pins: UI traversal order, plans, hit testing.</summary>
public class UiRendererTests
{
    private static N3UiImage Image(string id, string tex, int left = 0, int top = 0) => new()
    {
        Id = id,
        TexFileName = tex,
        Region = new N3UiRect { Left = left, Top = top, Right = left + 100, Bottom = top + 50 },
        UvRect = new N3UiRectF { Left = 0f, Top = 0f, Right = 1f, Bottom = 1f },
    };

    [Fact]
    public void BuildPlans_DrawsSelfThenChildrenTailFirst()
    {
        // Root image with children [head, mid, tail]: draw order must be
        // self, tail, mid, head (rbegin→rend like CN3UIBase::Render).
        N3UiImage root = Image("root", "root.dxt");
        root.Children.Add(Image("head", "head.dxt"));
        root.Children.Add(Image("mid", "mid.dxt"));
        root.Children.Add(Image("tail", "tail.dxt"));

        (List<UiQuadPlan> quads, _) = UiRenderer.BuildPlans(root);

        Assert.Equal(
            ["root.dxt", "tail.dxt", "mid.dxt", "head.dxt"],
            quads.Select(q => q.TexFileName).ToArray());
    }

    [Fact]
    public void BuildPlans_ButtonRendersOnlyTheNormalStateImage()
    {
        var button = new N3UiButton { Id = "BTN" };
        N3UiImage normal = Image("normal", "n.dxt");
        normal.Reserved = 0; // BS_NORMAL
        N3UiImage down = Image("down", "d.dxt");
        down.Reserved = 1; // BS_DOWN
        var caption = new N3UiString { Id = "TXT", Text = "OK", FontName = "Gulim", FontHeight = 12, Color = 0xFF000000 };
        button.Children.AddRange([normal, down, caption]);

        (List<UiQuadPlan> quads, List<UiTextPlan> texts) = UiRenderer.BuildPlans(button);

        Assert.Equal("n.dxt", Assert.Single(quads).TexFileName);
        Assert.Equal("OK", Assert.Single(texts).Text);
        Assert.Equal(0xFF000000, texts[0].ColorArgb);
    }

    [Fact]
    public void BuildPlans_AnimatedImageRendersOnlyTheFrameChild()
    {
        N3UiImage anim = Image("anim", "self.dxt");
        anim.Style = UiStyles.ImageAnimate;
        anim.Children.Add(Image("frame0", "f0.dxt"));
        anim.Children.Add(Image("frame1", "f1.dxt"));

        (List<UiQuadPlan> quads, _) = UiRenderer.BuildPlans(anim);

        // No self-quad, only the current (first) frame.
        Assert.Equal("f0.dxt", Assert.Single(quads).TexFileName);
    }

    [Fact]
    public void BuildPlans_QuadCarriesRegionUvAndColor()
    {
        N3UiImage image = Image("img", "a.dxt", left: 10, top: 20);
        image.UvRect = new N3UiRectF { Left = 0.25f, Top = 0.5f, Right = 0.75f, Bottom = 1f };

        (List<UiQuadPlan> quads, _) = UiRenderer.BuildPlans(image);
        UiQuadPlan quad = Assert.Single(quads);

        Assert.Equal(10, quad.Screen.Left);
        Assert.Equal(70, quad.Screen.Bottom);
        Assert.Equal(0.25f, quad.Uv.Left);
        Assert.Equal(1f, quad.Uv.Bottom);
        Assert.Equal(UiRenderer.OpaqueWhite, quad.ColorArgb);
    }

    [Fact]
    public void BuildPlans_RuntimeIconEmitsQuadWithItsTextureAndIconUv()
    {
        var root = new UiControl(new N3UiBase { Id = "WND", Region = new N3UiRect { Right = 400, Bottom = 400 } });
        UiIconControl icon = UiIconControl.CreateRuntime(new N3UiRect { Left = 10, Top = 20, Right = 55, Bottom = 65 });
        root.AddChild(icon);

        // No texture yet → nothing drawn even though the widget is visible.
        (List<UiQuadPlan> empty, _) = UiRenderer.BuildPlans(root);
        Assert.Empty(empty);

        icon.IconTexture = @"UI\ItemIcon_1_0002_03_4.dxt";
        (List<UiQuadPlan> quads, _) = UiRenderer.BuildPlans(root);
        UiQuadPlan quad = Assert.Single(quads);

        Assert.Equal(@"UI\ItemIcon_1_0002_03_4.dxt", quad.TexFileName);
        Assert.Equal(10, quad.Screen.Left);
        Assert.Equal(65, quad.Screen.Bottom);
        Assert.Equal(45f / 64f, quad.Uv.Right);   // fixed item-icon UV window
        Assert.Equal(UiRenderer.OpaqueWhite, quad.ColorArgb);

        // Durability-exhausted items are drawn with the red tint.
        icon.DurabilityExhausted = true;
        (List<UiQuadPlan> tinted, _) = UiRenderer.BuildPlans(root);
        Assert.Equal(UiRenderer.DurabilityExhaustTint, Assert.Single(tinted).ColorArgb);

        // Hidden slot → nothing.
        icon.DurabilityExhausted = false;
        icon.SetVisible(false);
        (List<UiQuadPlan> hidden, _) = UiRenderer.BuildPlans(root);
        Assert.Empty(hidden);
    }

    [Fact]
    public void HitTest_ReturnsTheTopmostWidget()
    {
        var root = new N3UiBase
        {
            Id = "ROOT",
            Region = new N3UiRect { Left = 0, Top = 0, Right = 200, Bottom = 200 },
        };
        N3UiImage below = Image("below", "b.dxt", left: 0, top: 0);   // tail: drawn first
        N3UiImage above = Image("above", "a.dxt", left: 50, top: 0);  // head: drawn last, topmost
        root.Children.Add(above);
        root.Children.Add(below);

        // Overlap region (50..100, 0..50): head child wins.
        Assert.Equal("above", UiRenderer.HitTest(root, 60, 10)!.Id);
        // Only the tail child covers (0..50).
        Assert.Equal("below", UiRenderer.HitTest(root, 10, 10)!.Id);
        // Root region only.
        Assert.Equal("ROOT", UiRenderer.HitTest(root, 190, 190)!.Id);
        // Outside everything.
        Assert.Null(UiRenderer.HitTest(root, 500, 500));

        // PtInRect: right/bottom exclusive.
        Assert.False(UiRenderer.IsIn(new N3UiRect { Left = 0, Top = 0, Right = 10, Bottom = 10 }, 10, 5));
        Assert.True(UiRenderer.IsIn(new N3UiRect { Left = 0, Top = 0, Right = 10, Bottom = 10 }, 9, 0));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void CorpusUifs_ProduceQuadPlans()
    {
        string? root = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Client", "Data");
            if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
            {
                root = candidate;
                break;
            }
        }

        if (root == null)
            return; // corpus not available (e.g. CI)

        int files = 0, quads = 0, texts = 0;
        foreach (string path in Directory.EnumerateFiles(root, "*.uif", new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
        }))
        {
            if (Path.GetFileName(path).Equals("char_select.uif", StringComparison.OrdinalIgnoreCase))
                continue; // known pre-1264 legacy layout

            var ui = new N3UiBase();
            ui.LoadFromFile(path);
            (List<UiQuadPlan> q, List<UiTextPlan> t) = UiRenderer.BuildPlans(ui);
            files++;
            quads += q.Count;
            texts += t.Count;
        }

        Assert.True(files >= 170, $"only {files} .uif planned");
        Assert.True(quads > 1000, $"only {quads} quads across the corpus — implausible");
        Assert.True(texts > 0, "no text plans across the corpus");
    }
}
