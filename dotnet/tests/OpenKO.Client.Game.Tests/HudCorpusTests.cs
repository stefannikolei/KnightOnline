using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-9.4 corpus checks: the in-game HUD .uif layouts named by the real UIs_us.tbl
/// load and expose the control IDs the controllers bind to. Skipped when the asset
/// corpus isn't present (e.g. CI).
/// </summary>
[Trait("Category", "Corpus")]
public class HudCorpusTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public bool CryptionEnabled { get; private set; }

        public void Send(ReadOnlySpan<byte> payload)
        {
        }

        public void Connect(string host, int port)
        {
        }

        public void EnableCryption(ulong publicKey) => CryptionEnabled = true;
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

    private static UiControl? LoadDialog(KoPathResolver resolver, string uifName)
    {
        if (string.IsNullOrEmpty(uifName))
            return null;
        string? path = resolver.Resolve(uifName);
        if (path == null)
            return null;
        var layout = new N3UiBase();
        layout.LoadFromFile(path);
        return UiControlFactory.Build(layout);
    }

    [Fact]
    public void RealHudLayouts_ExposeTheControlsTheDialogsBindTo()
    {
        string? root = FindDataRoot();
        if (root == null)
            return; // corpus not available

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));
        var context = new GameContext(new FakeGameClient());

        // State bar
        UiControl? stateBar = LoadDialog(resolver, table.StateBar(1));
        Assert.NotNull(stateBar);
        Assert.NotNull(stateBar!.GetChildById("Progress_HP"));
        Assert.NotNull(stateBar.GetChildById("Progress_MSP"));
        Assert.NotNull(stateBar.GetChildById<UiStringControl>("Text_HP"));
        Assert.NotNull(stateBar.GetChildById<UiStringControl>("Text_Position"));
        _ = new StateBarDialog(context, stateBar);

        // Target bar
        UiControl? targetBar = LoadDialog(resolver, table.TargetBar(1));
        Assert.NotNull(targetBar);
        Assert.NotNull(targetBar!.GetChildById("pro_target"));
        Assert.NotNull(targetBar.GetChildById<UiStringControl>("text_target"));
        _ = new TargetBarDialog(context, targetBar);

        // Chat
        UiControl? chat = LoadDialog(resolver, table.Chat(1));
        Assert.NotNull(chat);
        Assert.NotNull(chat!.GetChildById<UiEditControl>("edit0"));
        Assert.NotNull(chat.GetChildById("btn_normal"));
        Assert.NotNull(chat.GetChildById("btn_off"));
        _ = new ChatDialog(context, chat);

        // Message window
        UiControl? msg = LoadDialog(resolver, table.MsgOutput(1));
        Assert.NotNull(msg);
        Assert.NotNull(msg!.GetChildById<UiStringControl>("text_message"));
        Assert.NotNull(msg.GetChildById("btn_off"));
        _ = new MessageWndDialog(context, msg);

        // Command bar
        UiControl? cmd = LoadDialog(resolver, table.Cmd(1));
        Assert.NotNull(cmd);
        Assert.NotNull(cmd!.GetChildById<UiButton>("btn_inventory"));
        Assert.NotNull(cmd.GetChildById<UiButton>("btn_exit"));
        _ = new CmdBarDialog(context, cmd);

        // Dead
        UiControl? dead = LoadDialog(resolver, table.Dead(1));
        Assert.NotNull(dead);
        Assert.NotNull(dead!.GetChildById<UiStringControl>("Text_Alive"));
        Assert.NotNull(dead.GetChildById<UiStringControl>("Text_Town"));
        _ = new DeadDialog(context, dead);
    }
}
