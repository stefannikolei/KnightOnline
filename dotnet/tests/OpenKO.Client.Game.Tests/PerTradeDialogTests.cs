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
/// Sub-slice 9.8b pins: the player-to-player TRADE dialog + WIZ_EXCHANGE state machine driven by
/// synthetic <see cref="PerTradeDialog.OnExchange"/> payloads and a fake game client. Every ADD/
/// AGREE/DECIDE/CANCEL packet is asserted byte-exact against the cited C++ layout, and the
/// OTHER_ADD / DONE receive parsers are exercised. Fully headless.
/// </summary>
public class PerTradeDialogTests
{
    private const int Helmet = 100010000;   // non-countable
    private const int Potion = 200020000;   // countable
    private const uint DwGold = 900000000;

    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public bool CryptionEnabled => true;

        public void EnableCryption(ulong publicKey) { }

        public byte[] Last => Sent[^1];
    }

    // ---- synthetic-tree + table helpers (mirrors WareHouseWarpDialogTests) --

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiArea Area(int order, UiAreaType type, N3UiRect region) =>
        new() { Id = order.ToString(), AreaType = (int)type, Region = region };

    private static N3UiButton Btn(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20), ClickRect = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 60, 16) };

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
        public byte[] Done() => _b.ToArray();
    }

    // ---- harness -----------------------------------------------------------

    private sealed record Harness(
        PerTradeDialog Dialog, UiControl Root, Inventory Inv, LocalPlayer Local,
        FakeGameClient Client, CountableItemEditDialog Edit, UiEditControl Field, MessageBoxDialog Box);

    private static Harness Build()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var root = new N3UiBase { Id = "pertrade", Region = Rect(0, 0, 1400, 600) };
        for (int i = 0; i < PerTradeDialog.MaxItemPerTrade; i++)
        {
            root.Children.Add(Area(i, UiAreaType.PerTradeMy, Rect(i * 40, 0, i * 40 + 30, 30)));
            root.Children.Add(Area(i, UiAreaType.PerTradeOther, Rect(i * 40, 40, i * 40 + 30, 70)));
            root.Children.Add(Str(i.ToString()));         // my count strings
            root.Children.Add(Str((i + 100).ToString())); // other count strings
        }

        for (int i = 0; i < PerTradeDialog.MaxItemInventory; i++)
        {
            root.Children.Add(Area(i, UiAreaType.PerTradeInv, Rect(i * 40, 100, i * 40 + 30, 130)));
            root.Children.Add(Str((i + 200).ToString())); // inv count strings
        }

        root.Children.Add(Str("string_money_inv"));
        root.Children.Add(Str("string_money_my"));
        root.Children.Add(Str("string_money_other"));
        foreach (string id in (string[])["btn_close", "btn_gold", "btn_trade_my", "btn_trade_other"])
            root.Children.Add(Btn(id));

        UiControl uiRoot = UiControlFactory.Build(root);

        // Shared countable/quantity popup.
        var editRootNode = new N3UiBase { Id = CountableItemEditDialog.RootId, Region = Rect(0, 0, 200, 100) };
        editRootNode.Children.Add(new N3UiEdit { Id = "edit_trade", Region = Rect(0, 0, 100, 20) });
        editRootNode.Children.Add(Btn("btn_ok"));
        editRootNode.Children.Add(Btn("btn_cancel"));
        UiControl editRoot = UiControlFactory.Build(editRootNode);
        var manager = new UiManager();
        var edit = new CountableItemEditDialog(manager, editRoot);
        var field = editRoot.GetChildById<UiEditControl>("edit_trade")!;

        // Message box (permit prompt).
        var boxNode = new N3UiBase { Id = "msgbox", Region = Rect(0, 0, 200, 100) };
        foreach (string id in (string[])["Btn_OK", "Btn_Yes", "Btn_No", "Btn_Cancel"])
            boxNode.Children.Add(Btn(id));
        boxNode.Children.Add(Str("Text_Message"));
        boxNode.Children.Add(Str("Text_Title"));
        UiControl boxRoot = UiControlFactory.Build(boxNode);
        var box = new MessageBoxDialog(boxRoot);

        var dialog = new PerTradeDialog(context, uiRoot, BuildItems(), manager.IconDrag, edit, box);
        dialog.Bind(context.InGame);
        return new Harness(
            dialog, uiRoot, context.InGame.Inventory, context.InGame.World.Local, client, edit, field, box);
    }

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    private static byte[] Recv(byte sub) => new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(sub).Done();

    /// <summary>Drive an incoming request → accept → the live NORMAL trade window.</summary>
    private static Harness Trading()
    {
        Harness h = Build();
        Trading_From(h);
        return h;
    }

    // ======================================================================
    // Protocol byte layout (ExchangeProtocol builders)
    // ======================================================================

    [Fact]
    public void BuildRequest_IsReqShortIdThenNearFlag()
    {
        // MsgSend_PerTradeReq: [WIZ_EXCHANGE][0x01][int16 destId][byte flag(1=near)]
        byte[] p = ExchangeProtocol.BuildRequest(4321, ExchangeProtocol.TradeTypeNormal);
        Assert.Equal(5, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_EXCHANGE, p[0]);
        Assert.Equal(ExchangeProtocol.Request, p[1]);
        Assert.Equal((short)4321, BitConverter.ToInt16(p, 2));
        Assert.Equal(1, p[4]);
    }

    [Fact]
    public void BuildAgree_IsAgreeThenFlag()
    {
        // ProcessProceed/LeavePerTradeState: [WIZ_EXCHANGE][0x02][byte 1/0]
        byte[] yes = ExchangeProtocol.BuildAgree(true);
        Assert.Equal([(byte)GameOpcode.WIZ_EXCHANGE, ExchangeProtocol.Agree, 1], yes);
        byte[] no = ExchangeProtocol.BuildAgree(false);
        Assert.Equal([(byte)GameOpcode.WIZ_EXCHANGE, ExchangeProtocol.Agree, 0], no);
    }

    [Fact]
    public void BuildAdd_IsAddPosItemIdCount()
    {
        // SendToServerItemAddMsg (UIPerTradeDlg.cpp:500): [0x30][0x03][u8 pos][u32 itemId][u32 count]
        byte[] p = ExchangeProtocol.BuildAdd(5, Helmet, 3);
        Assert.Equal(11, p.Length);                         // 1+1+1+4+4 (MP_Add* writes)
        Assert.Equal((byte)GameOpcode.WIZ_EXCHANGE, p[0]);
        Assert.Equal(ExchangeProtocol.Add, p[1]);
        Assert.Equal(5, p[2]);
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 3));
        Assert.Equal(3u, BitConverter.ToUInt32(p, 7));
    }

    [Fact]
    public void BuildAdd_GoldUses0xFFAndDwGold()
    {
        // ItemCountEditOK (SubProcPerTrade.cpp:565): [0x30][0x03][0xFF][u32 900000000][u32 amount]
        byte[] p = ExchangeProtocol.BuildAdd(0xFF, (int)DwGold, 5000);
        Assert.Equal(ExchangeProtocol.Add, p[1]);
        Assert.Equal(0xFF, p[2]);
        Assert.Equal(DwGold, BitConverter.ToUInt32(p, 3));
        Assert.Equal(5000u, BitConverter.ToUInt32(p, 7));
    }

    [Fact]
    public void BuildDecideAndCancel_AreBareSubcommands()
    {
        Assert.Equal([(byte)GameOpcode.WIZ_EXCHANGE, ExchangeProtocol.Decide], ExchangeProtocol.BuildDecide());
        Assert.Equal([(byte)GameOpcode.WIZ_EXCHANGE, ExchangeProtocol.Cancel], ExchangeProtocol.BuildCancel());
    }

    // ======================================================================
    // State machine
    // ======================================================================

    [Fact]
    public void Initiate_SendsRequestAndEntersWaitForReq()
    {
        Harness h = Build();
        // Put a visible player so the initiate path is exercised through the dialog directly.
        byte[]? p = h.Dialog.RequestTrade(4321);

        Assert.NotNull(p);
        Assert.Equal(ExchangeProtocol.Request, p![1]);
        Assert.Equal((short)4321, BitConverter.ToInt16(p, 2));
        Assert.Equal(PerTradeState.WaitForReq, h.Dialog.State);
    }

    [Fact]
    public void Initiate_ThenAgree1_EntersNormal()
    {
        Harness h = Build();
        h.Dialog.RequestTrade(4321);
        h.Dialog.OnExchange(ExchangeProtocol.Agree, new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Agree).Byte(1).Done());

        Assert.Equal(PerTradeState.Normal, h.Dialog.State);
        Assert.True(h.Root.Visible);
    }

    [Fact]
    public void Initiate_ThenAgree0_ReturnsToNone()
    {
        Harness h = Build();
        h.Dialog.RequestTrade(4321);
        h.Dialog.OnExchange(ExchangeProtocol.Agree, new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Agree).Byte(0).Done());

        Assert.Equal(PerTradeState.None, h.Dialog.State);
    }

    [Fact]
    public void IncomingRequest_ShowsPermit_AcceptSendsAgree1AndEntersNormal()
    {
        Harness h = Build();
        h.Dialog.OnExchange(ExchangeProtocol.Request,
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Request).Short(4321).Done());

        Assert.Equal(PerTradeState.WaitForMyDecision, h.Dialog.State);
        Assert.Equal((short)4321, h.Dialog.OtherId);
        Assert.True(h.Box.IsOpen);

        Msg(h.Box.Root, h.Box.Root.GetChildById<UiButton>("Btn_Yes")!, UiMsg.ButtonClick);

        byte[] p = h.Client.Last;
        Assert.Equal(ExchangeProtocol.Agree, p[1]);
        Assert.Equal(1, p[2]);
        Assert.Equal(PerTradeState.Normal, h.Dialog.State);
    }

    [Fact]
    public void IncomingRequest_RejectSendsAgree0AndReturnsToNone()
    {
        Harness h = Build();
        h.Dialog.OnExchange(ExchangeProtocol.Request,
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Request).Short(4321).Done());

        Msg(h.Box.Root, h.Box.Root.GetChildById<UiButton>("Btn_No")!, UiMsg.ButtonClick);

        byte[] p = h.Client.Last;
        Assert.Equal(ExchangeProtocol.Agree, p[1]);
        Assert.Equal(0, p[2]);
        Assert.Equal(PerTradeState.None, h.Dialog.State);
    }

    [Fact]
    public void AddItem_NonCountable_SendsBuildAddCountOne()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(3), new InventoryItem(Helmet, 1, 4000));
        Trading_From(h);

        h.Dialog.AddItem(3);

        byte[] p = h.Client.Last;
        Assert.Equal(ExchangeProtocol.Add, p[1]);
        Assert.Equal(3, p[2]);                              // pos = inventory source order
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 3));
        Assert.Equal(1u, BitConverter.ToUInt32(p, 7));
        Assert.True(h.Dialog.MyItems[0]!.ItemId == Helmet); // optimistically placed in a my slot
        Assert.Null(h.Dialog.InvItems[3]);                  // removed from the inv-mirror
    }

    [Fact]
    public void AddItem_DragFromInvMirrorToMySlot_SendsBuildAdd()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(2), new InventoryItem(Helmet, 1, 4000));
        Trading_From(h);

        UiIconControl icon = h.Dialog.InvIcons[2]!;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        h.Dialog.Cursor = new UiPoint(0 * 40 + 15, 15); // over my slot 0
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal(ExchangeProtocol.Add, p[1]);
        Assert.Equal(2, p[2]);                              // source inv order
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 3));
    }

    [Fact]
    public void AddItem_Countable_OpensPopupThenSendsCount()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(1), new InventoryItem(Potion, 40, 0));
        Trading_From(h);

        h.Dialog.AddItem(1);

        // The popup gathers the amount; no packet yet.
        Assert.DoesNotContain(h.Client.Sent, s => s.Length >= 2 && s[1] == ExchangeProtocol.Add);
        Assert.True(h.Edit.IsLocked);

        h.Field.Text = "15";
        h.Edit.Ok();

        byte[] p = h.Client.Last;
        Assert.Equal(ExchangeProtocol.Add, p[1]);
        Assert.Equal(1, p[2]);
        Assert.Equal((uint)Potion, BitConverter.ToUInt32(p, 3));
        Assert.Equal(15u, BitConverter.ToUInt32(p, 7));
        Assert.Equal(25, h.Dialog.InvItems[1]!.Count); // 40 - 15 remains in the mirror
    }

    [Fact]
    public void AddGold_ButtonThenPopup_SendsAdd0xFFDwGoldAndDebitsWallet()
    {
        Harness h = Build();
        h.Local.Gold = 10000;
        Trading_From(h);

        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_gold")!, UiMsg.ButtonClick);
        Assert.Equal(PerTradeState.Editting, h.Dialog.State);
        Assert.True(h.Edit.IsLocked);

        h.Field.Text = "5000";
        h.Edit.Ok();

        byte[] p = h.Client.Last;
        Assert.Equal(ExchangeProtocol.Add, p[1]);
        Assert.Equal(0xFF, p[2]);
        Assert.Equal(DwGold, BitConverter.ToUInt32(p, 3));
        Assert.Equal(5000u, BitConverter.ToUInt32(p, 7));
        Assert.Equal(5000, h.Local.Gold);      // wallet debited
        Assert.Equal(5000, h.Dialog.MyGold);   // trade-window credited
        Assert.Equal(PerTradeState.Normal, h.Dialog.State);
    }

    [Fact]
    public void Decide_SendsBuildDecideAndFreezesMyIcons()
    {
        Harness h = Trading();

        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_trade_my")!, UiMsg.ButtonClick);

        Assert.Equal([(byte)GameOpcode.WIZ_EXCHANGE, ExchangeProtocol.Decide], h.Client.Last);
        Assert.Equal(PerTradeState.MyTradeDecisionDone, h.Dialog.State);
    }

    [Fact]
    public void Cancel_CloseButton_SendsBuildCancelAndReturnsToNone()
    {
        Harness h = Trading();

        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_close")!, UiMsg.ButtonClick);

        Assert.Equal([(byte)GameOpcode.WIZ_EXCHANGE, ExchangeProtocol.Cancel], h.Client.Last);
        Assert.Equal(PerTradeState.None, h.Dialog.State);
        Assert.False(h.Root.Visible);
    }

    [Fact]
    public void OtherAdd_Item_PlacesIntoOtherGrid()
    {
        Harness h = Trading();

        h.Dialog.OnExchange(ExchangeProtocol.OtherAdd,
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.OtherAdd)
                .DWord((uint)Potion).DWord(30).Short(0).Done());

        Assert.Equal(Potion, h.Dialog.OtherItems[0]!.ItemId);
        Assert.Equal(30, h.Dialog.OtherItems[0]!.Count);
    }

    [Fact]
    public void OtherAdd_Gold_AccumulatesOtherGold()
    {
        Harness h = Trading();

        h.Dialog.OnExchange(ExchangeProtocol.OtherAdd,
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.OtherAdd)
                .DWord(DwGold).DWord(2500).Short(0).Done());

        Assert.Equal(2500, h.Dialog.OtherGold);
        Assert.Null(h.Dialog.OtherItems[0]); // gold is not a grid item
    }

    [Fact]
    public void OtherDecide_MarksOtherReady()
    {
        Harness h = Trading();
        Assert.False(h.Dialog.OtherReady);

        h.Dialog.OnExchange(ExchangeProtocol.OtherDecide, Recv(ExchangeProtocol.OtherDecide));

        Assert.True(h.Dialog.OtherReady);
    }

    [Fact]
    public void Done_Success_AppliesGoldAndItemsToInventory()
    {
        Harness h = Trading();
        h.Local.Gold = 1000;

        // DONE(1): totalGold=7777, 1 item move into inv slot 4 (a received potion x50).
        byte[] done = new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Done)
            .Byte(1).DWord(7777).Short(1)
            .Byte(4).DWord((uint)Potion).Short(50).Short(0).Done();
        h.Dialog.OnExchange(ExchangeProtocol.Done, done);

        Assert.Equal(7777, h.Local.Gold);
        InventoryItem? got = h.Inv.BackpackItem(4);
        Assert.NotNull(got);
        Assert.Equal(Potion, got!.ItemId);
        Assert.Equal(50, got.Count);
        Assert.Equal(PerTradeState.None, h.Dialog.State);
    }

    [Fact]
    public void Done_Fail_RestoresOfferedGoldAndReturnsToNone()
    {
        Harness h = Build();
        h.Local.Gold = 10000;
        Trading_From(h);

        // Offer 3000 gold, then the server reports failure → gold restored.
        Msg(h.Root, h.Root.GetChildById<UiButton>("btn_gold")!, UiMsg.ButtonClick);
        h.Field.Text = "3000";
        h.Edit.Ok();
        Assert.Equal(7000, h.Local.Gold);

        h.Dialog.OnExchange(ExchangeProtocol.Done,
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Done).Byte(0).Done());

        Assert.Equal(10000, h.Local.Gold); // offered gold returned
        Assert.Equal(PerTradeState.None, h.Dialog.State);
    }

    [Fact]
    public void Cancel_Recv_RestoresOfferedItemToInventory()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Helmet, 1, 4000));
        Trading_From(h);

        h.Dialog.AddItem(0);                 // offer the helmet
        h.Dialog.OnExchange(ExchangeProtocol.Add, // server confirms the add
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Add).Byte(1).Done());

        h.Dialog.OnExchange(ExchangeProtocol.Cancel, Recv(ExchangeProtocol.Cancel));

        // The offered item is returned to the backpack; the trade ends.
        Assert.Equal(Helmet, h.Inv.BackpackItem(0)!.ItemId);
        Assert.Equal(PerTradeState.None, h.Dialog.State);
    }

    [Fact]
    public void AddResult_Failure_RollsBackTheOptimisticMove()
    {
        Harness h = Build();
        h.Inv.Set(Inventory.BackpackIndex(0), new InventoryItem(Helmet, 1, 4000));
        Trading_From(h);

        h.Dialog.AddItem(0);
        Assert.NotNull(h.Dialog.MyItems[0]);
        Assert.Null(h.Dialog.InvItems[0]);

        // ADD(0) → rollback: the item returns to the inv-mirror, the my slot clears.
        h.Dialog.OnExchange(ExchangeProtocol.Add,
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Add).Byte(0).Done());

        Assert.Null(h.Dialog.MyItems[0]);
        Assert.Equal(Helmet, h.Dialog.InvItems[0]!.ItemId);
    }

    // ======================================================================
    // Corpus (skipped when Client/Data is absent)
    // ======================================================================

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealLayout_LoadsAndExposesKeyAreasAndButtons()
    {
        string? root = FindDataRoot();
        if (root == null)
            return;

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        string uif = table.PersonalTrade(1);
        Assert.NotEqual(string.Empty, uif);
        string? path = resolver.Resolve(uif);
        Assert.NotNull(path);
        var layout = new N3UiBase();
        layout.LoadFromFile(path!);
        UiControl trade = UiControlFactory.Build(layout);

        Assert.NotNull(trade.GetChildById("btn_gold"));
        Assert.NotNull(trade.GetChildById("btn_trade_my"));
        Assert.NotNull(trade.GetChildAreaByOrder(UiAreaType.PerTradeMy, 0));
        Assert.NotNull(trade.GetChildAreaByOrder(UiAreaType.PerTradeOther, 0));
        Assert.NotNull(trade.GetChildAreaByOrder(UiAreaType.PerTradeInv, 0));
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

    // ---- helpers -----------------------------------------------------------

    /// <summary>Drive h into the live NORMAL trade window (accept an incoming request).</summary>
    private static void Trading_From(Harness h)
    {
        h.Dialog.OnExchange(ExchangeProtocol.Request,
            new Pkt().Byte((byte)GameOpcode.WIZ_EXCHANGE).Byte(ExchangeProtocol.Request).Short(4321).Done());
        Msg(h.Box.Root, h.Box.Root.GetChildById<UiButton>("Btn_Yes")!, UiMsg.ButtonClick);
    }
}
