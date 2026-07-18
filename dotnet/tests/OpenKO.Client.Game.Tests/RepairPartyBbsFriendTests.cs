using System.Buffers.Binary;
using System.Text;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Slice 10.3 pins: the NPC-repair mode (CItemRepairMgr + CUIInventory INV_STATE_REPAIR), the
/// party-recruitment board (CUIPartyBBS / WIZ_PARTY_BBS) and the client-local friends list
/// (CUIFriends / WIZ_FRIEND_PROCESS). Protocol byte layouts are asserted against the C# Ebenezer
/// send side; the friend status path is inert by design (server no-op upstream). Fully headless.
/// </summary>
public class RepairPartyBbsFriendTests
{
    private const int Helmet = 100010000;

    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public bool CryptionEnabled => true;

        public void EnableCryption(ulong publicKey) { }

        public byte[] Last => Sent[^1];
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

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiArea Area(int order, UiAreaType type, N3UiRect region) =>
        new() { Id = order.ToString(), AreaType = (int)type, Region = region };

    private static N3UiButton Btn(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20), ClickRect = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 60, 16) };

    // ======================================================================
    // RepairCost.Calc — CItemRepairMgr::CalcRepairGold
    // ======================================================================

    [Fact]
    public void RepairCost_KnownValues()
    {
        // allPrice = 10000 → 10000^0.75 = 1000; temp = 0.999 + 1000 = 1000.999.
        Assert.Equal(1000, RepairCost.Calc(10000f, curDurability: 0, maxDurability: 100));   // full missing
        Assert.Equal(500, RepairCost.Calc(10000f, curDurability: 50, maxDurability: 100));    // half missing
        Assert.Equal(0, RepairCost.Calc(10000f, curDurability: 100, maxDurability: 100));     // nothing to repair
        Assert.Equal(0, RepairCost.Calc(10000f, curDurability: 0, maxDurability: 0));         // not wearable
    }

    // ======================================================================
    // PartyBbsProtocol — WIZ_PARTY_BBS (0x4F)
    // ======================================================================

    [Fact]
    public void PartyBbs_BuildRequestRegisterCancel()
    {
        byte[] page = PartyBbsProtocol.BuildRequestPage(3);
        Assert.Equal(4, page.Length);
        Assert.Equal((byte)GameOpcode.WIZ_PARTY_BBS, page[0]);
        Assert.Equal(PartyBbsProtocol.Data, page[1]);
        Assert.Equal((short)3, BitConverter.ToInt16(page, 2));

        Assert.Equal(new byte[] { (byte)GameOpcode.WIZ_PARTY_BBS, PartyBbsProtocol.Register }, PartyBbsProtocol.BuildRegister());
        Assert.Equal(new byte[] { (byte)GameOpcode.WIZ_PARTY_BBS, PartyBbsProtocol.Cancel }, PartyBbsProtocol.BuildCancel());
    }

    // Build a WIZ_PARTY_BBS list body exactly as GameUser.Bbs.cs::PartyBbsList does: header, then
    // exactly 23 rows, then [page][total].
    private static byte[] BuildBbsList(byte type, IReadOnlyList<PartyBbsEntry> rows, short page, short total)
    {
        var p = new Pkt().Byte((byte)GameOpcode.WIZ_PARTY_BBS).Byte(type).Byte(1);
        for (int i = 0; i < PartyBbsProtocol.RowsPerPage; i++)
        {
            if (i < rows.Count)
                p.Str2(rows[i].Name).Byte(rows[i].Level).Short(rows[i].Class);
            else
                p.Short(0).Byte(0).Short(0); // padding row (nameLen 0)
        }

        p.Short(page).Short(total);
        return p.Done();
    }

    [Fact]
    public void PartyBbs_ParseList_ReadsExactly23RowsThenFooter()
    {
        PartyBbsEntry[] rows =
        [
            new("Alice", 42, 1),
            new("Bob", 55, 5),
        ];
        byte[] body = BuildBbsList(PartyBbsProtocol.Data, rows, page: 2, total: 25);

        PartyBbsPage reply = PartyBbsProtocol.ParseList(body);
        Assert.True(reply.Ok);
        Assert.Equal(PartyBbsProtocol.Data, reply.Type);
        Assert.Equal(2, reply.Rows.Count); // empty padding rows dropped
        Assert.Equal("Alice", reply.Rows[0].Name);
        Assert.Equal((byte)42, reply.Rows[0].Level);
        Assert.Equal((short)5, reply.Rows[1].Class);
        Assert.Equal((short)2, reply.Page);
        Assert.Equal((short)25, reply.Total);
    }

    [Fact]
    public void PartyBbs_ParseList_FailureResultHasNoBody()
    {
        byte[] body = new Pkt().Byte((byte)GameOpcode.WIZ_PARTY_BBS).Byte(PartyBbsProtocol.Data).Byte(0).Done();
        PartyBbsPage reply = PartyBbsProtocol.ParseList(body);
        Assert.False(reply.Ok);
        Assert.Empty(reply.Rows);
    }

    [Fact]
    public void PartyBbsDialog_PagingRegisterAndWhisper()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var node = new N3UiBase { Id = "partybbs", Region = Rect(0, 0, 300, 300) };
        node.Children.Add(new N3UiList { Id = "List_Infos", Region = Rect(0, 0, 200, 200), FontHeight = 16 });
        node.Children.Add(Str("string_page"));
        foreach (string id in (string[])["btn_page_up", "btn_page_down", "btn_refresh", "btn_add", "btn_delete", "btn_whisper", "btn_Party", "btn_exit"])
            node.Children.Add(Btn(id));
        UiControl root = UiControlFactory.Build(node);
        var dialog = new PartyBbsDialog(context, root);
        dialog.Bind(context.InGame);

        // Open → request page 0.
        dialog.Open();
        Assert.Equal(PartyBbsProtocol.Data, client.Last[1]);
        Assert.Equal((short)0, BitConverter.ToInt16(client.Last, 2));

        // Reply: two rows across 25 total seekers (2 pages).
        PartyBbsEntry[] rows = [new("Alice", 42, 1), new("Bob", 55, 5)];
        context.InGame.PartyBbsReceived!(PartyBbsProtocol.ParseList(BuildBbsList(PartyBbsProtocol.Data, rows, 0, 25)));
        Assert.True(root.Visible);
        Assert.Equal(2, dialog.MaxPage);
        UiListControl list = root.GetChildById<UiListControl>("List_Infos")!;
        Assert.Equal(2, list.Count);
        Assert.Equal("1", root.GetChildById<UiStringControl>("string_page")!.Text);

        // Page down → request page 1.
        root.ReceiveMessage(root.GetChildById<UiButton>("btn_page_down")!, UiMsg.ButtonClick);
        Assert.Equal(PartyBbsProtocol.Data, client.Last[1]);
        Assert.Equal((short)1, BitConverter.ToInt16(client.Last, 2));

        // Reply for page 1 clears the processing latch; then register.
        context.InGame.PartyBbsReceived!(PartyBbsProtocol.ParseList(BuildBbsList(PartyBbsProtocol.Data, rows, 1, 25)));
        root.ReceiveMessage(root.GetChildById<UiButton>("btn_add")!, UiMsg.ButtonClick);
        Assert.Equal(PartyBbsProtocol.Register, client.Last[1]);

        // Reply to clear latch, then whisper the selected (first) row.
        context.InGame.PartyBbsReceived!(PartyBbsProtocol.ParseList(BuildBbsList(PartyBbsProtocol.Register, rows, 0, 25)));
        list.SetCurSel(0);
        root.ReceiveMessage(root.GetChildById<UiButton>("btn_whisper")!, UiMsg.ButtonClick);
        Assert.Equal((byte)GameOpcode.WIZ_CHAT_TARGET, client.Last[0]);
        Assert.Equal(0x01, client.Last[1]);
        Assert.Equal("Alice", Encoding.ASCII.GetString(client.Last, 4, client.Last[2]));
    }

    // ======================================================================
    // FriendProtocol — WIZ_FRIEND_PROCESS (0x49)
    // ======================================================================

    [Fact]
    public void Friend_BuildRequest_CountThenLengthPrefixedNames()
    {
        byte[] p = FriendProtocol.BuildRequest(["Alice", "Bo"]);
        Assert.Equal((byte)GameOpcode.WIZ_FRIEND_PROCESS, p[0]);
        Assert.Equal((short)2, BitConverter.ToInt16(p, 1));
        Assert.Equal((short)5, BitConverter.ToInt16(p, 3));
        Assert.Equal("Alice", Encoding.ASCII.GetString(p, 5, 5));
        Assert.Equal((short)2, BitConverter.ToInt16(p, 10));
        Assert.Equal("Bo", Encoding.ASCII.GetString(p, 12, 2));
        Assert.Equal(14, p.Length);
    }

    [Fact]
    public void Friend_ParseReply_ReadsIdAndStatusBits()
    {
        byte[] body = new Pkt()
            .Byte((byte)GameOpcode.WIZ_FRIEND_PROCESS)
            .Short(2)
            .Str2("Alice").Short(11).Byte(0x03) // online + in party
            .Str2("Bob").Short(22).Byte(0x00)    // offline
            .Done();

        IReadOnlyList<FriendStatus> list = FriendProtocol.ParseReply(body);
        Assert.Equal(2, list.Count);
        Assert.Equal("Alice", list[0].Name);
        Assert.Equal((short)11, list[0].Id);
        Assert.True(list[0].Online);
        Assert.True(list[0].InParty);
        Assert.False(list[1].Online);
        Assert.False(list[1].InParty);
    }

    // ======================================================================
    // Friends dialog (a page of CUIVarious)
    // ======================================================================

    private static (VariousDialog Dialog, UiControl Root, FakeGameClient Client, GameContext Context, InMemoryFriendStore Store) BuildFriends()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var node = new N3UiBase { Id = "various", Region = Rect(0, 0, 300, 300) };
        node.Children.Add(new N3UiList { Id = "List_Friends", Region = Rect(0, 0, 200, 200), FontHeight = 16 });
        node.Children.Add(Str("String_Page"));
        foreach (string id in (string[])["Btn_Add", "Btn_Delete", "Btn_Whisper", "Btn_Party", "Btn_Refresh", "Btn_Page_Up", "Btn_Page_Down", "btn_friends"])
            node.Children.Add(Btn(id));
        UiControl root = UiControlFactory.Build(node);

        var store = new InMemoryFriendStore();
        var dialog = new VariousDialog(context, root, friendStore: store);
        dialog.Bind(context.InGame);
        return (dialog, root, client, context, store);
    }

    [Fact]
    public void Friends_AddTargetSavesAndSendsQuery()
    {
        (VariousDialog dialog, UiControl root, FakeGameClient client, GameContext context, InMemoryFriendStore store) = BuildFriends();

        // A visible target player to add.
        var target = new RemotePlayer { Id = 77, Name = "Alice" };
        context.InGame.World.AddOrUpdate(target);
        dialog.TargetId = 77;

        Assert.True(dialog.AddFriend());
        Assert.Contains("Alice", dialog.FriendNames);
        Assert.Contains("Alice", store.Load());

        byte[] p = client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_FRIEND_PROCESS, p[0]);
        Assert.Equal((short)1, BitConverter.ToInt16(p, 1));
        Assert.Equal("Alice", Encoding.ASCII.GetString(p, 5, p[3]));

        // Adding the same name again is a no-op.
        Assert.False(dialog.AddFriend());
    }

    [Fact]
    public void Friends_DeleteRemovesAndPersists()
    {
        (VariousDialog dialog, UiControl root, _, _, InMemoryFriendStore store) = BuildFriends();

        Assert.True(dialog.MemberAdd("Alice", -1, false, false));
        Assert.True(dialog.MemberAdd("Bob", -1, false, false));
        dialog.UpdateFriendList();

        UiListControl list = root.GetChildById<UiListControl>("List_Friends")!;
        list.SetCurSel(0); // "Alice" (sorted)
        root.ReceiveMessage(root.GetChildById<UiButton>("Btn_Delete")!, UiMsg.ButtonClick);

        Assert.DoesNotContain("Alice", dialog.FriendNames);
        Assert.Contains("Bob", dialog.FriendNames);
        Assert.Equal(new[] { "Bob" }, store.Load());
    }

    [Fact]
    public void Friends_LoadFromStore_PopulatesOnConstruction()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        var store = new InMemoryFriendStore();
        store.Save(["Zed", "Amy"]);

        var node = new N3UiBase { Id = "various", Region = Rect(0, 0, 300, 300) };
        node.Children.Add(new N3UiList { Id = "List_Friends", Region = Rect(0, 0, 200, 200), FontHeight = 16 });
        UiControl root = UiControlFactory.Build(node);
        var dialog = new VariousDialog(context, root, friendStore: store);

        // Sorted (std::map order): Amy before Zed.
        Assert.Equal(new[] { "Amy", "Zed" }, dialog.FriendNames);
    }

    [Fact]
    public void Friends_StatusReplyIsInert_UpdatesTrackedFlagsOnly()
    {
        (VariousDialog dialog, _, _, GameContext context, _) = BuildFriends();
        dialog.MemberAdd("Alice", -1, false, false);

        // The server never sends this (no-op upstream); driving it directly must not throw and
        // only updates the tracked entry — there is no live status in play.
        context.InGame.FriendsReceived!([new FriendStatus("Alice", 11, 0x01)]);
        Assert.Contains("Alice", dialog.FriendNames);
    }

    // ======================================================================
    // Inventory repair mode (CUIInventory INV_STATE_REPAIR + CItemRepairMgr)
    // ======================================================================

    // Item basic columns 0..23 mirroring ItemBasicRow.FromCells (Price at 21, MaxDurability at 20).
    private static readonly TblType[] BasicColumns =
    [
        TblType.Dword, TblType.Byte, TblType.String, TblType.String, TblType.Dword, TblType.Byte,
        TblType.Dword, TblType.Dword, TblType.Dword, TblType.Dword,
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
        TblType.Dword, TblType.Dword, TblType.Short, TblType.Byte,
    ];

    private static object[] BasicRow(uint id, uint iconId, short maxDur, int price, bool countable) =>
    [
        id, (byte)0, "item", "", 0u, (byte)0,
        0u, iconId, 0u, 0u,
        (byte)0, (byte)0, (byte)1, (byte)0, (byte)0,
        (short)0, (short)0, (short)0, (short)10, maxDur,
        (uint)price, 0u, (short)0, (byte)(countable ? 1 : 0),
    ];

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

    // Ext table columns: id(0), header(1), then shorts through PriceMultiply(13). MaxDurability at
    // 12, PriceMultiply at 13 (ItemExtRow.FromCells). 14 columns is enough (TblCell guards range).
    private static readonly TblType[] ExtColumns =
    [
        TblType.Dword, TblType.String,
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
    ];

    private static object[] ExtRow(uint id, short maxDur, short priceMultiply) =>
    [
        id, "",
        (short)0, (short)0, (short)0, (short)0, (short)0, (short)0,
        (short)0, (short)0, (short)0, (short)0, maxDur, priceMultiply,
    ];

    private static ItemTableSet BuildItems()
    {
        // Price 10000, maxDur 100. ExtIndex 0 (basic col 1), ext row id = itemId % 1000 = 0 with
        // PriceMultiply 1 → allPrice = 10000 (CItemRepairMgr uses iPrice * siPriceMultiply).
        N3TableFile basic = BuildTable(BasicColumns, [BasicRow(Helmet, 100010004u, 100, 10000, countable: false)]);
        var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
        exts[0] = BuildTable(ExtColumns, [ExtRow(0, maxDur: 0, priceMultiply: 1)]);
        return new ItemTableSet(basic, exts);
    }

    private sealed record RepairHarness(
        InventoryDialog Dialog, UiControl Root, Inventory Inv, LocalPlayer Local, FakeGameClient Client);

    private static RepairHarness BuildRepairInventory()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);

        var root = new N3UiBase { Id = "inventory", Region = Rect(0, 0, 800, 400) };
        for (int i = 0; i < Inventory.EquipSlotCount; i++)
            root.Children.Add(Area(i, UiAreaType.Slot, Rect(i * 40, 0, i * 40 + 30, 30)));
        for (int i = 0; i < Inventory.BackpackSlotCount; i++)
            root.Children.Add(Area(i, UiAreaType.Inv, Rect(i * 20, 100, i * 20 + 18, 130)));
        root.Children.Add(Str("text_weight"));
        root.Children.Add(Btn("btn_close"));
        UiControl uiRoot = UiControlFactory.Build(root);

        var dialog = new InventoryDialog(context, uiRoot, BuildItems(), new IconDragState());
        dialog.Bind(context.InGame);
        return new RepairHarness(dialog, uiRoot, context.InGame.Inventory, context.InGame.World.Local, client);
    }

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    [Fact]
    public void Repair_HoverComputesPrice_AndClickWithGoldSends()
    {
        RepairHarness h = BuildRepairInventory();
        // A worn helmet at half durability (50/100). Equip slot 1 (Head).
        int flat = (int)EquipSlot.Head;
        h.Inv.Set(flat, new InventoryItem(Helmet, 1, 50));
        h.Local.Gold = 100000;
        h.Dialog.Open(repair: true);
        Assert.Equal(InventoryMode.Repair, h.Dialog.Mode);

        UiIconControl icon = h.Dialog.EquipIcons[flat]!;
        var cursor = new UiPoint(icon.Region.Left + 1, icon.Region.Top + 1);

        // Hover → price = RepairCost(10000, 50, 100) = 500, affordable.
        InventoryDialog.RepairHoverInfo info = h.Dialog.RepairHover(cursor)!.Value;
        Assert.Equal(500, info.Cost);
        Assert.Equal(RepairProtocol.ArmEquip, info.Arm);
        Assert.Equal(flat, info.Order);
        Assert.True(info.HaveEnough);

        // Click (press + release on the same icon) → WIZ_ITEM_REPAIR.
        h.Dialog.Cursor = cursor;
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        Msg(h.Root, icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_REPAIR, p[0]);
        Assert.Equal(RepairProtocol.ArmEquip, p[1]);
        Assert.Equal(flat, p[2]);
        Assert.Equal((uint)Helmet, BitConverter.ToUInt32(p, 3));
    }

    [Fact]
    public void Repair_ClickWithoutGold_DoesNotSend_RaisesLackGold()
    {
        RepairHarness h = BuildRepairInventory();
        int flat = (int)EquipSlot.Head;
        h.Inv.Set(flat, new InventoryItem(Helmet, 1, 50));
        h.Local.Gold = 10; // far below the 500 cost
        h.Dialog.Open(repair: true);

        bool lack = false;
        h.Dialog.RepairLackGold += () => lack = true;

        UiIconControl icon = h.Dialog.EquipIcons[flat]!;
        h.Dialog.Cursor = new UiPoint(icon.Region.Left + 1, icon.Region.Top + 1);
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        Msg(h.Root, icon, UiMsg.IconUp);

        Assert.True(lack);
        Assert.Empty(h.Client.Sent);
    }

    [Fact]
    public void Repair_SuccessReply_RestoresDurabilityClearsExhaustAndUpdatesGold()
    {
        RepairHarness h = BuildRepairInventory();
        int flat = (int)EquipSlot.Head;
        h.Inv.Set(flat, new InventoryItem(Helmet, 1, 0)); // durability 0 → exhausted
        h.Local.Gold = 100000;
        h.Dialog.Open(repair: true);

        UiIconControl icon = h.Dialog.EquipIcons[flat]!;
        Assert.True(icon.DurabilityExhausted);

        h.Dialog.Cursor = new UiPoint(icon.Region.Left + 1, icon.Region.Top + 1);
        Msg(h.Root, icon, UiMsg.IconDownFirst);
        Msg(h.Root, icon, UiMsg.IconUp);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_REPAIR, h.Client.Last[0]);

        // Server confirms: durability restored to max (100), gold updated, exhaust cleared.
        h.Dialog.OnRepairResult(new RepairResult(true, 90000));
        Assert.Equal((short)100, h.Inv.Get(flat)!.Durability);
        Assert.Equal(90000, h.Local.Gold);
        Assert.False(h.Dialog.EquipIcons[flat]!.DurabilityExhausted);
    }
}
