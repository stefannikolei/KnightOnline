using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-9.7 corpus checks: the party/force, clan-operation and character-sheet .uif layouts
/// named by the real UIs_us.tbl load and expose the control IDs the 9.7 controllers bind to.
/// Skipped when the asset corpus isn't present (e.g. CI).
/// </summary>
[Trait("Category", "Corpus")]
public class PartyClanCorpusTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public bool CryptionEnabled => true;

        public void Send(ReadOnlySpan<byte> payload) { }

        public void Connect(string host, int port) { }

        public void EnableCryption(ulong publicKey) { }
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
    public void RealPartyClanLayouts_ExposeTheControlsTheDialogsBindTo()
    {
        string? root = FindDataRoot();
        if (root == null)
            return; // corpus not available

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));
        var context = new GameContext(new FakeGameClient());

        // Party / force window — member slot 0 name + HP gauge (the name is a plain N3UIStatic).
        UiControl? party = LoadDialog(resolver, table.PartyOrForce(1));
        Assert.NotNull(party);
        Assert.NotNull(party!.GetChildById("static_name_0"));
        Assert.NotNull(party.GetChildById("progress_hp_0"));
        Assert.NotNull(party.GetChildById("Area_0"));
        _ = new PartyOrForceDialog(context, party);

        // Knights operation window — the list + create/join buttons. This .uif is absent from
        // some corpora; only assert its ids when it loads.
        UiControl? knightsOp = LoadDialog(resolver, table.KnightsOperation(1));
        if (knightsOp != null)
        {
            Assert.NotNull(knightsOp.GetChildById<UiListControl>("List_Knights"));
            Assert.NotNull(knightsOp.GetChildById<UiButton>("Btn_Create"));
            Assert.NotNull(knightsOp.GetChildById<UiButton>("Btn_Join"));
            _ = new KnightsOperationDialog(context, knightsOp);
        }

        // Clan-name popup.
        UiControl? createClan = LoadDialog(resolver, table.InputClanName(1));
        Assert.NotNull(createClan);
        Assert.NotNull(createClan!.GetChildById<UiEditControl>("Edit_Clan"));
        Assert.NotNull(createClan.GetChildById<UiButton>("btn_yes"));
        _ = new CreateClanDialog(context, createClan);

        // Character sheet: the status page (Text_HP + a stat-up button) and clan page.
        UiControl? various = LoadDialog(resolver, table.Various(1));
        UiControl? state = LoadDialog(resolver, table.State(1));
        UiControl? clan = LoadDialog(resolver, table.Knights(1));
        Assert.NotNull(various);
        Assert.NotNull(state);
        Assert.NotNull(state!.GetChildById<UiStringControl>("Text_HP"));
        Assert.NotNull(state.GetChildById<UiButton>("Btn_Strength"));
        Assert.NotNull(clan);
        Assert.NotNull(clan!.GetChildById<UiListControl>("List_clan_ChrID"));
        _ = new VariousDialog(context, various!, state, clan);
    }
}
