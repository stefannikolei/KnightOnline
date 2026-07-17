using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.9 pins: the QuestMenu (select → byte-exact <c>[0x55,index]</c>) and QuestTalk (paging
/// advance/wrap/hide) dialog controllers driven headlessly against synthetic UI trees and a fake client.
/// </summary>
public class QuestDialogTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];
        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());
        public void Connect(string host, int port) { }
        public bool CryptionEnabled => true;
        public void EnableCryption(ulong publicKey) { }
        public byte[] Last => Sent[^1];
    }

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiButton Btn(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20), ClickRect = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 60, 16) };

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    // ---- QuestMenu ---------------------------------------------------------

    private static (QuestMenuDialog Dialog, UiControl Root, FakeGameClient Client) BuildMenu()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var node = new N3UiBase { Id = "questmenu", Region = Rect(0, 0, 300, 400) };
        node.Children.Add(Str("Text_Title"));
        node.Children.Add(Str("Text_Npcname"));
        node.Children.Add(Str("Text_Menu")); // the sample row cloned per menu entry
        node.Children.Add(Btn("btn_close"));
        UiControl root = UiControlFactory.Build(node);

        var dialog = new QuestMenuDialog(context, root) { TextResolver = id => $"text{id}" };
        return (dialog, root, client);
    }

    [Fact]
    public void QuestMenu_Open_ResolvesTitleAndMenuRows()
    {
        (QuestMenuDialog dialog, UiControl root, _) = BuildMenu();

        dialog.Open(new QuestMenuData(77, 5, [10u, 20u, 30u]));

        Assert.True(root.Visible);
        Assert.Equal((short)77, dialog.NpcId);
        Assert.Equal("text5", root.GetChildById<UiStringControl>("Text_Title")!.Text);
        Assert.Equal(["text10", "text20", "text30"], dialog.MenuTexts);
        Assert.Equal(3, dialog.MenuRows.Count);
    }

    [Fact]
    public void QuestMenu_EmptyMenu_StaysHidden()
    {
        (QuestMenuDialog dialog, UiControl root, _) = BuildMenu();
        dialog.Open(new QuestMenuData(1, 2, []));
        Assert.False(root.Visible);
        Assert.Empty(dialog.MenuTexts);
    }

    [Fact]
    public void QuestMenu_RowLClick_SendsSelectMenuAndHides()
    {
        (QuestMenuDialog dialog, UiControl root, FakeGameClient client) = BuildMenu();
        dialog.Open(new QuestMenuData(1, 2, [10u, 20u, 30u]));

        Msg(root, dialog.MenuRows[1], UiMsg.StringLClick);

        Assert.Equal([(byte)GameOpcode.WIZ_SELECT_MSG, 1], client.Last);
        Assert.False(root.Visible);
    }

    [Fact]
    public void QuestMenu_SelectMenu_ReturnsPacketOrNullForOutOfRange()
    {
        (QuestMenuDialog dialog, _, FakeGameClient client) = BuildMenu();
        dialog.Open(new QuestMenuData(1, 2, [10u, 20u]));

        byte[]? ok = dialog.SelectMenu(0);
        Assert.Equal([(byte)GameOpcode.WIZ_SELECT_MSG, 0], ok);
        Assert.Equal(ok, client.Last);

        Assert.Null(dialog.SelectMenu(5));
    }

    // ---- QuestTalk ---------------------------------------------------------

    private static (QuestTalkDialog Dialog, UiControl Root, UiStringControl Text) BuildTalk()
    {
        var context = new GameContext(new FakeGameClient());

        var node = new N3UiBase { Id = "questtalk", Region = Rect(0, 0, 300, 200) };
        node.Children.Add(Str("Text_Talk"));
        node.Children.Add(Btn("btn_Ok_center"));
        node.Children.Add(Btn("btn_close"));
        UiControl root = UiControlFactory.Build(node);

        var dialog = new QuestTalkDialog(context, root) { TextResolver = id => $"page{id}" };
        return (dialog, root, root.GetChildById<UiStringControl>("Text_Talk")!);
    }

    [Fact]
    public void QuestTalk_Open_ShowsFirstPage()
    {
        (QuestTalkDialog dialog, UiControl root, UiStringControl text) = BuildTalk();
        dialog.Open(new QuestTalkData([100u, 200u]));

        Assert.True(root.Visible);
        Assert.Equal(2, dialog.PageCount);
        Assert.Equal(0, dialog.CurrentPage);
        Assert.Equal("page100", text.Text);
    }

    [Fact]
    public void QuestTalk_OkButton_AdvancesThenWrapsAndHides()
    {
        (QuestTalkDialog dialog, UiControl root, UiStringControl text) = BuildTalk();
        dialog.Open(new QuestTalkData([100u, 200u]));

        UiButton ok = root.GetChildById<UiButton>("btn_Ok_center")!;
        Msg(root, ok, UiMsg.ButtonClick);
        Assert.Equal(1, dialog.CurrentPage);
        Assert.Equal("page200", text.Text);
        Assert.True(root.Visible);

        // Past the last page: reset to 0 and hide.
        Msg(root, ok, UiMsg.ButtonClick);
        Assert.Equal(0, dialog.CurrentPage);
        Assert.False(root.Visible);
    }

    [Fact]
    public void QuestTalk_CloseButton_Hides()
    {
        (QuestTalkDialog dialog, UiControl root, _) = BuildTalk();
        dialog.Open(new QuestTalkData([100u]));
        Msg(root, root.GetChildById<UiButton>("btn_close")!, UiMsg.ButtonClick);
        Assert.False(root.Visible);
    }
}
