using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-9.3 corpus checks: the login/nation/char-select .uif layouts named by the
/// real UIs_us.tbl load and expose the control IDs the controllers bind to. Skipped
/// when the asset corpus isn't present (e.g. CI).
/// </summary>
[Trait("Category", "Corpus")]
public class FrontendCorpusTests
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
        string? path = resolver.Resolve(uifName);
        if (path == null)
            return null;
        var layout = new N3UiBase();
        layout.LoadFromFile(path);
        return UiControlFactory.Build(layout);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RealLoginLayout_ExposesTheControlsTheDialogBindsTo(int nation)
    {
        string? root = FindDataRoot();
        if (root == null)
            return; // corpus not available

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        UiControl? dialog = LoadDialog(resolver, table.LoginIntro(nation));
        Assert.NotNull(dialog);

        UiControl? login = dialog!.GetChildById("Group_LogIn");
        Assert.NotNull(login);
        Assert.NotNull(login!.GetChildById<UiEditControl>("Edit_ID"));
        Assert.NotNull(login.GetChildById<UiEditControl>("Edit_PW"));
        Assert.NotNull(login.GetChildById<UiButton>("btn_ok"));

        UiControl? serverList = dialog.GetChildById("Group_ServerList_01");
        Assert.NotNull(serverList);
        Assert.NotNull(serverList!.GetChildById<UiButton>("Btn_Connect"));

        // The controller wires up without throwing on the real tree.
        var context = new GameContext(new FakeGameClient());
        _ = new LoginDialog(context, dialog);
    }

    [Fact]
    public void RealCharSelectAndNationLayouts_ExposeTheirButtons()
    {
        string? root = FindDataRoot();
        if (root == null)
            return;

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        UiControl? charSelect = LoadDialog(resolver, table.CharacterSelect(1));
        Assert.NotNull(charSelect);
        Assert.NotNull(charSelect!.GetChildById("bt_left"));
        Assert.NotNull(charSelect.GetChildById("bt_right"));
        Assert.NotNull(charSelect.GetChildById("bt_exit"));

        UiControl? nation = LoadDialog(resolver, table.NationSelect(1));
        if (nation != null)
        {
            Assert.NotNull(nation.GetChildById("btn_karus_selection"));
            Assert.NotNull(nation.GetChildById("btn_elmo_selection"));
        }
    }
}
