using System.Text;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.9 pins: the WIZ_NOTICE (0x2E) parse (String1 lines) and the CUINotice banner dialog —
/// Open joins the lines into Text_Notice and shows; Btn_Quit clears and hides. Fully headless.
/// </summary>
public class NoticeTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public void Send(ReadOnlySpan<byte> payload) { }
        public void Connect(string host, int port) { }
        public bool CryptionEnabled => true;
        public void EnableCryption(ulong publicKey) { }
    }

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiButton Btn(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20), ClickRect = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 200, 80) };

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    private static byte[] NoticePacket(params string[] lines)
    {
        var b = new List<byte> { (byte)GameOpcode.WIZ_NOTICE, (byte)lines.Length };
        foreach (string line in lines)
        {
            byte[] raw = Encoding.ASCII.GetBytes(line);
            b.Add((byte)raw.Length);
            b.AddRange(raw);
        }

        return b.ToArray();
    }

    [Fact]
    public void ParseNotice_ReadsAllLines()
    {
        IReadOnlyList<string> lines = NoticeProtocol.ParseNotice(NoticePacket("Line A", "Line B", "Line C"));
        Assert.Equal(["Line A", "Line B", "Line C"], lines);
    }

    private static (NoticeDialog Dialog, UiControl Root, UiStringControl Text) Build()
    {
        var context = new GameContext(new FakeGameClient());
        var node = new N3UiBase { Id = "notice", Region = Rect(0, 0, 300, 200) };
        node.Children.Add(Str("Text_Notice"));
        node.Children.Add(Btn("Btn_Quit"));
        UiControl root = UiControlFactory.Build(node);
        var dialog = new NoticeDialog(context, root);
        return (dialog, root, root.GetChildById<UiStringControl>("Text_Notice")!);
    }

    [Fact]
    public void Open_JoinsLinesAndShows()
    {
        (NoticeDialog dialog, UiControl root, UiStringControl text) = Build();
        dialog.Open(["First", "Second"]);

        Assert.True(root.Visible);
        Assert.Equal(["First", "Second"], dialog.Lines);
        Assert.Equal("First\nSecond", text.Text);
    }

    [Fact]
    public void QuitButton_ClearsAndHides()
    {
        (NoticeDialog dialog, UiControl root, UiStringControl text) = Build();
        dialog.Open(["First"]);
        Msg(root, root.GetChildById<UiButton>("Btn_Quit")!, UiMsg.ButtonClick);

        Assert.False(root.Visible);
        Assert.Equal(string.Empty, text.Text);
    }
}
