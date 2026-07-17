using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.6-3 pins: the MagicCastManager cast gate + wire layouts, the HotKeyDialog
/// slot/page/cast behaviour, and hotkey persistence. Headless over a synthetic .uif, skill table
/// and fake client; a Corpus fact loads the real hotkey layout.
/// </summary>
public class HotKeyDialogTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public bool CryptionEnabled { get; private set; }

        public void EnableCryption(ulong publicKey) => CryptionEnabled = true;

        public byte[] Last => Sent[^1];
    }

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiArea Area(int order) => new()
    {
        Id = order.ToString(),
        AreaType = (int)UiAreaType.SkillHotkey,
        Region = Rect(order * 40, 0, order * 40 + 30, 30),
    };

    private static N3UiButton Button(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 40, 16) };

    /// <summary>Synthetic hotkey .uif: 8 SkillHotkey areas, page buttons and count/tooltip strings.</summary>
    private static UiControl BuildRoot()
    {
        var root = new N3UiBase { Id = "hotkey", Region = Rect(0, 0, 400, 40) };
        for (int i = 0; i < HotKeyDialog.SlotCount; i++)
            root.Children.Add(Area(i));
        root.Children.Add(Button("btn_up"));
        root.Children.Add(Button("btn_down"));
        for (int i = 0; i < HotKeyDialog.SlotCount; i++)
            root.Children.Add(Str(i.ToString()));            // count strings "0".."7"
        for (int i = 0; i < HotKeyDialog.SlotCount; i++)
            root.Children.Add(Str((i + 10).ToString()));     // tooltip strings "10".."17"
        return UiControlFactory.Build(root);
    }

    // ---- Skill table plumbing (shared with the skill-tree tests' TBL shape) ----

    private static readonly TblType[] SkillColumns = BuildSkillColumns();

    private static TblType[] BuildSkillColumns()
    {
        var cols = new TblType[29];
        cols[0] = TblType.Dword;
        cols[1] = cols[2] = cols[3] = TblType.String;
        for (int i = 4; i < 29; i++)
            cols[i] = TblType.Int;
        cols[19] = TblType.Dword;
        cols[20] = TblType.Dword;
        cols[26] = TblType.Dword;
        cols[27] = TblType.Dword;
        return cols;
    }

    private sealed record SkillSpec(
        uint Id, int Target = 7, int NeedLevel = 1, int ExhaustMsp = 0, int ExhaustHp = 0,
        int CastTime = 0, int ReCastTime = 0, uint ExhaustItem = 0, uint First = 0, uint Second = 0);

    private static object[] SkillCells(SkillSpec s)
    {
        var cells = new object[29];
        cells[0] = s.Id;
        cells[1] = "eng";
        cells[2] = "name" + s.Id;
        cells[3] = "desc";
        for (int i = 4; i < 29; i++)
            cells[i] = 0;
        cells[14] = s.Target;      // target
        cells[15] = s.NeedLevel;   // needLevel
        cells[16] = 0;             // needSkill
        cells[17] = s.ExhaustMsp;  // exhaust MSP
        cells[18] = s.ExhaustHp;   // exhaust HP
        cells[19] = 0u;            // needItem
        cells[20] = s.ExhaustItem; // exhaust item
        cells[21] = s.CastTime;    // cast time
        cells[22] = s.ReCastTime;  // recast time
        cells[26] = s.First;       // 1st table type
        cells[27] = s.Second;      // 2nd table type
        return cells;
    }

    private static SkillTableSet BuildSkills(params SkillSpec[] specs)
    {
        object[][] rows = [.. specs.Select(SkillCells)];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(SkillColumns.Length);
        foreach (TblType t in SkillColumns)
            w.Write((int)t);
        w.Write(rows.Length);
        foreach (object[] row in rows)
            for (int j = 0; j < SkillColumns.Length; j++)
            {
                switch (SkillColumns[j])
                {
                    case TblType.Int: w.Write(Convert.ToInt32(row[j])); break;
                    case TblType.Dword: w.Write(Convert.ToUInt32(row[j])); break;
                    case TblType.String:
                        var s = (string)row[j];
                        w.Write(s.Length);
                        w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                        break;
                }
            }

        w.Flush();
        return new SkillTableSet(N3TableFile.Load(ms.ToArray(), encrypted: false));
    }

    private sealed record Harness(HotKeyDialog Dialog, UiControl Root, LocalPlayer Local, FakeGameClient Client, GameContext Context, InMemoryHotkeyStore Store);

    private static Harness Build(
        SkillTableSet skills, IEnumerable<uint>? known = null, byte level = 60, short mp = 1000, short hp = 1000)
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        context.Machine.SetActive(context.InGame);
        LocalPlayer local = context.InGame.World.Local;
        local.Level = level;
        local.Mp = mp;
        local.Hp = hp;
        local.SocketId = 77;

        UiControl root = BuildRoot();
        var store = new InMemoryHotkeyStore();
        // A real SkillTreeDialog validates class skills (HasSkill); usable-item ids need no tree.
        SkillTreeDialog? tree = known != null ? BuildTree(context, skills, known) : null;
        var dialog = new HotKeyDialog(context, root, skills, tree, new IconDragState(), store);
        dialog.Bind(context.InGame);
        return new Harness(dialog, root, local, client, context, store);
    }

    /// <summary>A real SkillTreeDialog seeded so HasSkill(id) is true for the class skills we test.</summary>
    private static SkillTreeDialog BuildTree(GameContext context, SkillTableSet skills, IEnumerable<uint> known)
    {
        var tRoot = new N3UiBase { Id = "skilltree", Region = Rect(0, 0, 400, 300) };
        for (int i = 0; i < SkillTreeDialog.SlotCount; i++)
            tRoot.Children.Add(new N3UiArea { Id = i.ToString(), AreaType = (int)UiAreaType.SkillTree, Region = Rect(i * 40, 0, i * 40 + 30, 30) });
        tRoot.Children.Add(Button("btn_public"));

        uint anyId = known.First();
        int cls = (int)(anyId / 1000);
        context.InGame.World.Local.Class = (short)cls;
        // Master pools high so the seeded skills populate as usable class skills.
        for (int i = 5; i <= 8; i++)
            context.InGame.World.Local.Skills[i] = 80;

        var tree = new SkillTreeDialog(context, UiControlFactory.Build(tRoot), skills, new IconDragState());
        tree.Rebuild();
        return tree;
    }

    // ======================= MagicCastManager =========================

    [Fact]
    public void Cast_TargetSkill_BuildsTargetPacket()
    {
        var mgr = new MagicCastManager();
        SkillRow skill = BuildSkills(new SkillSpec(103001, Target: 7, CastTime: 0)).Find(103001)!;
        var me = new LocalPlayer { SocketId = 77, Level = 60, Mp = 500 };

        CastResult r = mgr.TryCast(skill, casterId: 77, targetId: 42, (10, 20, 30), me, new Inventory(), 0.0);

        Assert.True(r.Success);
        byte[] wire = MagicProtocol.Build(r.Packet);
        var pr = new PacketReader(wire);
        Assert.Equal((byte)GameOpcode.WIZ_MAGIC_PROCESS, pr.GetByte());
        Assert.Equal(MagicProtocol.Effecting, pr.GetByte());  // CastTime==0 → EFFECTING(3)
        Assert.Equal(103001u, pr.GetDWord());
        Assert.Equal(77, pr.GetShort());   // caster
        Assert.Equal(42, pr.GetShort());   // target id in the target slot
        Assert.Equal(0, pr.GetShort());    // data1..6 all zero
        Assert.Equal(0, pr.GetShort());
        Assert.Equal(0, pr.GetShort());
    }

    [Fact]
    public void Cast_CastTimeSkill_UsesCastingSubcommand()
    {
        var mgr = new MagicCastManager();
        SkillRow skill = BuildSkills(new SkillSpec(103002, Target: 7, CastTime: 20)).Find(103002)!;
        var me = new LocalPlayer { Level = 60, Mp = 500 };

        CastResult r = mgr.TryCast(skill, 77, 42, (0, 0, 0), me, new Inventory(), 0.0);
        Assert.True(r.Success);
        Assert.Equal(MagicProtocol.Casting, MagicProtocol.Build(r.Packet)[1]); // CastTime>0 → CASTING(1)
    }

    [Fact]
    public void Cast_AreaSkill_BuildsPositionPacket()
    {
        var mgr = new MagicCastManager();
        SkillRow skill = BuildSkills(new SkillSpec(103003, Target: 13, CastTime: 0)).Find(103003)!; // AREA
        var me = new LocalPlayer { Level = 60, Mp = 500 };

        CastResult r = mgr.TryCast(skill, 77, targetId: -1, (11, 22, 33), me, new Inventory(), 0.0);
        Assert.True(r.Success);

        var pr = new PacketReader(MagicProtocol.Build(r.Packet));
        pr.GetByte();
        Assert.Equal(MagicProtocol.Effecting, pr.GetByte());
        Assert.Equal(103003u, pr.GetDWord());
        Assert.Equal(77, pr.GetShort());   // caster
        Assert.Equal(-1, pr.GetShort());   // target slot = -1 for a position cast
        Assert.Equal(11, pr.GetShort());   // posX in data1
        Assert.Equal(22, pr.GetShort());   // posY in data2
        Assert.Equal(33, pr.GetShort());   // posZ in data3
    }

    [Fact]
    public void Cast_MeleeType1Instant_SetsComboFlag()
    {
        var mgr = new MagicCastManager();
        SkillRow skill = BuildSkills(new SkillSpec(101001, Target: 7, CastTime: 0, First: 1)).Find(101001)!;
        var me = new LocalPlayer { Level = 60 };

        CastResult r = mgr.TryCast(skill, 5, 9, (0, 0, 0), me, new Inventory(), 0.0);
        Assert.True(r.Success);
        Assert.Equal(1, r.Packet.Data1); // combo flag
        Assert.Equal(1, r.Packet.Data2);
    }

    [Fact]
    public void Cast_CooldownGate_RejectsSecondCastUntilRecastElapses()
    {
        var mgr = new MagicCastManager();
        SkillRow skill = BuildSkills(new SkillSpec(103004, Target: 7, ReCastTime: 50)).Find(103004)!; // 5.0s
        var me = new LocalPlayer { Level = 60, Mp = 500 };

        Assert.True(mgr.TryCast(skill, 77, 42, (0, 0, 0), me, new Inventory(), 0.0).Success);
        Assert.Equal(CastFailReason.OnCooldown, mgr.TryCast(skill, 77, 42, (0, 0, 0), me, new Inventory(), 4.9).Reason);
        Assert.True(mgr.TryCast(skill, 77, 42, (0, 0, 0), me, new Inventory(), 5.0).Success);

        // Cooldown ring fraction: half-elapsed → ~0.5.
        SkillRow s2 = BuildSkills(new SkillSpec(103005, ReCastTime: 100)).Find(103005)!; // 10s
        mgr.TryCast(s2, 77, 42, (0, 0, 0), me, new Inventory(), 100.0);
        Assert.Equal(0.5, mgr.Cooldown(103005, 105.0), 3);
        Assert.Equal(0.0, mgr.Cooldown(103005, 999.0), 3);
    }

    [Fact]
    public void Cast_RejectsOnManaAndLevel()
    {
        var mgr = new MagicCastManager();
        SkillRow costly = BuildSkills(new SkillSpec(103006, ExhaustMsp: 200)).Find(103006)!;
        var poorMp = new LocalPlayer { Level = 60, Mp = 100 };
        Assert.Equal(CastFailReason.NotEnoughMp, mgr.TryCast(costly, 77, 42, (0, 0, 0), poorMp, new Inventory(), 0.0).Reason);

        SkillRow highLevel = BuildSkills(new SkillSpec(103007, NeedLevel: 70)).Find(103007)!;
        var lowLevel = new LocalPlayer { Level = 60, Mp = 1000 };
        Assert.Equal(CastFailReason.LevelTooLow, mgr.TryCast(highLevel, 77, 42, (0, 0, 0), lowLevel, new Inventory(), 0.0).Reason);
    }

    [Fact]
    public void Cast_RejectsWhenTargetRequiredButMissing()
    {
        var mgr = new MagicCastManager();
        SkillRow enemy = BuildSkills(new SkillSpec(103008, Target: 7)).Find(103008)!;
        var me = new LocalPlayer { Level = 60, Mp = 500 };
        Assert.Equal(CastFailReason.NoTarget, mgr.TryCast(enemy, 77, targetId: -1, (0, 0, 0), me, new Inventory(), 0.0).Reason);
    }

    [Fact]
    public void Cast_ExhaustItem_RequiresInventoryStock()
    {
        var mgr = new MagicCastManager();
        SkillRow arrow = BuildSkills(new SkillSpec(102001, Target: 7, ExhaustItem: 379001000)).Find(102001)!;
        var me = new LocalPlayer { Level = 60, Mp = 500 };

        var empty = new Inventory();
        Assert.Equal(CastFailReason.MissingItem, mgr.TryCast(arrow, 77, 42, (0, 0, 0), me, empty, 0.0).Reason);

        var stocked = new Inventory();
        stocked.Set(20, new InventoryItem(379001000, 30, 0));
        Assert.True(mgr.TryCast(arrow, 77, 42, (0, 0, 0), me, stocked, 0.0).Success);
    }

    // ======================= HotKeyDialog =============================

    [Fact]
    public void SetSkill_PlacesRuntimeIcon_AndTriggerCastsAtTarget()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(103001, Target: 7, CastTime: 0));
        Harness h = Build(skills, known: [103001u]);

        Assert.True(h.Dialog.SetSkill(0, 2, 103001));
        Assert.Equal(103001u, h.Dialog.SkillAt(0, 2));

        h.Dialog.TargetId = 55;
        CastResult r = h.Dialog.TriggerSlot(2, nowSeconds: 0.0);
        Assert.True(r.Success);

        byte[] wire = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_MAGIC_PROCESS, wire[0]);
        MagicPacket p = MagicProtocol.Parse(wire);
        Assert.Equal(103001, p.MagicId);
        Assert.Equal(55, p.TargetId);
        Assert.Equal(77, p.SourceId); // local socket id
    }

    [Fact]
    public void SetSkill_RejectsUnknownClassSkill()
    {
        // 104009 is a different class block → never enters the class-103 tree.
        SkillTableSet skills = BuildSkills(new SkillSpec(103001), new SkillSpec(104009));
        Harness h = Build(skills, known: [103001u]); // tree built for class 103

        Assert.False(h.Dialog.SetSkill(0, 0, 104009)); // foreign-class skill not in the tree → rejected
        Assert.Equal(0u, h.Dialog.SkillAt(0, 0));
        Assert.True(h.Dialog.SetSkill(0, 0, 103001));
    }

    [Fact]
    public void SetSkill_AcceptsUsableItemIdWithoutTree()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(450000, Target: 1));
        Harness h = Build(skills); // no tree

        Assert.True(h.Dialog.SetSkill(0, 0, 450000)); // id >= UsableItemIdMin bypasses the tree check
    }

    [Fact]
    public void EmptySlot_TriggerIsNoop()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(103001));
        Harness h = Build(skills, known: [103001u]);
        Assert.False(h.Dialog.TriggerSlot(4, 0.0).Success);
        Assert.Empty(h.Client.Sent);
    }

    [Fact]
    public void PageNav_HidesOtherPages()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(103001));
        Harness h = Build(skills, known: [103001u]);

        h.Dialog.SetSkill(1, 0, 103001);   // page 1 slot 0
        Assert.Equal(0, h.Dialog.CurrentPage);
        // Icon on page 1 is hidden while page 0 is shown → triggering page 1 slot does nothing here.
        h.Dialog.PageDown();
        Assert.Equal(1, h.Dialog.CurrentPage);
        h.Dialog.TargetId = 9;
        Assert.True(h.Dialog.TriggerSlot(0, 0.0).Success); // now visible on page 1

        h.Dialog.PageUp();
        Assert.Equal(0, h.Dialog.CurrentPage);
    }

    [Fact]
    public void FlushAll_ClearsEveryPage_AndPersistsEmpty()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(103001));
        Harness h = Build(skills, known: [103001u]);

        h.Dialog.SetSkill(0, 0, 103001);
        h.Dialog.SetSkill(2, 3, 103001);
        h.Dialog.FlushAll();

        Assert.Equal(0u, h.Dialog.SkillAt(0, 0));
        Assert.Equal(0u, h.Dialog.SkillAt(2, 3));
        Assert.Empty(h.Store.Load());
    }

    [Fact]
    public void QuickAdd_UsesFirstEmptySlot()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(103001), new SkillSpec(103002));
        Harness h = Build(skills, known: [103001u, 103002u]);

        Assert.Equal(0, h.Dialog.GetEmptySlotIndex());
        Assert.True(h.Dialog.ReceiveSkillFromTree(103001));
        Assert.Equal(103001u, h.Dialog.SkillAt(0, 0));
        Assert.Equal(1, h.Dialog.GetEmptySlotIndex());
        Assert.True(h.Dialog.ReceiveSkillFromTree(103002));
        Assert.Equal(103002u, h.Dialog.SkillAt(0, 1));
    }

    // ======================= Persistence ==============================

    [Fact]
    public void Persistence_RoundTripsThroughStore()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(103001), new SkillSpec(103002));
        var store = new InMemoryHotkeyStore();
        store.Save([new HotkeyEntry(0, 1, 103001), new HotkeyEntry(3, 2, 103002)]);

        var client = new FakeGameClient();
        var context = new GameContext(client);
        context.Machine.SetActive(context.InGame);
        context.InGame.World.Local.Class = 103;
        for (int i = 5; i <= 8; i++)
            context.InGame.World.Local.Skills[i] = 80;

        SkillTreeDialog tree = BuildTree(context, skills, [103001u, 103002u]);
        var dialog = new HotKeyDialog(context, BuildRoot(), skills, tree, new IconDragState(), store);
        dialog.LoadFromStore();

        Assert.Equal(103001u, dialog.SkillAt(0, 1));
        Assert.Equal(103002u, dialog.SkillAt(3, 2));
    }

    [Fact]
    public void Persistence_DropsInvalidEntriesOnLoad()
    {
        SkillTableSet skills = BuildSkills(new SkillSpec(103001));
        var store = new InMemoryHotkeyStore();
        store.Save([new HotkeyEntry(0, 0, 103001), new HotkeyEntry(0, 1, 999999)]); // 999999 unknown

        var client = new FakeGameClient();
        var context = new GameContext(client);
        context.Machine.SetActive(context.InGame);
        SkillTreeDialog tree = BuildTree(context, skills, [103001u]);
        var dialog = new HotKeyDialog(context, BuildRoot(), skills, tree, new IconDragState(), store);
        dialog.LoadFromStore();

        Assert.Equal(103001u, dialog.SkillAt(0, 0));
        Assert.Equal(0u, dialog.SkillAt(0, 1)); // invalid id dropped
    }

    // ======================= Corpus ===================================

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealHotKeyLayout_ExposesAreasAndStrings()
    {
        string? root = FindDataRoot();
        if (root == null)
            return;

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        string uif = table.HotKey(1);
        string? path = resolver.Resolve(uif);
        Assert.NotNull(path);

        var layout = new N3UiBase();
        layout.LoadFromFile(path!);
        UiControl dialog = UiControlFactory.Build(layout);

        Assert.NotNull(dialog.GetChildById<UiButton>("btn_up"));
        Assert.NotNull(dialog.GetChildById<UiButton>("btn_down"));
        for (int i = 0; i < HotKeyDialog.SlotCount; i++)
        {
            Assert.NotNull(dialog.GetChildAreaByOrder(UiAreaType.SkillHotkey, i));
            Assert.NotNull(dialog.GetChildById<UiStringControl>(i.ToString()));         // count strings
            Assert.NotNull(dialog.GetChildById<UiStringControl>((i + 10).ToString()));  // tooltip strings
        }
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
