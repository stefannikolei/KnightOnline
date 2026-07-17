using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
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
/// Sub-slice 9.6-2 pins for the skill tree: tab assignment from <c>NeedSkill % 10</c>, the
/// WIZ_SKILLPT_CHANGE learn packet and its rejections, and (corpus) the real .uif load.
/// Fully headless over a synthetic .uif tree, skill table and fake client.
/// </summary>
public class SkillTreeDialogTests
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

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiArea Area(int order) =>
        new() { Id = order.ToString(), AreaType = (int)UiAreaType.SkillTree, Region = Rect(order * 40, 0, order * 40 + 30, 30) };

    private static N3UiButton Button(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 40, 16) };

    /// <summary>Synthetic skilltree .uif: 6 SkillTree areas, paging/tab/learn buttons and labels.</summary>
    private static UiControl BuildRoot()
    {
        var root = new N3UiBase { Id = "skilltree", Region = Rect(0, 0, 400, 300) };

        for (int i = 0; i < SkillTreeDialog.SlotCount; i++)
            root.Children.Add(Area(i));

        root.Children.Add(Button("btn_close"));
        root.Children.Add(Button("btn_left"));
        root.Children.Add(Button("btn_right"));
        for (int i = 0; i < 8; i++)
            root.Children.Add(Button("btn_" + i));

        // Tab buttons (Karus berserker family + master).
        root.Children.Add(Button("btn_public"));
        root.Children.Add(Button("btn_berserker0"));
        root.Children.Add(Button("btn_berserker1"));
        root.Children.Add(Button("btn_berserker2"));
        root.Children.Add(Button("btn_master"));

        root.Children.Add(Str("string_skillpoint"));
        for (int i = 0; i <= 6; i++)
            root.Children.Add(Str("string_" + i));
        for (int i = 0; i < SkillTreeDialog.SlotCount; i++)
            root.Children.Add(Str("string_list_" + i));
        root.Children.Add(Str("string_page"));

        return UiControlFactory.Build(root);
    }

    // Skill table columns: only through validDist (col 28). TblCell guards trailing.
    private static readonly TblType[] SkillColumns = BuildSkillColumns();

    private static TblType[] BuildSkillColumns()
    {
        var cols = new TblType[29];
        cols[0] = TblType.Dword;                 // id
        cols[1] = cols[2] = cols[3] = TblType.String; // eng/name/desc
        for (int i = 4; i < 29; i++)
            cols[i] = TblType.Int;
        cols[19] = TblType.Dword; // needItem
        cols[20] = TblType.Dword; // exhaustItem
        cols[26] = TblType.Dword; // 1st table type
        cols[27] = TblType.Dword; // 2nd table type
        return cols;
    }

    // id, needLevel (col 15), needSkill (col 16).
    private static object[] SkillRowCells(uint id, int needLevel, int needSkill)
    {
        var cells = new object[29];
        cells[0] = id;
        cells[1] = "eng";
        cells[2] = "name" + id;
        cells[3] = "desc";
        for (int i = 4; i < 29; i++)
            cells[i] = 0;
        cells[15] = needLevel;
        cells[16] = needSkill;
        cells[19] = 0u;
        cells[20] = 0u;
        cells[26] = 0u;
        cells[27] = 0u;
        return cells;
    }

    private static SkillTableSet BuildSkills(IEnumerable<object[]> rows) =>
        new(BuildTable(SkillColumns, rows.ToArray()));

    private static N3TableFile BuildTable(IReadOnlyList<TblType> columns, IReadOnlyList<object[]> rows)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(columns.Count);
        foreach (TblType t in columns)
            w.Write((int)t);
        w.Write(rows.Count);
        foreach (object[] row in rows)
            for (int j = 0; j < columns.Count; j++)
            {
                switch (columns[j])
                {
                    case TblType.Int: w.Write(Convert.ToInt32(row[j])); break;
                    case TblType.Dword: w.Write(Convert.ToUInt32(row[j])); break;
                    case TblType.String:
                        var s = (string)row[j];
                        w.Write(s.Length);
                        w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                        break;
                    default: throw new InvalidOperationException();
                }
            }

        w.Flush();
        return N3TableFile.Load(ms.ToArray(), encrypted: false);
    }

    private sealed record Harness(SkillTreeDialog Dialog, UiControl Root, LocalPlayer Local, FakeGameClient Client, GameContext Context);

    private static Harness Build(SkillTableSet skills, short cls, byte level, byte[]? pools = null)
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        context.Machine.SetActive(context.InGame);
        LocalPlayer local = context.InGame.World.Local;
        local.Class = cls;
        local.Level = level;
        if (pools != null)
            Array.Copy(pools, local.Skills, Math.Min(pools.Length, local.Skills.Length));

        UiControl root = BuildRoot();
        var dialog = new SkillTreeDialog(context, root, skills, new IconDragState());
        dialog.Rebuild();
        return new Harness(dialog, root, local, client, context);
    }

    // ---- Tab assignment ----------------------------------------------------

    [Fact]
    public void Populate_AssignsTabsFromNeedSkillModulo()
    {
        // Class 106 (KA Guardian, master-capable). Its id block is 106000..106999.
        SkillTableSet skills = BuildSkills(
        [
            SkillRowCells(106001, needLevel: 1, needSkill: 0),  // % 10 == 0 → base tab 0
            SkillRowCells(106002, needLevel: 1, needSkill: 5),  // → tab 1
            SkillRowCells(106003, needLevel: 1, needSkill: 6),  // → tab 2
            SkillRowCells(106004, needLevel: 1, needSkill: 7),  // → tab 3
            SkillRowCells(106005, needLevel: 1, needSkill: 8),  // → tab 4 (master)
            SkillRowCells(106006, needLevel: 1, needSkill: 3),  // % 10 == 3 → not placed
        ]);

        // Mastery pools high so every tab skill is usable.
        Harness h = Build(skills, cls: 106, level: 80, pools: [0, 0, 0, 0, 0, 80, 80, 80, 80]);

        Assert.Equal(0, h.Dialog.FindPlacement(106001)!.Tab);
        Assert.Equal(1, h.Dialog.FindPlacement(106002)!.Tab);
        Assert.Equal(2, h.Dialog.FindPlacement(106003)!.Tab);
        Assert.Equal(3, h.Dialog.FindPlacement(106004)!.Tab);
        Assert.Equal(4, h.Dialog.FindPlacement(106005)!.Tab);
        Assert.Null(h.Dialog.FindPlacement(106006)); // unmapped modulo not placed
        Assert.True(h.Dialog.HasSkill(106001));
        Assert.False(h.Dialog.HasSkill(999999));
    }

    [Fact]
    public void Populate_ExcludesUsableItemIdsAndForeignClassBlocks()
    {
        SkillTableSet skills = BuildSkills(
        [
            SkillRowCells(106001, 1, 0),   // in block
            SkillRowCells(107001, 1, 0),   // different class block — excluded
            SkillRowCells(450000, 1, 0),   // usable-item id — excluded even though >= would collide
        ]);

        Harness h = Build(skills, cls: 106, level: 80);
        Assert.True(h.Dialog.HasSkill(106001));
        Assert.False(h.Dialog.HasSkill(107001));
        Assert.False(h.Dialog.HasSkill(450000));
    }

    [Fact]
    public void Populate_UsabilityFromLevelAndMastery()
    {
        SkillTableSet skills = BuildSkills(
        [
            SkillRowCells(106001, needLevel: 50, needSkill: 0),  // base tab — needs level 50
            SkillRowCells(106002, needLevel: 30, needSkill: 5),  // tab1 — needs mastery 30
        ]);

        // Level 40 (< 50 → base skill locked), tab1 mastery 30 (== 30 → unlocked).
        Harness h = Build(skills, cls: 106, level: 40, pools: [0, 0, 0, 0, 0, 30, 0, 0, 0]);

        SkillTreeDialog.SkillPlacement baseSkill = h.Dialog.FindPlacement(106001)!;
        SkillTreeDialog.SkillPlacement specSkill = h.Dialog.FindPlacement(106002)!;
        Assert.False(baseSkill.Usable);
        Assert.True(baseSkill.Icon.SkillDisabled);
        Assert.Equal(@"UI\skillicon_enigma.dxt", baseSkill.Icon.IconTexture);

        Assert.True(specSkill.Usable);
        Assert.False(specSkill.Icon.SkillDisabled);
        Assert.Equal(@"UI\skillicon_02_1060.dxt", specSkill.Icon.IconTexture);
    }

    // ---- Learn packet + rejections -----------------------------------------

    [Fact]
    public void Learn_SpecTab_SendsSkillPointPacketAndBumpsPool()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(106001, 1, 5)]);
        Harness h = Build(skills, cls: 106, level: 60, pools: [3, 0, 0, 0, 0, 10, 0, 0, 0]);

        bool ok = h.Dialog.Learn(5); // first specialization tab
        Assert.True(ok);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_SKILLPT_CHANGE, p[0]);
        Assert.Equal(5, p[1]);
        Assert.Equal(2, p.Length);

        Assert.Equal(2, h.Local.Skills[0]);  // unspent decremented
        Assert.Equal(11, h.Local.Skills[5]); // tab1 pool bumped
    }

    [Fact]
    public void Learn_NoUnspentPoints_Rejected()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(106001, 1, 5)]);
        Harness h = Build(skills, cls: 106, level: 60, pools: [0, 0, 0, 0, 0, 10, 0, 0, 0]);
        Assert.False(h.Dialog.Learn(5));
        Assert.Empty(h.Client.Sent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Learn_BasePools_Rejected(int value)
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(106001, 1, 0)]);
        Harness h = Build(skills, cls: 106, level: 60, pools: [5, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.False(h.Dialog.Learn(value));
        Assert.Empty(h.Client.Sent);
    }

    [Fact]
    public void Learn_SpecTab_WhileBaseClass_Rejected()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(101001, 1, 5)]);
        Harness h = Build(skills, cls: 101, level: 60, pools: [5, 0, 0, 0, 0, 0, 0, 0, 0]); // KA Warrior (base)
        Assert.False(h.Dialog.Learn(5));
        Assert.Empty(h.Client.Sent);
    }

    [Fact]
    public void Learn_MasterTab_NonMasterClass_Rejected()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(105001, 1, 8)]);
        Harness h = Build(skills, cls: 105, level: 60, pools: [5, 0, 0, 0, 0, 0, 0, 0, 0]); // KA Berserker (1st promo)
        Assert.False(h.Dialog.Learn(8)); // master tab needs a 2nd-promotion class
        Assert.Empty(h.Client.Sent);
    }

    [Fact]
    public void Learn_MasterTab_MasterClass_Allowed()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(106001, 1, 8)]);
        Harness h = Build(skills, cls: 106, level: 60, pools: [5, 0, 0, 0, 0, 0, 0, 0, 0]); // KA Guardian (master)
        Assert.True(h.Dialog.Learn(8));
        Assert.Equal(8, h.Client.Last[1]);
    }

    [Fact]
    public void Learn_PoolAtLevelCap_Rejected()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(106001, 1, 5)]);
        // tab1 pool already == level (30) → cannot raise.
        Harness h = Build(skills, cls: 106, level: 30, pools: [5, 0, 0, 0, 0, 30, 0, 0, 0]);
        Assert.False(h.Dialog.Learn(5));
        Assert.Empty(h.Client.Sent);
    }

    [Fact]
    public void ButtonClick_LearnButton_RoutesToLearn()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(106001, 1, 5)]);
        Harness h = Build(skills, cls: 106, level: 60, pools: [3, 0, 0, 0, 0, 10, 0, 0, 0]);

        UiButton btn5 = h.Root.GetChildById<UiButton>("btn_4")!; // btn_4 → PointPushUpButton(5)
        h.Root.ReceiveMessage(btn5, UiMsg.ButtonClick);

        Assert.Equal((byte)GameOpcode.WIZ_SKILLPT_CHANGE, h.Client.Last[0]);
        Assert.Equal(5, h.Client.Last[1]);
    }

    [Fact]
    public void TabButton_SwitchesCurrentTab()
    {
        SkillTableSet skills = BuildSkills([SkillRowCells(106001, 1, 5)]);
        Harness h = Build(skills, cls: 106, level: 60, pools: [0, 0, 0, 0, 0, 80, 0, 0, 0]);

        Assert.Equal(0, h.Dialog.CurrentTab);
        h.Root.ReceiveMessage(h.Root.GetChildById<UiButton>("btn_berserker0")!, UiMsg.ButtonClick);
        Assert.Equal(1, h.Dialog.CurrentTab);
    }

    // ---- Corpus ------------------------------------------------------------

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealSkillTreeLayout_ExposesAreasAndControls()
    {
        string? root = FindDataRoot();
        if (root == null)
            return;

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        string uif = table.SkillTree(1);
        string? path = resolver.Resolve(uif);
        Assert.NotNull(path);

        var layout = new N3UiBase();
        layout.LoadFromFile(path!);
        UiControl dialog = UiControlFactory.Build(layout);

        Assert.NotNull(dialog.GetChildById("btn_close"));
        Assert.NotNull(dialog.GetChildById<UiStringControl>("string_skillpoint"));
        Assert.NotNull(dialog.GetChildAreaByOrder(UiAreaType.SkillTree, 0));

        string? skillPath = resolver.Resolve("Data\\skill_magic_main_us.tbl");
        if (skillPath == null)
            return;

        var context = new GameContext(new FakeGameClient());
        context.Machine.SetActive(context.InGame);
        var dlg = new SkillTreeDialog(context, dialog, SkillTableSet.LoadFromFile(skillPath), new IconDragState());
        Assert.NotNull(dlg.Root);
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
