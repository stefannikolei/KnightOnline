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
/// Sub-slice 9.5-3 pins: the inventory dialog drag/drop flow — runtime icon population,
/// the WIZ_ITEM_MOVE direction + src/dest resolution, the server reply commit/rollback and
/// right-click quick-equip, plus the WIZ_ITEM_MOVE reply parse. Fully headless over a
/// synthetic .uif tree, item table and fake client.
/// </summary>
public class InventoryDialogTests
{
    // Two test items: a helmet (equip, attach=Head) and a stackable potion (inventory-only).
    private const int Helmet = 100010000;
    private const int Potion = 200020000;

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

    private static N3UiArea Area(int order, UiAreaType type, N3UiRect region) =>
        new() { Id = order.ToString(), AreaType = (int)type, Region = region };

    /// <summary>Synthetic inventory .uif: 14 Slot areas, 28 Inv areas, area_char/area_samma/text_weight.</summary>
    private static UiControl BuildRoot()
    {
        var root = new N3UiBase { Id = "inventory", Region = Rect(0, 0, 1200, 400) };

        for (int i = 0; i < Inventory.EquipSlotCount; i++)
            root.Children.Add(Area(i, UiAreaType.Slot, Rect(i * 40, 0, i * 40 + 30, 30)));
        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
            root.Children.Add(Area(i, UiAreaType.Inv, Rect(i * 40, 100, i * 40 + 30, 130)));

        root.Children.Add(new N3UiArea { Id = "area_char", AreaType = 0, Region = Rect(0, 200, 100, 230) });
        root.Children.Add(new N3UiArea { Id = "area_samma", AreaType = 0, Region = Rect(200, 200, 300, 230) });
        root.Children.Add(new N3UiString { Id = "text_weight", Region = Rect(0, 300, 100, 316) });

        return UiControlFactory.Build(root);
    }

    // A minimal Item_Org table: only the first 20 columns (TblCell guards short rows). Types
    // mirror ItemBasicRow.FromCells: id/extIndex/name/remark/idk0/idk1/resrc/icon/snd0/snd1/
    // class/robe/attach/needRace/needClass/dmg/atkInt/atkRange/weight/maxDur.
    private static readonly TblType[] BasicColumns =
    [
        TblType.Dword, TblType.Byte, TblType.String, TblType.String, TblType.Dword, TblType.Byte,
        TblType.Dword, TblType.Dword, TblType.Dword, TblType.Dword,
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
    ];

    private static object[] BasicRow(uint id, uint iconId, KoItemPosition attach, short maxDur) =>
    [
        id, (byte)0, "item", "", 0u, (byte)0,
        0u, iconId, 0u, 0u,
        (byte)0, (byte)0, (byte)attach, (byte)0, (byte)0,
        (short)0, (short)0, (short)0, (short)10, maxDur,
    ];

    private static ItemTableSet BuildItems()
    {
        N3TableFile basic = BuildTable(BasicColumns,
        [
            BasicRow(Helmet, 100010004u, KoItemPosition.Head, 4000),
            BasicRow(Potion, 200020001u, KoItemPosition.Inventory, 0),
        ]);
        return new ItemTableSet(basic, new N3TableFile?[ItemTableSet.MaxItemExtension]);
    }

    private static N3TableFile BuildTable(IReadOnlyList<TblType> columns, IReadOnlyList<object[]> rows)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(columns.Count);
        foreach (TblType t in columns)
            w.Write((int)t);
        w.Write(rows.Count);
        foreach (object[] row in rows)
        {
            for (int j = 0; j < columns.Count; j++)
            {
                switch (columns[j])
                {
                    case TblType.Byte: w.Write(Convert.ToByte(row[j])); break;
                    case TblType.Short: w.Write(Convert.ToInt16(row[j])); break;
                    case TblType.Dword: w.Write(Convert.ToUInt32(row[j])); break;
                    case TblType.String:
                        var s = (string)row[j];
                        w.Write(s.Length);
                        w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                        break;
                    default: throw new InvalidOperationException();
                }
            }
        }

        w.Flush();
        return N3TableFile.Load(ms.ToArray(), encrypted: false);
    }

    private sealed record Harness(
        InventoryDialog Dialog, UiControl Root, Inventory Inv, FakeGameClient Client, IconDragState Drag, GameContext Context);

    private static Harness Build()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiControl root = BuildRoot();
        var drag = new IconDragState();
        var dialog = new InventoryDialog(context, root, BuildItems(), drag);
        return new Harness(dialog, root, context.InGame.Inventory, client, drag, context);
    }

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    // ---- Population --------------------------------------------------------

    [Fact]
    public void Populate_SetsIconTexturesPayloadAndVisibility()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Potion, 5, 0));
        h.Inv.Set((int)EquipSlot.Head, new InventoryItem(Helmet, 1, 4000));

        h.Dialog.Populate(h.Inv);

        UiIconControl potion = h.Dialog.BackpackIcons[0]!;
        Assert.True(potion.Visible);
        Assert.StartsWith(@"UI\ItemIcon_", potion.IconTexture);
        var payload = Assert.IsType<InventoryDialog.InventoryIconItem>(potion.Payload);
        Assert.Equal(Potion, payload.ItemId);
        Assert.Equal(5, payload.Count);

        UiIconControl helmet = h.Dialog.EquipIcons[(int)EquipSlot.Head]!;
        Assert.True(helmet.Visible);
        Assert.NotEmpty(helmet.IconTexture);

        // Empty slot hides its icon and clears the texture.
        UiIconControl empty = h.Dialog.BackpackIcons[1]!;
        Assert.False(empty.Visible);
        Assert.Empty(empty.IconTexture);
    }

    [Fact]
    public void Bind_UpdatesWeightFromLocalPlayer()
    {
        Harness h = Build();
        h.Dialog.Bind(h.Context.InGame);
        h.Context.InGame.World.Local.CurWeight = 120;
        h.Context.InGame.World.Local.MaxWeight = 3000;

        h.Dialog.Populate(h.Inv);

        UiStringControl weight = h.Root.GetChildById<UiStringControl>("text_weight")!;
        Assert.Equal("120 / 3000", weight.Text);
    }

    // ---- Drag/drop direction resolution ------------------------------------

    [Fact]
    public void Drag_BackpackToBackpack_SendsInventoryToInventory()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Potion, 5, 0));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.BackpackIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(210, 110); // inside Inv area order 5 (200..230, 100..130)
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_MOVE, p[0]);
        Assert.Equal((byte)ItemMoveDirection.InventoryToInventory, p[1]);
        Assert.Equal((uint)Potion, BitConverter.ToUInt32(p, 2));
        Assert.Equal(0, p[6]);  // src order (backpack cell 0)
        Assert.Equal(5, p[7]);  // dest order (backpack cell 5)
        Assert.True(h.Drag.WaitFromServer);
    }

    [Fact]
    public void Drag_BackpackToEquipSlot_SendsInventoryToSlot()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.BackpackIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(50, 10); // inside Slot area order 1 (Head)
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)ItemMoveDirection.InventoryToSlot, p[1]);
        Assert.Equal(0, p[6]);                        // src backpack cell 0
        Assert.Equal((int)EquipSlot.Head, p[7]);      // dest equip slot 1
    }

    [Fact]
    public void Drag_DropOnPaperdoll_AutoPicksEquipSlot()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(3), new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.BackpackIcons[3]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(50, 210); // inside area_char (0..100, 200..230)
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)ItemMoveDirection.InventoryToSlot, p[1]);
        Assert.Equal(3, p[6]);                        // src backpack cell 3
        Assert.Equal((int)EquipSlot.Head, p[7]);      // helmet auto-routes to the head slot
    }

    [Fact]
    public void Drag_DropOnEmptySpace_RestoresWithoutSending()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Potion, 5, 0));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.BackpackIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(600, 350); // empty space
        Msg(h.Root, icon, UiMsg.IconUp);

        Assert.Empty(h.Client.Sent);
        Assert.False(h.Drag.WaitFromServer);
    }

    // ---- Server reply commit / rollback ------------------------------------

    [Fact]
    public void Reply_Success_CommitsTheModelMove()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Potion, 5, 0));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.BackpackIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(210, 110); // Inv cell 5
        Msg(h.Root, icon, UiMsg.IconUp);

        // The model is untouched until the server confirms.
        Assert.NotNull(h.Inv.Get(Inventory.BackpackIndex(0)));
        Assert.Null(h.Inv.Get(Inventory.BackpackIndex(5)));

        h.Dialog.OnItemMoveResult(ok: true);

        Assert.Null(h.Inv.Get(Inventory.BackpackIndex(0)));
        Assert.Equal(Potion, h.Inv.Get(Inventory.BackpackIndex(5))!.ItemId);
        Assert.True(h.Dialog.BackpackIcons[5]!.Visible);
        Assert.False(h.Dialog.BackpackIcons[0]!.Visible);
        Assert.False(h.Drag.WaitFromServer);
    }

    [Fact]
    public void Reply_Fail_RollsBackTheIcon()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Potion, 5, 0));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.BackpackIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(210, 110);
        Msg(h.Root, icon, UiMsg.IconUp);

        h.Dialog.OnItemMoveResult(ok: false);

        // Model unchanged; the source icon is home and the destination stays empty.
        Assert.Equal(Potion, h.Inv.Get(Inventory.BackpackIndex(0))!.ItemId);
        Assert.True(h.Dialog.BackpackIcons[0]!.Visible);
        Assert.False(h.Dialog.BackpackIcons[5]!.Visible);
        Assert.False(h.Drag.WaitFromServer);

        // The rolled-back icon sits back on its home slot region.
        UiAreaControl home = h.Root.GetChildAreaByOrder(UiAreaType.Inv, 0)!;
        Assert.Equal(home.Region.Left, h.Dialog.BackpackIcons[0]!.Region.Left);
    }

    // ---- Right-click quick equip / unequip ---------------------------------

    [Fact]
    public void RightClick_BackpackItem_QuickEquips()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.BackpackIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconRDownFirst);
        Msg(h.Root, icon, UiMsg.IconRUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)ItemMoveDirection.InventoryToSlot, p[1]);
        Assert.Equal(0, p[6]);
        Assert.Equal((int)EquipSlot.Head, p[7]);
    }

    [Fact]
    public void RightClick_EquippedItem_QuickUnequipsToFirstEmptyBackpack()
    {
        Harness h = Build();
        h.Inv.Set((int)EquipSlot.Head, new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Populate(h.Inv);

        UiIconControl icon = h.Dialog.EquipIcons[(int)EquipSlot.Head]!;
        Msg(h.Root, icon, UiMsg.IconRDownFirst);
        Msg(h.Root, icon, UiMsg.IconRUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)ItemMoveDirection.SlotToInventory, p[1]);
        Assert.Equal((int)EquipSlot.Head, p[6]); // src equip slot
        Assert.Equal(0, p[7]);                    // first empty backpack cell
    }

    // ---- Reply parse -------------------------------------------------------

    [Fact]
    public void ParseItemMoveResult_DecodesTheStatBlob()
    {
        byte[] payload = BuildMoveReplyStats(
            attack: 100, guard: 50, weightMax: 2000, hpMax: 500, mspMax: 300,
            str: 1, sta: 2, dex: 3, intel: 4, magic: 5,
            rf: 6, rc: 7, rl: 8, rm: 9, rcu: 10, rp: 11);

        ItemMoveResult res = ItemProtocol.ParseItemMoveResult(payload);

        Assert.True(res.Success);
        Assert.Equal((short)100, res.Attack);
        Assert.Equal((short)50, res.Guard);
        Assert.Equal((short)2000, res.WeightMax);
        Assert.Equal((short)500, res.HpMax);
        Assert.Equal((short)300, res.MspMax);
        Assert.Equal((ushort)1, res.StrDelta);
        Assert.Equal((ushort)5, res.MagicAttackDelta);
        Assert.Equal((ushort)6, res.ResistFire);
        Assert.Equal((ushort)11, res.ResistPoison);

        // A rejection carries no stats.
        ItemMoveResult fail = ItemProtocol.ParseItemMoveResult([(byte)GameOpcode.WIZ_ITEM_MOVE, 0x00]);
        Assert.False(fail.Success);
        Assert.Equal((short)0, fail.HpMax);
    }

    [Fact]
    public void InGameState_ItemMoveReply_AppliesStatsAndRaisesEvent()
    {
        var client = new FakeGameClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();
        ctx.InGame.World.Local.Hp = 999; // will clamp to the new max

        ItemMoveResult? raised = null;
        ctx.InGame.ItemMoveResult = r => raised = r;

        byte[] payload = BuildMoveReplyStats(
            attack: 77, guard: 33, weightMax: 2500, hpMax: 480, mspMax: 260,
            str: 0, sta: 0, dex: 0, intel: 0, magic: 0,
            rf: 12, rc: 0, rl: 0, rm: 0, rcu: 0, rp: 0);
        ctx.Machine.DispatchPacket(payload);

        Assert.NotNull(raised);
        Assert.True(raised!.Value.Success);
        LocalPlayer l = ctx.InGame.World.Local;
        Assert.Equal((short)480, l.MaxHp);
        Assert.Equal((short)480, l.Hp);            // clamped
        Assert.Equal((short)260, l.MaxMp);
        Assert.Equal((short)2500, l.MaxWeight);
        Assert.Equal((byte)12, l.FireResist);
    }

    private static byte[] BuildMoveReplyStats(
        short attack, short guard, short weightMax, short hpMax, short mspMax,
        ushort str, ushort sta, ushort dex, ushort intel, ushort magic,
        ushort rf, ushort rc, ushort rl, ushort rm, ushort rcu, ushort rp)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)GameOpcode.WIZ_ITEM_MOVE);
        w.Write((byte)0x01);
        foreach (short s in (short[])[attack, guard, weightMax, hpMax, mspMax])
            w.Write(s);
        foreach (ushort u in (ushort[])[str, sta, dex, intel, magic, rf, rc, rl, rm, rcu, rp])
            w.Write(u);
        w.Flush();
        return ms.ToArray();
    }

    // ---- Corpus ------------------------------------------------------------

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealInventoryLayout_ExposesAreasAndControls()
    {
        string? root = FindDataRoot();
        if (root == null)
            return; // corpus not available

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        string uif = table.Inventory(1);
        string? path = resolver.Resolve(uif);
        Assert.NotNull(path);

        var layout = new N3UiBase();
        layout.LoadFromFile(path!);
        UiControl dialog = UiControlFactory.Build(layout);

        Assert.NotNull(dialog.GetChildById("area_char"));
        Assert.NotNull(dialog.GetChildById("area_samma"));
        Assert.NotNull(dialog.GetChildById<UiStringControl>("text_weight"));

        // The runtime binds equip icons to Slot 0..13 and backpack icons to Inv 0..27.
        Assert.NotNull(dialog.GetChildAreaByOrder(UiAreaType.Slot, 0));
        Assert.NotNull(dialog.GetChildAreaByOrder(UiAreaType.Slot, Inventory.EquipSlotCount - 1));
        Assert.NotNull(dialog.GetChildAreaByOrder(UiAreaType.Inv, 0));
        Assert.NotNull(dialog.GetChildAreaByOrder(UiAreaType.Inv, Inventory.BackpackSlotCount - 1));

        // The controller builds cleanly over the real layout.
        var context = new GameContext(new FakeGameClient());
        var dlg = new InventoryDialog(context, dialog, BuildItems(), new IconDragState());
        Assert.Equal(Inventory.EquipSlotCount, dlg.EquipIcons.Count);
        Assert.Equal(Inventory.BackpackSlotCount, dlg.BackpackIcons.Count);
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
