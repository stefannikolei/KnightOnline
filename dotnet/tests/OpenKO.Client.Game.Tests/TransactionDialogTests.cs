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
/// Slice 10.2 pins: the NPC-vendor buy/sell window (CUITransactionDlg) — the WIZ_ITEM_TRADE
/// protocol builders/parsers byte-exact, the local vendor-catalogue filter (ItemTableSet.VendorItems)
/// and the dialog's open/buy/sell/move + gold-on-success behaviour. Fully headless.
/// </summary>
public class TransactionDialogTests
{
    // Vendor item: selling group 5, ext id 1 → tradeId 5001, full id 900010000 + 1.
    private const uint VendorBaseId = 900010000;
    private const int VendorTradeId = 5001;      // org 5, ext 1
    private const int VendorItemId = 900010001;  // basic.Id + ext.Id
    private const int Helmet = 100010000;        // non-countable backpack item
    private const int Potion = 200020000;        // countable backpack item

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

    // Basic table columns 0..35 (ExtIndex 1, MaxDur 19, Price 20, Countable 23, SellGroup 35).
    private static readonly TblType[] BasicColumns =
    [
        TblType.Dword, TblType.Byte, TblType.String, TblType.String, TblType.Dword, TblType.Byte,
        TblType.Dword, TblType.Dword, TblType.Dword, TblType.Dword,
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
        TblType.Dword, TblType.Dword, TblType.Short, TblType.Byte,
        TblType.Dword, TblType.Dword, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,
    ];

    private static object[] BasicRow(uint id, byte extIndex, short maxDur, uint price, bool countable, byte sellGroup) =>
    [
        id, extIndex, "item", "", 0u, (byte)0,
        0u, 0u, 0u, 0u,
        (byte)0, (byte)0, (byte)0, (byte)0, (byte)0,
        (short)0, (short)0, (short)0, (short)0, maxDur,
        price, 0u, (short)0, (byte)(countable ? 1 : 0),
        0u, 0u, (byte)0, (byte)0, (byte)0, (byte)0,
        (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, sellGroup,
    ];

    // Ext table columns 0..13 (Id 0, MaxDur 12, PriceMultiply 13).
    private static readonly TblType[] ExtColumns =
    [
        TblType.Dword, TblType.String, TblType.Dword, TblType.String, TblType.Dword, TblType.Dword,
        TblType.Dword, TblType.Byte, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
        TblType.Short, TblType.Short,
    ];

    private static object[] ExtRow(uint id, short priceMultiply) =>
    [
        id, "", 0u, "", 0u, 0u,
        0u, (byte)0, (short)0, (short)0, (short)0, (short)0,
        (short)0, priceMultiply,
    ];

    private static ItemTableSet BuildItems()
    {
        N3TableFile basic = BuildTable(BasicColumns,
        [
            BasicRow(VendorBaseId, 0, 100, 50, countable: false, sellGroup: 5),
            BasicRow((uint)Helmet, 0, 4000, 100, countable: false, sellGroup: 1),
            BasicRow((uint)Potion, 0, 0, 5, countable: true, sellGroup: 2),
        ]);

        N3TableFile ext0 = BuildTable(ExtColumns,
        [
            ExtRow(0, 1),   // helmet / potion ext (id % 1000 == 0)
            ExtRow(1, 1),   // vendor ext (id % 1000 == 1)
        ]);

        var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
        exts[0] = ext0;
        return new ItemTableSet(basic, exts);
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
        public byte[] Done() => _b.ToArray();
    }

    // ======================================================================
    // Protocol byte layout (byte-exact vs UITransactionDlg.cpp:740-779)
    // ======================================================================

    [Fact]
    public void BuildBuy_MatchesSendToServerBuyMsgLayout()
    {
        byte[] p = TransactionProtocol.BuildBuy(VendorTradeId, npcId: 4242, itemId: VendorItemId, pos: 3, count: 2);
        Assert.Equal(15, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_TRADE, p[0]);
        Assert.Equal(TransactionProtocol.Buy, p[1]);
        Assert.Equal((uint)VendorTradeId, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)));
        Assert.Equal((short)4242, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(6)));
        Assert.Equal((uint)VendorItemId, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8)));
        Assert.Equal(3, p[12]);
        Assert.Equal((short)2, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(13)));
    }

    [Fact]
    public void BuildSell_MatchesSendToServerSellMsgLayout()
    {
        byte[] p = TransactionProtocol.BuildSell(Helmet, pos: 6, count: 1);
        Assert.Equal(9, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_TRADE, p[0]);
        Assert.Equal(TransactionProtocol.Sell, p[1]);
        Assert.Equal((uint)Helmet, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)));
        Assert.Equal(6, p[6]);
        Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(7)));
    }

    [Fact]
    public void BuildMove_MatchesSendToServerMoveMsgLayout()
    {
        byte[] p = TransactionProtocol.BuildMove(Helmet, startPos: 2, destPos: 9);
        Assert.Equal(8, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_TRADE, p[0]);
        Assert.Equal(TransactionProtocol.Move, p[1]);
        Assert.Equal((uint)Helmet, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)));
        Assert.Equal(2, p[6]);
        Assert.Equal(9, p[7]);
    }

    [Fact]
    public void ParseResult_HandlesEveryResultCode()
    {
        TransactionResult ok = TransactionProtocol.ParseResult(
            new Pkt().Byte((byte)GameOpcode.WIZ_ITEM_TRADE).Byte(TransactionProtocol.ResultSuccess).DWord(987654).Done());
        Assert.True(ok.Success);
        Assert.Equal(987654u, ok.Money);

        TransactionResult fail = TransactionProtocol.ParseResult(
            new Pkt().Byte((byte)GameOpcode.WIZ_ITEM_TRADE).Byte(TransactionProtocol.ResultFail).Byte(0x04).Done());
        Assert.False(fail.Success);
        Assert.Equal(0x04, fail.FailType);

        TransactionResult moveOk = TransactionProtocol.ParseResult(
            new Pkt().Byte((byte)GameOpcode.WIZ_ITEM_TRADE).Byte(TransactionProtocol.ResultMoveSuccess).Done());
        Assert.True(moveOk.MoveSuccess);

        TransactionResult moveFail = TransactionProtocol.ParseResult(
            new Pkt().Byte((byte)GameOpcode.WIZ_ITEM_TRADE).Byte(TransactionProtocol.ResultMoveFail).Done());
        Assert.True(moveFail.MoveFail);
    }

    [Fact]
    public void ParseTradeStart_ReadsTradeId()
    {
        uint id = TransactionProtocol.ParseTradeStart(
            new Pkt().Byte((byte)GameOpcode.WIZ_TRADE_NPC).DWord(5001).Done());
        Assert.Equal(5001u, id);
    }

    // ======================================================================
    // Vendor-catalogue filter (ItemTableSet.VendorItems)
    // ======================================================================

    [Fact]
    public void VendorItems_SelectsBySellGroupAndExtId()
    {
        ItemTableSet items = BuildItems();

        var vendor = items.VendorItems(VendorTradeId); // org 5, ext 1
        (ItemBasicRow basic, ItemExtRow ext) = Assert.Single(vendor);
        Assert.Equal(VendorBaseId, basic.Id);
        Assert.Equal((byte)5, basic.SellGroup);
        Assert.Equal(1u, ext.Id);
        Assert.Equal(VendorItemId, (int)(basic.Id + ext.Id));

        // A selling group with no matching rows yields nothing.
        Assert.Empty(items.VendorItems(7001));
    }

    // ======================================================================
    // Transaction dialog
    // ======================================================================

    private sealed record Harness(
        TransactionDialog Dialog, UiControl Root, Inventory Inv, LocalPlayer Local,
        FakeGameClient Client, CountableItemEditDialog Edit, UiEditControl Field);

    private static Harness Build()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var root = new N3UiBase { Id = "transaction", Region = Rect(0, 0, 1400, 400) };
        for (int i = 0; i < TransactionDialog.MaxItemTrade; i++)
            root.Children.Add(Area(i, UiAreaType.TradeNpc, Rect(i * 40, 0, i * 40 + 30, 30)));
        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
            root.Children.Add(Area(i, UiAreaType.TradeMy, Rect(i * 40, 100, i * 40 + 30, 130)));
        root.Children.Add(Str("string_item_name"));
        root.Children.Add(Str("string_page"));
        foreach (string id in (string[])["btn_close", "btn_page_up", "btn_page_down"])
            root.Children.Add(Btn(id));
        UiControl uiRoot = UiControlFactory.Build(root);

        var editRootNode = new N3UiBase { Id = CountableItemEditDialog.RootId, Region = Rect(0, 0, 200, 100) };
        editRootNode.Children.Add(new N3UiEdit { Id = "edit_trade", Region = Rect(0, 0, 100, 20) });
        editRootNode.Children.Add(Btn("btn_ok"));
        editRootNode.Children.Add(Btn("btn_cancel"));
        UiControl editRoot = UiControlFactory.Build(editRootNode);
        var manager = new UiManager();
        var edit = new CountableItemEditDialog(manager, editRoot);
        var field = editRoot.GetChildById<UiEditControl>("edit_trade")!;

        var dialog = new TransactionDialog(context, uiRoot, BuildItems(), manager.IconDrag, edit);
        dialog.Bind(context.InGame);
        return new Harness(
            dialog, uiRoot, context.InGame.Inventory, context.InGame.World.Local, client, edit, field);
    }

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    [Fact]
    public void Open_PopulatesVendorCatalogueAndShows()
    {
        Harness h = Build();
        h.Dialog.Open(VendorTradeId, npcId: 77);

        Assert.True(h.Root.Visible);
        Assert.Equal(VendorTradeId, h.Dialog.TradeId);
        Assert.Equal((short)77, h.Dialog.NpcId);
        Assert.True(h.Dialog.VendorIcons[0]!.Visible);
        Assert.Equal(VendorItemId, h.Dialog.VendorItems[0]!.ItemId);
        Assert.Null(h.Dialog.VendorItems[1]); // only one item in this selling group
    }

    [Fact]
    public void Buy_DragVendorToBackpack_SendsBuildBuy()
    {
        Harness h = Build();
        h.Dialog.Open(VendorTradeId, npcId: 77);

        UiIconControl icon = h.Dialog.VendorIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(5 * 40 + 15, 115); // drop over a backpack slot
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_TRADE, p[0]);
        Assert.Equal(TransactionProtocol.Buy, p[1]);
        Assert.Equal((uint)VendorTradeId, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)));
        Assert.Equal((short)77, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(6)));
        Assert.Equal((uint)VendorItemId, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8)));
        Assert.Equal(0, p[12]);                                              // first free backpack slot
        Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(13)));
    }

    [Fact]
    public void Buy_Success_UpdatesGoldAndAddsItemToBackpack()
    {
        Harness h = Build();
        h.Local.Gold = 200;
        h.Dialog.Open(VendorTradeId, npcId: 77);

        byte[] p = h.Dialog.Buy(0)!;
        Assert.Equal(TransactionProtocol.Buy, p[1]);
        Assert.Null(h.Inv.BackpackItem(0)); // not applied until the server confirms

        h.Dialog.OnItemTrade(new TransactionResult(TransactionProtocol.ResultSuccess, 150, 0));
        Assert.Equal(150, h.Local.Gold);    // authoritative gold from the reply
        Assert.Equal(VendorItemId, h.Inv.BackpackItem(0)!.ItemId);
    }

    [Fact]
    public void Buy_Failure_LeavesBackpackUntouched()
    {
        Harness h = Build();
        h.Local.Gold = 200;
        h.Dialog.Open(VendorTradeId, npcId: 77);
        h.Dialog.Buy(0);

        h.Dialog.OnItemTrade(new TransactionResult(TransactionProtocol.ResultFail, 0, 0x04));
        Assert.Equal(200, h.Local.Gold);
        Assert.Null(h.Inv.BackpackItem(0));
    }

    [Fact]
    public void Sell_DragBackpackToVendor_SendsBuildSell()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(4), new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Open(VendorTradeId, npcId: 77);

        UiIconControl icon = h.Dialog.InvIcons[4]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(0 * 40 + 15, 15); // drop over a vendor slot
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal(TransactionProtocol.Sell, p[1]);
        Assert.Equal((uint)Helmet, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)));
        Assert.Equal(4, p[6]);                                              // source backpack slot order
        Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(7)));
    }

    [Fact]
    public void Sell_Success_RemovesItemAndUpdatesGold()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(4), new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Open(VendorTradeId, npcId: 77);

        h.Dialog.Sell(4);
        h.Dialog.OnItemTrade(new TransactionResult(TransactionProtocol.ResultSuccess, 9000, 0));
        Assert.Equal(9000, h.Local.Gold);
        Assert.Null(h.Inv.BackpackItem(4));
    }

    [Fact]
    public void Move_DragBackpackToBackpack_SendsBuildMoveAndSwaps()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Helmet, 1, 4000));
        h.Dialog.Open(VendorTradeId, npcId: 77);

        UiIconControl icon = h.Dialog.InvIcons[0]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(3 * 40 + 15, 115); // drop over backpack slot 3
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal(TransactionProtocol.Move, p[1]);
        Assert.Equal((uint)Helmet, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)));
        Assert.Equal(0, p[6]);   // start order
        Assert.Equal(3, p[7]);   // dest order

        // Optimistic swap applied to the backpack model.
        Assert.Null(h.Inv.BackpackItem(0));
        Assert.Equal(Helmet, h.Inv.BackpackItem(3)!.ItemId);

        // A move-fail rolls it back.
        h.Dialog.OnItemTrade(new TransactionResult(TransactionProtocol.ResultMoveFail, 0, 0));
        Assert.Equal(Helmet, h.Inv.BackpackItem(0)!.ItemId);
        Assert.Null(h.Inv.BackpackItem(3));
    }

    [Fact]
    public void BuyCountable_OpensQuantityPopupThenSendsCount()
    {
        Harness h = Build();
        h.Local.Gold = 10000;
        // A countable vendor item: reuse the potion's selling group (2 → tradeId 2000, ext 0).
        h.Dialog.Open(2000, npcId: 77);

        Assert.Equal(Potion, h.Dialog.VendorItems[0]!.ItemId);
        h.Dialog.Buy(0);
        Assert.Empty(h.Client.Sent);        // popup gathers the amount first
        Assert.True(h.Edit.IsLocked);

        h.Field.Text = "7";
        h.Edit.Ok();

        byte[] p = h.Client.Last;
        Assert.Equal(TransactionProtocol.Buy, p[1]);
        Assert.Equal((uint)Potion, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8)));
        Assert.Equal((short)7, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(13)));
    }

    [Fact]
    public void Paging_UpdatesPageString()
    {
        Harness h = Build();
        h.Dialog.Open(VendorTradeId, npcId: 77);
        UiStringControl page = h.Root.GetChildById<UiStringControl>("string_page")!;
        Assert.Equal("1", page.Text);

        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_page_down")!, UiMsg.ButtonClick);
        Assert.Equal(1, h.Dialog.CurrentPage);
        Assert.Equal("2", page.Text);

        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_page_up")!, UiMsg.ButtonClick);
        Assert.Equal(0, h.Dialog.CurrentPage);
        Assert.Equal("1", page.Text);
    }

    [Fact]
    public void Close_HidesWindow()
    {
        Harness h = Build();
        h.Dialog.Open(VendorTradeId, npcId: 77);
        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_close")!, UiMsg.ButtonClick);
        Assert.False(h.Root.Visible);
    }
}
