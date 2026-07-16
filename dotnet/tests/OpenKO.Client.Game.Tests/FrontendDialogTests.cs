using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-9.3 pins: the frontend dialog controllers (login/nation/char-select/
/// char-create/message-box) drive the state machine through clicks — fully
/// headless over synthetic .uif trees and a fake client.
/// </summary>
public class FrontendDialogTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public List<(string Host, int Port)> Connects { get; } = [];

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) => Connects.Add((host, port));

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

    /// <summary>Click = post UIMSG_BUTTON_CLICK like a completed press/release.</summary>
    private static void Click(UiControl button) => button.Parent!.ReceiveMessage(button, UiMsg.ButtonClick);

    private static UiControl BuildLoginTree(
        out UiButton btnOk, out UiEditControl editId, out UiEditControl editPw,
        out UiButton btnNotice, out UiStringControl server1, out UiButton btnConnect)
    {
        btnOk = Button("btn_ok");
        editId = Edit("Edit_ID");
        editPw = Edit("Edit_PW");
        UiControl login = Group("Group_LogIn", btnOk, Button("btn_cancel"), editId, editPw);

        btnNotice = Button("btn_ok");
        UiControl notice1 = Group("Group_Notice_1", btnNotice, Str("text_notice_name_01"), Str("text_notice_01"));

        server1 = Str("List_Server");
        UiControl serverGroup1 = Group("server_1", server1);
        btnConnect = Button("Btn_Connect");
        UiControl serverList = Group("Group_ServerList_01", serverGroup1, Group("img_arrow1"), btnConnect);

        return Group("ROOT", login, notice1, Group("Group_Notice_2"), Group("Group_Notice_3"), serverList);
    }

    [Fact]
    public void LoginDialog_SubmitsCredentialsFromEdits()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiControl root = BuildLoginTree(out UiButton btnOk, out UiEditControl editId, out UiEditControl editPw,
            out _, out _, out _);
        var dialog = new LoginDialog(context, root);

        editId.Text = "tester";
        editPw.Text = "secret";
        Click(btnOk);

        Assert.Equal((byte)LoginOpcode.LS_LOGIN_REQ, client.LastOpcode);
        Assert.Equal("tester", context.Account);
        Assert.Equal("secret", context.Password);
    }

    [Fact]
    public void LoginDialog_NoticeOkOpensServerListAndConnectTargetsSelectedServer()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiControl root = BuildLoginTree(out _, out _, out _,
            out UiButton btnNotice, out UiStringControl server1, out UiButton btnConnect);
        var dialog = new LoginDialog(context, root);

        dialog.SetServers([new ServerListEntry("10.0.0.7", "Ares", 42)]);
        Click(btnNotice); // closes notices, opens the list

        Assert.True(root.GetChildById("Group_ServerList_01")!.Visible);
        Assert.Equal("Ares", server1.Text);

        Click(btnConnect);
        Assert.Equal(("10.0.0.7", GameContext.GameServerPort), Assert.Single(client.Connects));
        Assert.Equal((byte)GameOpcode.WIZ_VERSION_CHECK, client.LastOpcode);
    }

    [Fact]
    public void LoginDialog_ParsesNewsBlocks()
    {
        string content = "Titel 1#\0\nText 1\0\n#\0\n\0\nTitel 2#\0\nText 2\0\n#\0\n\0\n";
        List<(string Title, string Message)> blocks = LoginDialog.ParseNewsBlocks(content);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(("Titel 1", "Text 1"), blocks[0]);
        Assert.Equal(("Titel 2", "Text 2"), blocks[1]);
    }

    [Fact]
    public void NationSelectDialog_SendsSelectedNation()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiButton karus = Button("btn_karus_selection");
        UiControl root = Group("ROOT", karus, Button("btn_elmo_selection"), Button("btn_back"));
        _ = new NationSelectDialog(context, root);

        Click(karus);

        Assert.Equal((byte)GameOpcode.WIZ_SEL_NATION, client.LastOpcode);
        Assert.Equal(NationSelectState.Karus, client.Sent[^1][1]);
    }

    [Fact]
    public void CharSelectDialog_RotatesAndStartsOccupiedSlot()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client) { Account = "tester" };
        var equipment = new List<EquipmentSlot>();
        context.Characters =
        [
            new CharacterSlot("Hero", 1, 101, 60, 0, 1, 21, equipment),
            new CharacterSlot("", 0, 0, 0, 0, 0, 0, equipment),
            new CharacterSlot("Alt", 1, 101, 10, 0, 1, 21, equipment),
        ];

        UiButton left = Button("bt_left");
        UiButton right = Button("bt_right");
        UiStringControl info = Str("text00");
        UiControl root = Group("ROOT", left, right, Button("bt_exit"), Button("bt_delete"), Button("bt_back"), info);
        var dialog = new CharSelectDialog(context, root);

        Assert.Contains("Hero", info.Text);

        Click(right); // slot 1 (empty)
        Assert.Equal(1, dialog.SelectedIndex);
        int createRequested = -1;
        dialog.CreateRequested += i => createRequested = i;
        dialog.StartSelected();
        Assert.Equal(1, createRequested);
        Assert.Equal(1, context.CharCreate.SlotIndex);

        Click(right); // slot 2 (occupied)
        dialog.StartSelected();
        Assert.Equal((byte)GameOpcode.WIZ_SEL_CHAR, client.LastOpcode);
    }

    [Fact]
    public void CharCreateDialog_SpendsBonusPointsAndSendsNewChar()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client) { Nation = NationSelectState.Karus };
        context.CharCreate.SlotIndex = 1;

        UiButton create = Button("btn_create");
        UiEditControl name = Edit("edit_name");
        UiButton strRight = Button("btn_str_right");
        UiControl root = Group("ROOT", create, Button("btn_cancel"), name, strRight,
            Button("btn_race_ka_at"), Button("btn_class_warrior"));
        var dialog = new CharCreateDialog(context, root);

        Assert.Equal(CharCreateDialog.RaceKaArkTuarek, dialog.Race);
        Assert.Equal(CharCreateDialog.ClassKaWarrior, dialog.Class);

        // No table given: stats seed at the server floor (50), no bonus pool.
        Assert.False(dialog.IncreaseStat(0));

        name.Text = "Newbie";
        Click(create);

        Assert.Equal((byte)GameOpcode.WIZ_NEW_CHAR, client.LastOpcode);
        byte[] packet = client.Sent[^1];
        Assert.Equal(1, packet[1]); // slot index
    }

    [Fact]
    public void MessageBoxDialog_OkClosesAndReportsResult()
    {
        UiButton ok = Button("Btn_OK");
        UiControl root = Group("ROOT", ok, Button("Btn_Yes"), Button("Btn_No"), Button("Btn_Cancel"),
            Str("Text_Message"), Str("Text_Title"));
        var box = new MessageBoxDialog(root);

        Assert.False(box.IsOpen);
        MessageBoxResult? result = null;
        box.Show("Are you sure?", style: MessageBoxStyle.Ok, onResult: r => result = r);
        Assert.True(box.IsOpen);

        Click(ok);

        Assert.False(box.IsOpen);
        Assert.Equal(MessageBoxResult.Ok, result);
    }
}
