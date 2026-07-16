using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-9.4 pins: the in-game HUD controllers (state bar / target bar / chat / message
/// window / command bar / dead dialog) drive real APIs through clicks and Enter — fully
/// headless over synthetic .uif trees and a fake client.
/// </summary>
public class HudDialogTests
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

        public byte LastOpcode => Sent.Count > 0 ? Sent[^1][0] : (byte)0;
    }

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static UiControl Group(string id, params UiControl[] children)
    {
        var g = new UiControl(new N3UiBase { Id = id, Region = Rect(0, 0, 400, 300) });
        foreach (UiControl c in children)
            g.AddChild(c);
        return g;
    }

    private static UiButton Button(string id) => new(new N3UiButton
    {
        Id = id,
        Style = UiStyle.BtnNormal,
        Region = Rect(0, 0, 50, 20),
        ClickRect = Rect(0, 0, 50, 20),
    });

    private static UiEditControl Edit(string id) => new(new N3UiEdit { Id = id, Region = Rect(0, 0, 100, 20) });

    private static UiStringControl Str(string id) => new(new N3UiString { Id = id, Region = Rect(0, 0, 100, 16) });

    private static UiControl Progress(string id) => new(new N3UiProgress { Id = id, Region = Rect(0, 0, 100, 12) });

    private static UiScrollBarControl Scroll(string id) => new(new N3UiScrollBar
    {
        Id = id,
        Style = UiStyle.ScrollBarVertical,
        Region = Rect(0, 0, 12, 100),
    });

    private static void Click(UiControl button) => button.Parent!.ReceiveMessage(button, UiMsg.ButtonClick);

    private static void ClickString(UiControl str) => str.Parent!.ReceiveMessage(str, UiMsg.StringLClick);

    // ---- StateBar ----------------------------------------------------------

    [Fact]
    public void StateBarDialog_FormatsTextAndRecordsProgress()
    {
        var context = new GameContext(new FakeGameClient());
        UiControl hp = Progress("Progress_HP");
        UiControl msp = Progress("Progress_MSP");
        UiStringControl textHp = Str("Text_HP");
        UiStringControl textMsp = Str("Text_MSP");
        UiStringControl textExp = Str("Text_ExpP");
        UiStringControl textPos = Str("Text_Position");
        UiControl miniMap = Group("Group_MiniMap");
        UiControl root = Group("ROOT", hp, msp, Progress("Progress_ExpC"), Progress("Progress_ExpP"),
            textHp, textMsp, textExp, textPos, miniMap);

        var dialog = new StateBarDialog(context, root);

        dialog.UpdateHp(50, 100);
        Assert.Equal("50 / 100", textHp.Text);
        Assert.Equal(50, dialog.HpPercent);
        Assert.Equal(50, dialog.GetProgress(hp));

        dialog.UpdateMp(30, 60);
        Assert.Equal("30 / 60", textMsp.Text);
        Assert.Equal(50, dialog.MpPercent);

        dialog.UpdateExp(50, 100);
        Assert.Equal("50.00 %", textExp.Text);
        Assert.Equal(50, dialog.ExpPercent);

        dialog.UpdatePosition(100.0f, 250.0f);
        Assert.Equal("100.0, 250.0", textPos.Text);

        Assert.False(miniMap.Visible); // deferred to a later slice
    }

    [Fact]
    public void StateBarDialog_FillPopulatesEveryBarFromLocalPlayer()
    {
        var context = new GameContext(new FakeGameClient());
        UiStringControl textHp = Str("Text_HP");
        UiStringControl textMsp = Str("Text_MSP");
        UiStringControl textExp = Str("Text_ExpP");
        UiControl root = Group("ROOT", Progress("Progress_HP"), Progress("Progress_MSP"),
            Progress("Progress_ExpC"), Progress("Progress_ExpP"), textHp, textMsp, textExp, Str("Text_Position"));

        var dialog = new StateBarDialog(context, root);
        var player = new LocalPlayer { Hp = 80, MaxHp = 100, Mp = 20, MaxMp = 40, Exp = 25, MaxExp = 100 };

        dialog.Fill(player);

        Assert.Equal("80 / 100", textHp.Text);
        Assert.Equal("20 / 40", textMsp.Text);
        Assert.Equal("25.00 %", textExp.Text);
        Assert.Equal(80, dialog.HpPercent);
    }

    // ---- TargetBar ---------------------------------------------------------

    [Fact]
    public void TargetBarDialog_ShowsHidesAndTracksHp()
    {
        var context = new GameContext(new FakeGameClient());
        UiStringControl text = Str("text_target");
        UiControl root = Group("ROOT", Progress("pro_target"), text);

        var dialog = new TargetBarDialog(context, root);
        Assert.False(root.Visible);

        dialog.SetTarget("Goblin");
        Assert.True(root.Visible);
        Assert.Equal("Goblin", text.Text);

        dialog.Bind(context.InGame);
        context.InGame.TargetHpReceived!.Invoke(new TargetHpUpdate(1, 0, 100, 40, 10));
        Assert.Equal(40, dialog.HpPercent);

        dialog.Clear();
        Assert.False(root.Visible);
    }

    // ---- Chat --------------------------------------------------------------

    private static (ChatDialog Dialog, UiEditControl Edit, FakeGameClient Client, UiControl Root) BuildChat()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiEditControl edit = Edit("edit0");
        UiControl root = Group("ROOT", edit, Button("btn_normal"), Button("btn_private"),
            Button("btn_party_force"), Button("btn_knights"), Button("btn_shout"), Button("btn_off"), Scroll("scroll"));
        var dialog = new ChatDialog(context, root);
        return (dialog, edit, client, root);
    }

    [Theory]
    [InlineData("hello", (byte)ChatChannel.Normal, "hello")]
    [InlineData("#party line", (byte)ChatChannel.Party, "party line")]
    [InlineData("$clan line", (byte)ChatChannel.Clan, "clan line")]
    [InlineData("!shout", (byte)ChatChannel.Shout, "shout")]
    public void ChatDialog_RoutesPrefixesToChannels(string input, byte expectedChannel, string expectedText)
    {
        (ChatDialog dialog, UiEditControl edit, FakeGameClient client, _) = BuildChat();

        edit.Text = input;
        edit.SubmitReturn();

        Assert.Equal((byte)GameOpcode.WIZ_CHAT, client.LastOpcode);
        byte[] packet = client.Sent[^1];
        Assert.Equal(expectedChannel, packet[1]);
        // The text string2 body starts after opcode + type + 2-byte length.
        string sentText = System.Text.Encoding.Latin1.GetString(packet, 4, packet.Length - 4);
        Assert.Equal(expectedText, sentText);
        Assert.Equal(string.Empty, edit.Text); // edit cleared
    }

    [Fact]
    public void ChatDialog_WhisperParsesTargetAndSendsPrivate()
    {
        (ChatDialog dialog, UiEditControl edit, FakeGameClient client, _) = BuildChat();

        edit.Text = "@Bob hi there";
        edit.SubmitReturn();

        Assert.Equal((byte)GameOpcode.WIZ_CHAT, client.LastOpcode);
        Assert.Equal((byte)ChatChannel.Private, client.Sent[^1][1]);
        Assert.Equal("Bob", dialog.LastWhisperTarget);
    }

    [Fact]
    public void ChatDialog_CommandAndEmptySendNothing()
    {
        (ChatDialog dialog, UiEditControl edit, FakeGameClient client, _) = BuildChat();

        edit.Text = "/help";
        edit.SubmitReturn();
        Assert.Empty(client.Sent);

        edit.Text = string.Empty;
        edit.SubmitReturn();
        Assert.Empty(client.Sent);
    }

    [Fact]
    public void ChatDialog_ChannelButtonChangesActiveChannel()
    {
        (ChatDialog dialog, UiEditControl edit, FakeGameClient client, UiControl root) = BuildChat();

        Click(root.GetChildById<UiButton>("btn_shout")!);
        Assert.Equal(ChatChannel.Shout, dialog.Channel);

        edit.Text = "yell";
        edit.SubmitReturn();
        Assert.Equal((byte)ChatChannel.Shout, client.Sent[^1][1]);
    }

    [Fact]
    public void ChatDialog_FoldAndIncomingMessage()
    {
        (ChatDialog dialog, _, _, UiControl root) = BuildChat();

        bool folded = false;
        dialog.FoldRequested += () => folded = true;
        Click(root.GetChildById<UiButton>("btn_off")!);
        Assert.True(folded);

        dialog.AddChatMsg(new ChatMessage(1, 1, 5, "Hero", "hi"));
        Assert.Equal("Hero: hi", Assert.Single(dialog.Lines).Text);
    }

    // ---- MessageWnd --------------------------------------------------------

    [Fact]
    public void MessageWndDialog_AppendsAndFolds()
    {
        var context = new GameContext(new FakeGameClient());
        UiButton fold = Button("btn_off");
        UiControl root = Group("ROOT", Str("text_message"), Scroll("scroll"), fold);
        var dialog = new MessageWndDialog(context, root);

        dialog.AddMsg("system up");
        Assert.Equal("system up", Assert.Single(dialog.Lines).Text);

        bool folded = false;
        dialog.FoldRequested += () => folded = true;
        Click(fold);
        Assert.True(folded);
    }

    // ---- CmdBar ------------------------------------------------------------

    [Fact]
    public void CmdBarDialog_FiresButtonIdCommand()
    {
        var context = new GameContext(new FakeGameClient());
        UiControl[] buttons = [.. CmdBarDialog.ButtonIds.Select(Button)];
        UiControl root = Group("ROOT", buttons);
        var dialog = new CmdBarDialog(context, root);

        string? fired = null;
        dialog.Command += id => fired = id;

        Click(root.GetChildById<UiButton>("btn_inventory")!);
        Assert.Equal("btn_inventory", fired);

        Click(root.GetChildById<UiButton>("btn_map")!);
        Assert.Equal("btn_map", fired);
    }

    // ---- Dead --------------------------------------------------------------

    [Fact]
    public void DeadDialog_TownAndLifeStoneSendCorrectRevivalType()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiStringControl alive = Str("Text_Alive");
        UiStringControl town = Str("Text_Town");
        UiControl root = Group("ROOT", alive, town);
        var dialog = new DeadDialog(context, root);

        Assert.False(root.Visible);
        dialog.Show();
        Assert.True(root.Visible);

        byte lastType = 0;
        dialog.RevivalRequested += t => lastType = t;

        ClickString(town);
        Assert.Equal((byte)GameOpcode.WIZ_REGENE, client.LastOpcode);
        Assert.Equal(1, client.Sent[^1][1]);
        Assert.Equal(1, lastType);

        ClickString(alive);
        Assert.Equal((byte)GameOpcode.WIZ_REGENE, client.LastOpcode);
        Assert.Equal(2, client.Sent[^1][1]);
        Assert.Equal(2, lastType);
    }

    // ---- Protocol builders -------------------------------------------------

    [Fact]
    public void GameProtocol_BuildsRevivalAndTargetHpRequest()
    {
        Assert.Equal([(byte)GameOpcode.WIZ_REGENE, 1], GameProtocol.BuildRevival(1));

        byte[] req = GameProtocol.BuildTargetHpRequest(0x0102);
        Assert.Equal((byte)GameOpcode.WIZ_TARGET_HP, req[0]);
        Assert.Equal(0x02, req[1]); // little-endian short low byte
        Assert.Equal(0x01, req[2]); // high byte
        Assert.Equal(0x01, req[3]); // byUpdateImmediately
    }
}
