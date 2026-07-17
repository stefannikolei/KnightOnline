using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.6-2 pins for class change: the WIZ_CLASS_CHANGE request bytes, the promotion
/// map, the result-driven dialog and the InGameState reply dispatch. Fully headless.
/// </summary>
public class ClassChangeTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port)
        {
        }

        public bool CryptionEnabled { get; private set; }

        public void EnableCryption(ulong publicKey) => CryptionEnabled = true;

        public byte[] Last => Sent[^1];
    }

    // ---- Protocol ----------------------------------------------------------

    [Fact]
    public void BuildRequest_IsClassChangeReqBytes()
    {
        byte[] p = ClassChangeProtocol.BuildRequest(205);
        Assert.Equal(4, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_CLASS_CHANGE, p[0]);
        Assert.Equal(0x02, p[1]);
        Assert.Equal((short)205, BitConverter.ToInt16(p, 2));
    }

    [Fact]
    public void SkillPoint_BuildIsTwoBytes()
    {
        byte[] p = SkillPointProtocol.Build(6);
        Assert.Equal(new byte[] { (byte)GameOpcode.WIZ_SKILLPT_CHANGE, 6 }, p);
    }

    [Theory]
    [InlineData((short)101, (short)105)]
    [InlineData((short)102, (short)107)]
    [InlineData((short)103, (short)109)]
    [InlineData((short)104, (short)111)]
    [InlineData((short)201, (short)205)]
    [InlineData((short)202, (short)207)]
    [InlineData((short)203, (short)209)]
    [InlineData((short)204, (short)211)]
    public void PromotionMap_CoversAllEightBaseClasses(short baseClass, short promo)
    {
        Assert.Equal(promo, ClassChangeProtocol.Promote(baseClass));
    }

    [Fact]
    public void PromotionMap_NonBaseClassUnchanged()
    {
        Assert.Equal((short)105, ClassChangeProtocol.Promote(105)); // already promoted
        Assert.False(ClassChangeProtocol.IsBaseClass(105));
        Assert.True(ClassChangeProtocol.IsBaseClass(101));
    }

    [Fact]
    public void ParseResult_ReturnsSubopcode()
    {
        Assert.Equal(0x01, ClassChangeProtocol.ParseResult([(byte)GameOpcode.WIZ_CLASS_CHANGE, 0x01]));
        Assert.Equal(0x00, ClassChangeProtocol.ParseResult([(byte)GameOpcode.WIZ_CLASS_CHANGE, 0x00]));
    }

    // ---- Dialog ------------------------------------------------------------

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiButton Button(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 40, 16) };

    private static UiControl BuildRoot()
    {
        var root = new N3UiBase { Id = "classchange", Region = Rect(0, 0, 200, 150) };
        root.Children.Add(Button("Btn_Ok"));
        root.Children.Add(Button("Btn_Cancel"));
        root.Children.Add(Button("Btn_Class"));
        root.Children.Add(Str("Text_Waring"));
        root.Children.Add(Str("Text_info"));
        root.Children.Add(Str("Text_Message"));
        return UiControlFactory.Build(root);
    }

    private sealed record Harness(ClassChangeDialog Dialog, UiControl Root, LocalPlayer Local, FakeGameClient Client, GameContext Context);

    private static Harness Build(short cls)
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        context.Machine.SetActive(context.InGame);
        context.InGame.World.Local.Class = cls;
        UiControl root = BuildRoot();
        var dialog = new ClassChangeDialog(context, root);
        return new Harness(dialog, root, context.InGame.World.Local, client, context);
    }

    [Fact]
    public void Open_Success_ShowsBtnClassAndPreviewsPromoName()
    {
        Harness h = Build(cls: 201); // El Warrior → Blade
        h.Dialog.Open(ClassChangeProtocol.ResultSuccess);

        Assert.True(h.Dialog.IsOpen);
        Assert.True(h.Root.GetChildById<UiButton>("Btn_Class")!.Visible);
        Assert.True(h.Root.GetChildById<UiButton>("Btn_Cancel")!.Visible);
        Assert.False(h.Root.GetChildById<UiButton>("Btn_Ok")!.Visible);

        UiStringControl info = h.Root.GetChildById<UiStringControl>("Text_info")!;
        Assert.True(info.Visible);
        Assert.Equal("Blade", info.Text);
    }

    [Theory]
    [InlineData(ClassChangeProtocol.ResultNotYet)]
    [InlineData(ClassChangeProtocol.ResultAlready)]
    [InlineData(ClassChangeProtocol.ResultItemInSlot)]
    public void Open_NonSuccess_ShowsOkOnly(byte code)
    {
        Harness h = Build(cls: 201);
        h.Dialog.Open(code);

        Assert.True(h.Dialog.IsOpen);
        Assert.True(h.Root.GetChildById<UiButton>("Btn_Ok")!.Visible);
        Assert.False(h.Root.GetChildById<UiButton>("Btn_Class")!.Visible);
    }

    [Fact]
    public void Open_Failure_RestoresClassWithoutOpening()
    {
        Harness h = Build(cls: 201);
        // Simulate an optimistic promotion first via a SUCCESS + Btn_Class.
        h.Dialog.Open(ClassChangeProtocol.ResultSuccess);
        h.Root.ReceiveMessage(h.Root.GetChildById<UiButton>("Btn_Class")!, UiMsg.ButtonClick);
        Assert.Equal((short)205, h.Local.Class); // optimistically promoted

        // A late FAILURE rolls the class back and does not open the dialog.
        h.Dialog.Open(ClassChangeProtocol.ResultFailure);
        Assert.Equal((short)201, h.Local.Class);
        Assert.False(h.Dialog.IsOpen);
    }

    [Fact]
    public void BtnClass_Click_PromotesSendsRequestAndRaisesClassChanged()
    {
        Harness h = Build(cls: 103); // KA Wizard → Sorcerer (109)
        bool raised = false;
        h.Dialog.ClassChanged += () => raised = true;

        h.Dialog.Open(ClassChangeProtocol.ResultSuccess);
        h.Root.ReceiveMessage(h.Root.GetChildById<UiButton>("Btn_Class")!, UiMsg.ButtonClick);

        Assert.Equal((short)109, h.Local.Class);
        byte[] p = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_CLASS_CHANGE, p[0]);
        Assert.Equal(0x02, p[1]);
        Assert.Equal((short)109, BitConverter.ToInt16(p, 2));
        Assert.True(raised);
        Assert.False(h.Dialog.IsOpen); // closes after promotion
    }

    [Fact]
    public void BtnCancel_Closes()
    {
        Harness h = Build(cls: 201);
        h.Dialog.Open(ClassChangeProtocol.ResultSuccess);
        h.Root.ReceiveMessage(h.Root.GetChildById<UiButton>("Btn_Cancel")!, UiMsg.ButtonClick);
        Assert.False(h.Dialog.IsOpen);
    }

    // ---- InGameState dispatch ----------------------------------------------

    [Fact]
    public void InGameState_ClassChangeReply_RaisesResultEvent()
    {
        var client = new FakeGameClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        byte? got = null;
        ctx.InGame.ClassChangeResult = c => got = c;

        ctx.Machine.DispatchPacket([(byte)GameOpcode.WIZ_CLASS_CHANGE, ClassChangeProtocol.ResultSuccess]);
        Assert.Equal(ClassChangeProtocol.ResultSuccess, got);
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealClassChangeLayout_ExposesControls()
    {
        string? root = FindDataRoot();
        if (root == null)
            return;

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        string uif = table.ClassChange(1);
        Assert.False(string.IsNullOrEmpty(uif));
        string? path = resolver.Resolve(uif);
        Assert.NotNull(path);

        var layout = new N3UiBase();
        layout.LoadFromFile(path!);
        UiControl dialog = UiControlFactory.Build(layout);

        Assert.NotNull(dialog.GetChildById<UiButton>("Btn_Class"));
        Assert.NotNull(dialog.GetChildById<UiButton>("Btn_Ok"));

        var context = new GameContext(new FakeGameClient());
        context.Machine.SetActive(context.InGame);
        _ = new ClassChangeDialog(context, dialog);
    }

    private static string? FindDataRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Client", "Data");
            if (File.Exists(Path.Combine(candidate, "Data", "UIs_us.tbl")))
                return candidate;
        }

        return null;
    }
}
