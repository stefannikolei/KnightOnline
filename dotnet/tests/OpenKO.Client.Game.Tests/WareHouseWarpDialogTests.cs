using System.Buffers.Binary;
using System.Text;
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
/// Sub-slice 9.8a pins: the solo NPC/object dialogs — warehouse (deposit/withdraw/gold/paging),
/// warp menu, inn menu and anvil upgrade-select + NPC repair — plus every added protocol
/// builder/parser asserted byte-exact against the cited C++ layout. Fully headless.
/// </summary>
public class WareHouseWarpDialogTests
{
    private const int Helmet = 100010000;   // non-countable
    private const int Potion = 200020000;   // countable

    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public bool CryptionEnabled => true;

        public void EnableCryption(ulong publicKey) { }

        public byte[] Last => Sent[^1];
    }

    // ---- synthetic-tree + table helpers ------------------------------------

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiArea Area(int order, UiAreaType type, N3UiRect region) =>
        new() { Id = order.ToString(), AreaType = (int)type, Region = region };

    private static N3UiButton Btn(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20), ClickRect = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 60, 16) };

    // Basic table columns 0..23 (Countable at 23). Mirrors ItemBasicRow.FromCells.
    private static readonly TblType[] BasicColumns =
    [
        TblType.Dword, TblType.Byte, TblType.String, TblType.String, TblType.Dword, TblType.Byte,
        TblType.Dword, TblType.Dword, TblType.Dword, TblType.Dword,
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
        TblType.Dword, TblType.Dword, TblType.Short, TblType.Byte,
    ];

    private static object[] BasicRow(uint id, uint iconId, short maxDur, bool countable) =>
    [
        id, (byte)0, "item", "", 0u, (byte)0,
        0u, iconId, 0u, 0u,
        (byte)0, (byte)0, (byte)1, (byte)0, (byte)0,
        (short)0, (short)0, (short)0, (short)10, maxDur,
        0u, 0u, (short)0, (byte)(countable ? 1 : 0),
    ];

    private static ItemTableSet BuildItems()
    {
        N3TableFile basic = BuildTable(BasicColumns,
        [
            BasicRow(Helmet, 100010004u, 4000, countable: false),
            BasicRow(Potion, 200020001u, 0, countable: true),
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
                        w.Write(Encoding.ASCII.GetBytes(s));
                        break;
                    default: throw new InvalidOperationException();
                }
            }
        }

        w.Flush();
        return N3TableFile.Load(ms.ToArray(), encrypted: false);
    }

    private sealed class Pkt
    {
        private readonly List<byte> _b = [];
        public Pkt Byte(int v) { _b.Add((byte)v); return this; }
        public Pkt Short(int v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteInt16LittleEndian(s, (short)v); _b.AddRange(s.ToArray()); return this; }
        public Pkt DWord(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); _b.AddRange(s.ToArray()); return this; }
        public Pkt Str2(string v) { Short(v.Length); _b.AddRange(Encoding.ASCII.GetBytes(v)); return this; }
        public byte[] Done() => _b.ToArray();
    }

    // ======================================================================
    // Protocol byte layout
    // ======================================================================

    [Fact]
    public void BuildInput_MatchesToWareMsgLayout()
    {
        byte[] p = WarehouseProtocol.BuildInput(Helmet, page: 2, srcPos: 5, destPos: 7, count: 3);
        Assert.Equal((byte)GameOpcode.WIZ_WAREHOUSE, p[0]);
        Assert.Equal(WarehouseProtocol.Input, p[1]);       // N3_SP_WARE_GET_IN
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 2));
        Assert.Equal(2, p[6]);
        Assert.Equal(5, p[7]);
        Assert.Equal(7, p[8]);
        Assert.Equal(3u, BitConverter.ToUInt32(p, 9));
        Assert.Equal(13, p.Length);
    }

    [Fact]
    public void BuildOutput_MatchesFromWareMsgLayout()
    {
        byte[] p = WarehouseProtocol.BuildOutput(Potion, page: 1, srcPos: 4, destPos: 9, count: 12);
        Assert.Equal((byte)GameOpcode.WIZ_WAREHOUSE, p[0]);
        Assert.Equal(WarehouseProtocol.Output, p[1]);      // N3_SP_WARE_GET_OUT
        Assert.Equal((uint)Potion, BitConverter.ToUInt32(p, 2));
        Assert.Equal(1, p[6]);
        Assert.Equal(4, p[7]);
        Assert.Equal(9, p[8]);
        Assert.Equal(12u, BitConverter.ToUInt32(p, 9));
    }

    [Fact]
    public void BuildGoldInputOutput_UseDwGoldWith0xFFPositions()
    {
        // GoldCountToWareOK → SendToServerToWareMsg(dwGold, 0xff, 0xff, 0xff, iGold)
        byte[] gin = WarehouseProtocol.BuildGoldInput(1500);
        Assert.Equal(WarehouseProtocol.Input, gin[1]);
        Assert.Equal(900000000u, BitConverter.ToUInt32(gin, 2));
        Assert.Equal(0xff, gin[6]);
        Assert.Equal(0xff, gin[7]);
        Assert.Equal(0xff, gin[8]);
        Assert.Equal(1500u, BitConverter.ToUInt32(gin, 9));

        byte[] gout = WarehouseProtocol.BuildGoldOutput(250);
        Assert.Equal(WarehouseProtocol.Output, gout[1]);
        Assert.Equal(900000000u, BitConverter.ToUInt32(gout, 2));
        Assert.Equal(0xff, gout[6]);
        Assert.Equal(250u, BitConverter.ToUInt32(gout, 9));
    }

    [Fact]
    public void BuildWareMoveAndInvMove_CarryNoCount()
    {
        byte[] wm = WarehouseProtocol.BuildWareMove(Helmet, page: 3, srcPos: 1, destPos: 2);
        Assert.Equal(WarehouseProtocol.Move, wm[1]);       // N3_SP_WARE_WARE_MOVE
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(wm, 2));
        Assert.Equal(3, wm[6]);
        Assert.Equal(1, wm[7]);
        Assert.Equal(2, wm[8]);
        Assert.Equal(9, wm.Length);                        // no trailing count dword

        byte[] im = WarehouseProtocol.BuildInvMove(Helmet, page: 0, srcPos: 6, destPos: 8);
        Assert.Equal(WarehouseProtocol.InvenMove, im[1]);  // N3_SP_WARE_INV_MOVE
        Assert.Equal(9, im.Length);
    }

    [Fact]
    public void ParseOpen_ReadsGoldAndOccupiedSlots()
    {
        var pkt = new Pkt().Byte((byte)GameOpcode.WIZ_WAREHOUSE).Byte(WarehouseProtocol.Open).Byte(0);
        pkt.DWord(123456); // ware gold
        // slot 0 occupied, slot 1 empty, slot 2 occupied; rest empty.
        for (int i = 0; i < WarehouseProtocol.SlotCount; i++)
        {
            if (i == 0) { pkt.DWord((uint)Helmet).Short(4000).Short(1); }
            else if (i == 2) { pkt.DWord((uint)Potion).Short(0).Short(25); }
            else { pkt.DWord(0).Short(0).Short(0); }
        }

        WarehouseContents c = WarehouseProtocol.ParseOpen(pkt.Done());
        Assert.Equal(123456, c.Gold);
        Assert.Equal(2, c.Items.Count);
        Assert.Equal(0, c.Items[0].Index);
        Assert.Equal((uint)Helmet, c.Items[0].ItemId);
        Assert.Equal((short)4000, c.Items[0].Durability);
        Assert.Equal(2, c.Items[1].Index);
        Assert.Equal((short)25, c.Items[1].Count);
    }

    [Fact]
    public void BuildWarp_IsOpcodeThenInt16Id()
    {
        byte[] p = WarpProtocol.BuildWarp(0x0102);
        Assert.Equal(3, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_WARP_LIST, p[0]);
        Assert.Equal((short)0x0102, BitConverter.ToInt16(p, 1));
    }

    [Fact]
    public void ParseList_ReadsRows()
    {
        var pkt = new Pkt().Byte((byte)GameOpcode.WIZ_WARP_LIST).Byte(WarpProtocol.KindList).Short(2);
        pkt.Short(11).Str2("Moradon").Str2("Go to Moradon?").Short(21).Short(200).DWord(500).Short(100).Short(200).Short(5);
        pkt.Short(12).Str2("Luferson").Str2("Go to Luferson?").Short(48).Short(150).DWord(0).Short(10).Short(20).Short(1);

        WarpListReply reply = WarpProtocol.ParseList(pkt.Done());
        Assert.Equal(WarpProtocol.KindList, reply.Kind);
        Assert.Equal(2, reply.Warps.Count);
        Assert.Equal(11, reply.Warps[0].Id);
        Assert.Equal("Moradon", reply.Warps[0].Name);
        Assert.Equal("Go to Moradon?", reply.Warps[0].Agreement);
        Assert.Equal(500u, reply.Warps[0].Gold);
        Assert.Equal("Luferson", reply.Warps[1].Name);

        WarpListReply err = WarpProtocol.ParseList(new Pkt().Byte((byte)GameOpcode.WIZ_WARP_LIST).Byte(WarpProtocol.KindError).Done());
        Assert.Equal(WarpProtocol.KindError, err.Kind);
        Assert.Empty(err.Warps);
    }

    [Fact]
    public void BuildRepair_AndParseResult()
    {
        // CItemRepairMgr::Tick: [WIZ_ITEM_REPAIR][arm][order][dword itemId]
        byte[] p = RepairProtocol.BuildRepair(RepairProtocol.ArmEquip, order: 6, itemId: (uint)Helmet);
        Assert.Equal(7, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_REPAIR, p[0]);
        Assert.Equal(0x01, p[1]);
        Assert.Equal(6, p[2]);
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 3));

        RepairResult r = RepairProtocol.ParseResult(new Pkt().Byte((byte)GameOpcode.WIZ_ITEM_REPAIR).Byte(0x01).DWord(9999).Done());
        Assert.True(r.Success);
        Assert.Equal(9999u, r.Gold);
    }

    [Fact]
    public void UpgradeParseRequest_ReadsNpcId()
    {
        byte[] payload = new Pkt().Byte((byte)GameOpcode.WIZ_ITEM_UPGRADE).Byte((byte)UpgradeProtocol.Opcode.Req).Short(4242).Done();
        UpgradeRequest req = UpgradeProtocol.ParseRequest(payload);
        Assert.Equal((short)4242, req.NpcId);
        Assert.Equal((byte)UpgradeProtocol.Opcode.Req, UpgradeProtocol.Subcommand(payload));
    }

    // ======================================================================
    // Warehouse dialog
    // ======================================================================

    private sealed record WareHarness(
        WareHouseDialog Dialog, UiControl Root, Inventory Inv, LocalPlayer Local,
        FakeGameClient Client, CountableEdit Edit);

    private sealed record CountableEdit(CountableItemEditDialog Dialog, UiEditControl Field);

    private static WareHarness BuildWare()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var root = new N3UiBase { Id = "warehouse", Region = Rect(0, 0, 1400, 400) };
        for (int i = 0; i < WarehouseProtocol.ItemsPerPage; i++)
            root.Children.Add(Area(i, UiAreaType.TradeNpc, Rect(i * 40, 0, i * 40 + 30, 30)));
        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
            root.Children.Add(Area(i, UiAreaType.TradeMy, Rect(i * 40, 100, i * 40 + 30, 130)));
        root.Children.Add(Str("string_item_name"));
        root.Children.Add(Str("string_wareitem_name"));
        root.Children.Add(Str("string_page"));
        foreach (string id in (string[])["btn_close", "btn_gold", "btn_gold_warehouse", "btn_page_up", "btn_page_down"])
            root.Children.Add(Btn(id));

        UiControl uiRoot = UiControlFactory.Build(root);

        // The shared countable/quantity popup.
        var editRootNode = new N3UiBase { Id = CountableItemEditDialog.RootId, Region = Rect(0, 0, 200, 100) };
        editRootNode.Children.Add(new N3UiEdit { Id = "edit_trade", Region = Rect(0, 0, 100, 20) });
        editRootNode.Children.Add(Btn("btn_ok"));
        editRootNode.Children.Add(Btn("btn_cancel"));
        UiControl editRoot = UiControlFactory.Build(editRootNode);
        var manager = new UiManager();
        var edit = new CountableItemEditDialog(manager, editRoot);
        var field = editRoot.GetChildById<UiEditControl>("edit_trade")!;

        var dialog = new WareHouseDialog(context, uiRoot, BuildItems(), manager.IconDrag, edit);
        return new WareHarness(
            dialog, uiRoot, context.InGame.Inventory, context.InGame.World.Local, client, new CountableEdit(edit, field));
    }

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    [Fact]
    public void Deposit_DragBackpackToWare_SendsBuildInput()
    {
        WareHarness h = BuildWare();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Open(new WarehouseContents(0, []), h.Inv);

        UiIconControl icon = h.Dialog.InvIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(3 * 40 + 15, 15); // ware slot 3
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_WAREHOUSE, p[0]);
        Assert.Equal(WarehouseProtocol.Input, p[1]);
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 2));
        Assert.Equal(0, p[6]);   // page 0
        Assert.Equal(0, p[7]);   // src inv order 0
        Assert.Equal(3, p[8]);   // dest ware order 3
        Assert.Equal(1u, BitConverter.ToUInt32(p, 9));
    }

    [Fact]
    public void Withdraw_DragWareToBackpack_SendsBuildOutput()
    {
        WareHarness h = BuildWare();
        var contents = new WarehouseContents(0, [new WarehouseItem(0, (uint)Helmet, 4000, 1)]);
        h.Dialog.Open(contents, h.Inv);

        UiIconControl icon = h.Dialog.WareIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(5 * 40 + 15, 115); // inv slot 5
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal(WarehouseProtocol.Output, p[1]);
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 2));
        Assert.Equal(0, p[6]);   // ware page 0
        Assert.Equal(0, p[7]);   // src ware order 0
        Assert.Equal(5, p[8]);   // dest inv order 5
    }

    [Fact]
    public void Withdraw_Success_MovesItemIntoBackpack()
    {
        WareHarness h = BuildWare();
        h.Dialog.Open(new WarehouseContents(0, [new WarehouseItem(0, (uint)Helmet, 4000, 1)]), h.Inv);

        UiIconControl icon = h.Dialog.WareIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(5 * 40 + 15, 115);
        Msg(h.Root, icon, UiMsg.IconUp);

        Assert.Null(h.Inv.BackpackItem(5)); // untouched until the server confirms
        h.Dialog.OnResult(ok: true);
        Assert.Equal(Helmet, h.Inv.BackpackItem(5)!.ItemId);
    }

    [Fact]
    public void Deposit_CountableItem_OpensQuantityPopupThenSendsCount()
    {
        WareHarness h = BuildWare();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Potion, 40, 0));
        h.Dialog.Open(new WarehouseContents(0, []), h.Inv);

        UiIconControl icon = h.Dialog.InvIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(1 * 40 + 15, 15); // ware slot 1
        Msg(h.Root, icon, UiMsg.IconUp);

        // No packet yet — the popup gathers the amount.
        Assert.Empty(h.Client.Sent);
        Assert.True(h.Edit.Dialog.IsLocked);

        h.Edit.Field.Text = "15";
        h.Edit.Dialog.Ok();

        byte[] p = h.Client.Last;
        Assert.Equal(WarehouseProtocol.Input, p[1]);
        Assert.Equal((uint)Potion, BitConverter.ToUInt32(p, 2));
        Assert.Equal(1, p[8]);                              // ware dest order 1
        Assert.Equal(15u, BitConverter.ToUInt32(p, 9));     // the entered count
    }

    [Fact]
    public void GoldButton_Deposit_SendsBuildGoldInputAndUpdatesGold()
    {
        WareHarness h = BuildWare();
        h.Local.Gold = 10000;
        h.Dialog.Open(new WarehouseContents(500, []), h.Inv);

        UiButton btnGold = h.Root.GetChildById<UiButton>("btn_gold")!;
        Msg(h.Root, btnGold, UiMsg.ButtonClick);
        Assert.True(h.Edit.Dialog.IsLocked);

        h.Edit.Field.Text = "300";
        h.Edit.Dialog.Ok();

        byte[] p = h.Client.Last;
        Assert.Equal(WarehouseProtocol.Input, p[1]);
        Assert.Equal(900000000u, BitConverter.ToUInt32(p, 2));
        Assert.Equal(0xff, p[6]);
        Assert.Equal(300u, BitConverter.ToUInt32(p, 9));
        Assert.Equal(9700, h.Local.Gold);
        Assert.Equal(800, h.Dialog.WareGold);
    }

    [Fact]
    public void GoldButton_Withdraw_SendsBuildGoldOutput()
    {
        WareHarness h = BuildWare();
        h.Local.Gold = 100;
        h.Dialog.Open(new WarehouseContents(1000, []), h.Inv);

        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_gold_warehouse")!, UiMsg.ButtonClick);
        h.Edit.Field.Text = "250";
        h.Edit.Dialog.Ok();

        byte[] p = h.Client.Last;
        Assert.Equal(WarehouseProtocol.Output, p[1]);
        Assert.Equal(900000000u, BitConverter.ToUInt32(p, 2));
        Assert.Equal(250u, BitConverter.ToUInt32(p, 9));
        Assert.Equal(350, h.Local.Gold);
        Assert.Equal(750, h.Dialog.WareGold);
    }

    [Fact]
    public void Paging_ShowsRequestedPageAndUpdatesString()
    {
        WareHarness h = BuildWare();
        // Item on page 1 (flat index = 1 * 24).
        int flat = WarehouseProtocol.ItemsPerPage;
        h.Dialog.Open(new WarehouseContents(0, [new WarehouseItem(flat, (uint)Helmet, 4000, 1)]), h.Inv);

        Assert.False(h.Dialog.WareIcons[0]!.Visible); // page 0 slot 0 empty
        UiStringControl page = h.Root.GetChildById<UiStringControl>("string_page")!;
        Assert.Equal("1", page.Text);

        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_page_down")!, UiMsg.ButtonClick);
        Assert.Equal(1, h.Dialog.CurrentPage);
        Assert.Equal("2", page.Text);
        Assert.True(h.Dialog.WareIcons[0]!.Visible); // page 1 slot 0 now shows the item
    }

    // ======================================================================
    // Warp dialog
    // ======================================================================

    [Fact]
    public void Warp_PopulatesListAndConfirmSendsBuildWarp()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var node = new N3UiBase { Id = "warp", Region = Rect(0, 0, 300, 300) };
        node.Children.Add(new N3UiList { Id = "List_Infos", Region = Rect(0, 0, 200, 160), FontHeight = 16 });
        node.Children.Add(Str("Text_Agreement"));
        node.Children.Add(Btn("Btn_Ok"));
        node.Children.Add(Btn("Btn_Cancel"));
        UiControl root = UiControlFactory.Build(node);
        var dialog = new WarpDialog(context, root);

        dialog.OnWarpList(new WarpListReply(WarpProtocol.KindList,
        [
            new WarpInfo(11, "Moradon", "Go?", 21, 200, 500, 0, 0, 0),
            new WarpInfo(12, "Luferson", "Sure?", 48, 150, 0, 0, 0, 0),
        ]));

        Assert.True(root.Visible);
        UiListControl list = root.GetChildById<UiListControl>("List_Infos")!;
        Assert.Equal(2, list.Count);
        Assert.Equal("Go?", root.GetChildById<UiStringControl>("Text_Agreement")!.Text);

        list.SetCurSel(1);
        Msg(root, root.GetChildById<UiButton>("Btn_Ok")!, UiMsg.ButtonClick);

        byte[] p = client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_WARP_LIST, p[0]);
        Assert.Equal((short)12, BitConverter.ToInt16(p, 1));
        Assert.False(root.Visible);
    }

    // ======================================================================
    // Inn + Upgrade dialogs
    // ======================================================================

    [Fact]
    public void Inn_WarehouseButton_SendsOpenAndSaleIsDeferred()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var node = new N3UiBase { Id = "inn", Region = Rect(0, 0, 200, 200) };
        foreach (string id in (string[])["btn_warehouse", "btn_makeclan", "btn_sale"])
            node.Children.Add(Btn(id));
        UiControl root = UiControlFactory.Build(node);
        var inn = new InnDialog(context, root);

        bool clan = false, sale = false;
        inn.FoundClanRequested += () => clan = true;
        inn.SellBoardRequested += () => sale = true;

        // The N3_SP_WARE_INN push shows the window.
        inn.Bind(context.InGame);
        context.Machine.SetActive(context.InGame);
        context.Machine.TickActive();
        context.Machine.DispatchPacket([(byte)GameOpcode.WIZ_WAREHOUSE, WarehouseProtocol.Inn]);
        Assert.True(root.Visible);

        Msg(root, root.GetChildById<UiButton>("btn_warehouse")!, UiMsg.ButtonClick);
        byte[] p = client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_WAREHOUSE, p[0]);
        Assert.Equal(WarehouseProtocol.Open, p[1]);
        Assert.False(root.Visible);

        Msg(root, root.GetChildById<UiButton>("btn_makeclan")!, UiMsg.ButtonClick);
        Assert.True(clan);
        Msg(root, root.GetChildById<UiButton>("btn_sale")!, UiMsg.ButtonClick);
        Assert.True(sale);
    }

    [Fact]
    public void Upgrade_RequestOpensSelectAndRepairSends()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var node = new N3UiBase { Id = "upgradeselect", Region = Rect(0, 0, 200, 200) };
        foreach (string id in (string[])["upgrade_1", "upgrade_2", "btn_close"])
            node.Children.Add(Btn(id));
        UiControl root = UiControlFactory.Build(node);
        var upgrade = new UpgradeDialog(context, root);

        upgrade.OnUpgrade((byte)UpgradeProtocol.Opcode.Req,
            new Pkt().Byte((byte)GameOpcode.WIZ_ITEM_UPGRADE).Byte((byte)UpgradeProtocol.Opcode.Req).Short(777).Done());
        Assert.True(root.Visible);
        Assert.Equal((short)777, upgrade.NpcId);

        short raised = -1;
        upgrade.ItemUpgradeRequested += npc => raised = npc;
        Msg(root, root.GetChildById<UiButton>("upgrade_1")!, UiMsg.ButtonClick);
        Assert.Equal((short)777, raised);
        Assert.False(root.Visible);

        byte[] p = upgrade.RequestRepair(RepairProtocol.ArmInventory, 4, (uint)Helmet);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_REPAIR, p[0]);
        Assert.Equal(RepairProtocol.ArmInventory, p[1]);
        Assert.Equal(4, p[2]);
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 3));
        Assert.Equal(p, client.Last); // the built packet was the one sent
    }

    // ======================================================================
    // Corpus (skipped when Client/Data is absent)
    // ======================================================================

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealLayouts_LoadAndExposeKeyIds()
    {
        string? root = FindDataRoot();
        if (root == null)
            return;

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        UiControl ware = LoadLayout(resolver, table.WareHouse(1));
        Assert.NotNull(ware.GetChildById("btn_close"));
        Assert.NotNull(ware.GetChildById("btn_gold"));
        Assert.NotNull(ware.GetChildById("btn_gold_warehouse"));
        Assert.NotNull(ware.GetChildById("string_page"));
        Assert.NotNull(ware.GetChildAreaByOrder(UiAreaType.TradeNpc, 0));
        Assert.NotNull(ware.GetChildAreaByOrder(UiAreaType.TradeMy, 0));

        UiControl warp = LoadLayout(resolver, table.ZoneChangeOrWarp(1));
        Assert.NotNull(warp.GetChildById<UiListControl>("List_Infos"));
        Assert.NotNull(warp.GetChildById("Btn_Ok"));
        Assert.NotNull(warp.GetChildById("Btn_Cancel"));

        UiControl upgrade = LoadLayout(resolver, table.UpgradeSelect(1));
        Assert.NotNull(upgrade.GetChildById("upgrade_1"));
        Assert.NotNull(upgrade.GetChildById("upgrade_2"));
    }

    private static UiControl LoadLayout(KoPathResolver resolver, string uif)
    {
        string? path = resolver.Resolve(uif);
        Assert.NotNull(path);
        var layout = new N3UiBase();
        layout.LoadFromFile(path!);
        return UiControlFactory.Build(layout);
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
