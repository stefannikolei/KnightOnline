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
/// Sub-slice 9.5-4 pins: the dropped-item loot box (populate + WIZ_ITEM_GET take-item), the
/// item / repair tooltip compose passes and the countable stack-split modal popup. Fully
/// headless over synthetic .uif trees and item rows.
/// </summary>
public class LootTooltipDialogTests
{
    private const uint Sword = 200030000;   // ext index 0, ext row id 0
    private const uint Potion = 100040000;  // countable, ext index 0, ext row id 0

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

    // ---- Synthetic item table (basic + one ext at index 0) -----------------

    private static readonly TblType[] BasicColumns =
    [
        TblType.Dword, TblType.Byte, TblType.String, TblType.String, TblType.Dword, TblType.Byte,
        TblType.Dword, TblType.Dword, TblType.Dword, TblType.Dword,
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short,
        TblType.Int, TblType.Int, TblType.Short, TblType.Byte,
    ];

    private static object[] BasicRow(uint id, uint iconId, KoItemPosition attach, short maxDur, bool countable) =>
    [
        id, (byte)0, "item", "", 0u, (byte)0,
        0u, iconId, 0u, 0u,
        (byte)0, (byte)0, (byte)attach, (byte)0, (byte)0,
        (short)0, (short)0, (short)0, (short)10, maxDur,
        0, 0, (short)0, (byte)(countable ? 1 : 0),
    ];

    // Ext table: only the leading id column is needed for a successful Find (short rows guarded).
    private static readonly TblType[] ExtColumns = [TblType.Dword];

    private static ItemTableSet BuildItems()
    {
        N3TableFile basic = BuildTable(BasicColumns,
        [
            BasicRow(Sword, 200030001u, KoItemPosition.RightHand, 4000, countable: false),
            BasicRow(Potion, 100040001u, KoItemPosition.Inventory, 0, countable: true),
        ]);
        N3TableFile ext = BuildTable(ExtColumns, [[0u]]); // ext row id 0 (itemId % 1000)
        var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
        exts[0] = ext;
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
        }

        w.Flush();
        return N3TableFile.Load(ms.ToArray(), encrypted: false);
    }

    // ---- DroppedItemDialog --------------------------------------------------

    private static UiControl BuildDropRoot()
    {
        var root = new N3UiBase { Id = "droppeditem", Region = Rect(0, 0, 400, 300) };
        for (int i = 0; i < DroppedItemDialog.MaxPieces; i++)
        {
            root.Children.Add(new N3UiArea
            {
                Id = i.ToString(),
                AreaType = (int)UiAreaType.DropItem,
                Region = Rect(i * 40, 0, i * 40 + 30, 30),
            });
            root.Children.Add(new N3UiString { Id = i.ToString(), Region = Rect(i * 40, 32, i * 40 + 30, 48) });
        }

        return UiControlFactory.Build(root);
    }

    private sealed record DropHarness(DroppedItemDialog Dialog, UiControl Root, FakeGameClient Client, GameContext Context);

    private static DropHarness BuildDrop()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiControl root = BuildDropRoot();
        var dialog = new DroppedItemDialog(context, root, BuildItems(), new IconDragState());
        return new DropHarness(dialog, root, client, context);
    }

    [Fact]
    public void Populate_PlacesIconsAndShowsCountForStackables()
    {
        DropHarness h = BuildDrop();
        h.Dialog.Populate(0x1234, [new LootItem(0, Sword, 1), new LootItem(2, Potion, 25)]);

        Assert.True(h.Root.Visible);
        Assert.Equal(0x1234u, h.Dialog.BundleId);

        UiIconControl sword = h.Dialog.Icons[0]!;
        Assert.True(sword.Visible);
        Assert.StartsWith(@"UI\ItemIcon_", sword.IconTexture);

        // Row 1 was empty in the bundle; its icon stays hidden.
        Assert.False(h.Dialog.Icons[1]!.Visible);

        UiIconControl potion = h.Dialog.Icons[2]!;
        Assert.True(potion.Visible);
        // The stackable's count label shows; the sword's is hidden.
        UiStringControl potionCount = h.Root.GetChildById<UiStringControl>("2")!;
        Assert.True(potionCount.Visible);
        Assert.Equal("25", potionCount.Text);
    }

    [Fact]
    public void TakeItem_SendsItemGetOncePerRow()
    {
        DropHarness h = BuildDrop();
        h.Dialog.Populate(0x1234, [new LootItem(0, Sword, 1)]);

        UiIconControl icon = h.Dialog.Icons[0]!;
        h.Root.ReceiveMessage(icon, UiMsg.IconDownFirst);
        h.Root.ReceiveMessage(icon, UiMsg.IconUp);

        byte[] p = h.Client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_GET, p[0]);
        Assert.Equal(0x1234u, BitConverter.ToUInt32(p, 1));
        Assert.Equal(Sword, BitConverter.ToUInt32(p, 5));
        Assert.Equal(9, p.Length);

        // A second down/up on the same row does not re-send.
        h.Root.ReceiveMessage(icon, UiMsg.IconDownFirst);
        h.Root.ReceiveMessage(icon, UiMsg.IconUp);
        Assert.Single(h.Client.Sent);
    }

    [Fact]
    public void OnGetResult_RoutesPickupIntoInventoryAndHidesWhenEmpty()
    {
        DropHarness h = BuildDrop();
        h.Dialog.Populate(0x1234, [new LootItem(0, Sword, 1)]);

        bool refreshed = false;
        h.Dialog.InventoryChanged += () => refreshed = true;

        h.Dialog.OnGetResult(new ItemGetResult(0x01, Pos: 3, ItemId: Sword, Count: 1, GoldId: 0, CharacterName: ""));

        InventoryItem? placed = h.Context.InGame.Inventory.Get(Inventory.BackpackIndex(3));
        Assert.NotNull(placed);
        Assert.Equal((int)Sword, placed!.ItemId);
        Assert.True(refreshed);
        Assert.False(h.Dialog.Icons[0]!.Visible);
        Assert.False(h.Root.Visible); // last row taken → dialog hides
    }

    [Fact]
    public void SendBundleOpen_WritesExpectedBytesAndTracksId()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        context.InGame.SendBundleOpen(0xABCD);

        byte[] p = client.Last;
        Assert.Equal((byte)GameOpcode.WIZ_BUNDLE_OPEN_REQ, p[0]);
        Assert.Equal(0xABCDu, BitConverter.ToUInt32(p, 1));
        Assert.Equal(5, p.Length);
        Assert.Equal(0xABCDu, context.InGame.PendingBundleId);
    }

    [Fact]
    public void ParseBundleOpen_SkipsEmptySlotsKeepsOrder()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)GameOpcode.WIZ_BUNDLE_OPEN_REQ);
        // slot 0 = sword, slot 1 empty, slot 2 = potion(25), remaining empty.
        (uint id, short count)[] slots = [(Sword, 1), (0u, 0), (Potion, 25), (0u, 0), (0u, 0), (0u, 0)];
        foreach ((uint id, short count) in slots)
        {
            w.Write(id);
            w.Write(count);
        }

        w.Flush();

        IReadOnlyList<LootItem> items = ItemProtocol.ParseBundleOpen(ms.ToArray());
        Assert.Equal(2, items.Count);
        Assert.Equal(new LootItem(0, Sword, 1), items[0]);
        Assert.Equal(new LootItem(2, Potion, 25), items[1]);
    }

    // ---- ItemTooltip compose ------------------------------------------------

    [Fact]
    public void ItemTooltipCompose_WeaponHasNameDamageDurabilityPriceAndRedRequirement()
    {
        var basic = new ItemBasicRow
        {
            Name = "Raptor",
            AttachPoint = KoItemPosition.RightHand,
            Damage = 100,
            MaxDurability = 5000,
            Price = 1000,
            NeedStrength = 200,
        };
        var ext = new ItemExtRow { Id = 0, PriceMultiply = 1, MagicOrRare = 0 };

        var player = new TooltipPlayer(
            Race: 1, Level: 30, Rank: 0, Title: 0,
            Strength: 50, Stamina: 50, Dexterity: 50, Intelligence: 50, MagicAttack: 50,
            Gold: 999999);

        IReadOnlyList<TooltipLine> lines = ItemTooltipControl.Compose(
            basic, ext, durability: 5000, count: 1, player, showPrice: true, isBuy: true);

        // Name is line 0, white for a general item.
        Assert.Equal("Raptor", lines[0].Text);
        Assert.Equal(TooltipColor.White, lines[0].Color);

        Assert.Contains(lines, l => l.Text.Contains("Attack Power: 100"));
        Assert.Contains(lines, l => l.Text.Contains("Max Durability: 5000"));
        Assert.Contains(lines, l => l.Text.Contains("Durability: 5000"));
        Assert.Contains(lines, l => l.Text.Contains("Purchase Price: 1000"));

        // The unmet STR requirement is red.
        TooltipLine str = Assert.Single(lines, l => l.Text.Contains("Required STR: 200"));
        Assert.Equal(TooltipColor.Red, str.Color);
    }

    [Fact]
    public void ItemTooltipCompose_MetRequirementIsWhite_AndUniqueUsesHeader()
    {
        var basic = new ItemBasicRow { Name = "Base", AttachPoint = KoItemPosition.RightHand, NeedStrength = 30 };
        var ext = new ItemExtRow { Id = 0, PriceMultiply = 1, MagicOrRare = 4, Header = "Legendary Blade" };

        var player = new TooltipPlayer(1, 60, 0, 0, 100, 100, 100, 100, 100, 0);
        IReadOnlyList<TooltipLine> lines = ItemTooltipControl.Compose(basic, ext, 0, 1, player);

        // Unique (attrib 4) shows the ext header as the (gold) name.
        Assert.Equal("Legendary Blade", lines[0].Text);
        Assert.Equal(TooltipColor.Gold, lines[0].Color);

        TooltipLine str = Assert.Single(lines, l => l.Text.Contains("Required STR: 30"));
        Assert.Equal(TooltipColor.White, str.Color);
    }

    [Fact]
    public void ItemTooltipCompose_GoldIsASingleWhiteLine()
    {
        var basic = new ItemBasicRow { Name = "Noah", AttachPoint = KoItemPosition.Gold };
        var ext = new ItemExtRow { Id = 0, PriceMultiply = 1 };

        IReadOnlyList<TooltipLine> lines = ItemTooltipControl.Compose(basic, ext, 0, 5000, null);
        TooltipLine only = Assert.Single(lines);
        Assert.Contains("Noah", only.Text);
        Assert.Contains("5000", only.Text);
        Assert.Equal(TooltipColor.White, only.Color);
    }

    // ---- RepairTooltip compose ---------------------------------------------

    [Fact]
    public void RepairTooltipCompose_RepairableShowsPriceAndDurability()
    {
        var basic = new ItemBasicRow { Name = "Raptor", MaxDurability = 5000, Countable = false };
        var ext = new ItemExtRow { Id = 0, PriceMultiply = 1 };

        RepairTooltipData data = RepairTooltipControl.Compose(basic, ext, durability: 2500, requiredGold: 500, haveEnough: false);

        Assert.NotNull(data.RepairGold);
        Assert.Contains("500", data.RepairGold!.Value.Text);
        Assert.Equal(TooltipColor.Red, data.RepairGold!.Value.Color); // not enough gold → red
        Assert.Contains("5000", data.DurMax!.Value.Text);
        Assert.Contains("2500", data.DurCurrent!.Value.Text);
        Assert.NotNull(data.Title);
    }

    [Fact]
    public void RepairTooltipCompose_CountableCannotBeRepaired()
    {
        var basic = new ItemBasicRow { Name = "Potion", MaxDurability = 0, Countable = true };
        var ext = new ItemExtRow { Id = 0, PriceMultiply = 1 };

        RepairTooltipData data = RepairTooltipControl.Compose(basic, ext, 0, 100, true);

        Assert.Null(data.RepairGold);
        Assert.Null(data.DurMax);
        Assert.Null(data.DurCurrent);
        Assert.NotNull(data.Title);
        Assert.Contains("Cannot Repair", data.Title!.Value.Text);
    }

    // ---- CountableItemEditDialog -------------------------------------------

    private static UiControl BuildEditRoot()
    {
        var root = new N3UiBase { Id = CountableItemEditDialog.RootId, Region = Rect(0, 0, 200, 120) };
        root.Children.Add(new N3UiEdit { Id = "edit_trade", Region = Rect(10, 10, 190, 30) });
        root.Children.Add(new N3UiButton { Id = "btn_ok", Region = Rect(10, 40, 90, 70) });
        root.Children.Add(new N3UiButton { Id = "btn_cancel", Region = Rect(100, 40, 190, 70) });
        root.Children.Add(new N3UiString { Id = "String_PersonTradeEdit_Msg", Region = Rect(10, 80, 190, 100) });
        return UiControlFactory.Build(root);
    }

    [Fact]
    public void CountableEdit_OpenLocksModal_OkClampsAndDispatches_CloseClears()
    {
        var manager = new UiManager();
        UiControl root = BuildEditRoot();
        manager.Add(root);
        var dialog = new CountableItemEditDialog(manager, root);

        int? got = null;
        dialog.Open(max: 10, onOk: v => got = v);

        Assert.True(dialog.IsLocked);
        Assert.Equal(CountableItemEditDialog.RootId, manager.ModalId);
        Assert.True(root.Visible);

        // Enter more than the max → clamped to the max.
        root.GetChildById<UiEditControl>("edit_trade")!.Text = "999";
        dialog.Ok();

        Assert.Equal(10, got);
        Assert.False(dialog.IsLocked);
        Assert.Null(manager.ModalId);
        Assert.False(root.Visible);
    }

    [Fact]
    public void CountableEdit_EnteredValuePassesThrough_AndButtonRoutingWorks()
    {
        var manager = new UiManager();
        UiControl root = BuildEditRoot();
        manager.Add(root);
        var dialog = new CountableItemEditDialog(manager, root);

        int? got = null;
        dialog.Open(max: 100, onOk: v => got = v);
        root.GetChildById<UiEditControl>("edit_trade")!.Text = "7";

        // Clicking btn_ok routes through ReceiveMessage → Ok().
        UiButton ok = root.GetChildById<UiButton>("btn_ok")!;
        root.ReceiveMessage(ok, UiMsg.ButtonClick);
        Assert.Equal(7, got);

        // Cancel dispatches nothing but still clears the lock.
        int? cancelled = null;
        dialog.Open(max: 100, onOk: v => cancelled = v);
        dialog.Cancel();
        Assert.Null(cancelled);
        Assert.Null(manager.ModalId);
    }

    // ---- Corpus -------------------------------------------------------------

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealLayouts_ExposeKeyIds()
    {
        string? root = FindDataRoot();
        if (root == null)
            return; // corpus not available

        var resolver = new KoPathResolver(root);
        var table = UiResourceTable.LoadFromFile(Path.Combine(root, "Data", "UIs_us.tbl"));

        UiControl drop = LoadLayout(resolver, table.DroppedItem(1));
        Assert.NotNull(drop.GetChildAreaByOrder(UiAreaType.DropItem, 0));

        UiControl info = LoadLayout(resolver, table.ItemInfo(1));
        Assert.NotNull(info.GetChildById<UiStringControl>("string_0"));

        UiControl repair = LoadLayout(resolver, table.RepairTooltip(1));
        Assert.NotNull(repair.GetChildById<UiStringControl>("string_repairgold"));

        UiControl edit = LoadLayout(resolver, table.CountableItemEdit(1));
        Assert.Equal(CountableItemEditDialog.RootId, edit.Id);
        Assert.NotNull(edit.GetChildById<UiEditControl>("edit_trade"));
        Assert.NotNull(edit.GetChildById<UiButton>("btn_ok"));
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
