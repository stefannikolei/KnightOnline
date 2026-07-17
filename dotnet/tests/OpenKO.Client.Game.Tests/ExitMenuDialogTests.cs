using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.9 pins: the in-game exit menu (CUIExitMenu) — the char-select / option / exit button
/// events and the cancel-hides behaviour. Fully headless.
/// </summary>
public class ExitMenuDialogTests
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

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    private static (ExitMenuDialog Dialog, UiControl Root) Build()
    {
        var context = new GameContext(new FakeGameClient());
        var node = new N3UiBase { Id = "exitmenu", Region = Rect(0, 0, 200, 200) };
        foreach (string id in (string[])["btn_chr", "btn_option", "btn_exit", "btn_cancel"])
            node.Children.Add(Btn(id));
        UiControl root = UiControlFactory.Build(node);
        return (new ExitMenuDialog(context, root), root);
    }

    [Fact]
    public void Toggle_ShowsThenHides()
    {
        (ExitMenuDialog dialog, UiControl root) = Build();
        Assert.False(root.Visible);
        dialog.Toggle();
        Assert.True(root.Visible);
        dialog.Toggle();
        Assert.False(root.Visible);
    }

    [Fact]
    public void Buttons_RaiseTheirEventsAndHide()
    {
        (ExitMenuDialog dialog, UiControl root) = Build();
        bool chr = false, option = false, exit = false;
        dialog.CharSelectRequested += () => chr = true;
        dialog.OptionRequested += () => option = true;
        dialog.ExitRequested += () => exit = true;

        dialog.Toggle();
        Msg(root, root.GetChildById<UiButton>("btn_chr")!, UiMsg.ButtonClick);
        Assert.True(chr);
        Assert.False(root.Visible);

        dialog.Toggle();
        Msg(root, root.GetChildById<UiButton>("btn_option")!, UiMsg.ButtonClick);
        Assert.True(option);

        dialog.Toggle();
        Msg(root, root.GetChildById<UiButton>("btn_exit")!, UiMsg.ButtonClick);
        Assert.True(exit);
    }

    [Fact]
    public void CancelButton_HidesWithoutEvents()
    {
        (ExitMenuDialog dialog, UiControl root) = Build();
        bool any = false;
        dialog.CharSelectRequested += () => any = true;
        dialog.OptionRequested += () => any = true;
        dialog.ExitRequested += () => any = true;

        dialog.Toggle();
        Msg(root, root.GetChildById<UiButton>("btn_cancel")!, UiMsg.ButtonClick);

        Assert.False(root.Visible);
        Assert.False(any);
    }
}
