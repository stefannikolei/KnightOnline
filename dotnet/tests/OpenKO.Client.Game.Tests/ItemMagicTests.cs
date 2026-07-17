using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-7.4 pins: item-move + magic packets and the inventory model.</summary>
public class ItemMagicTests
{
    private sealed class CaptureClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public bool CryptionEnabled => true;

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public void EnableCryption(ulong publicKey) { }
    }

    [Fact]
    public void ItemMove_RequestLayout()
    {
        byte[] payload = ItemProtocol.BuildItemMove(ItemMoveDirection.InventoryToSlot, 379001000, 28, 5);
        var r = new PacketReader(payload);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_MOVE, r.GetByte());
        Assert.Equal((byte)ItemMoveDirection.InventoryToSlot, r.GetByte());
        Assert.Equal(379001000u, r.GetDWord());
        Assert.Equal(28, r.GetByte());
        Assert.Equal(5, r.GetByte());
    }

    [Fact]
    public void ItemMove_ResultByte()
    {
        Assert.True(ItemProtocol.ParseItemMoveSucceeded([(byte)GameOpcode.WIZ_ITEM_MOVE, 0x01]));
        Assert.False(ItemProtocol.ParseItemMoveSucceeded([(byte)GameOpcode.WIZ_ITEM_MOVE, 0x00]));
    }

    [Fact]
    public void Magic_BuildParseRoundTrips()
    {
        var packet = new MagicPacket(
            MagicProtocol.Casting, MagicId: 490051, SourceId: 77, TargetId: 42,
            Data1: 1, Data2: 2, Data3: 3, Data4: 4, Data5: 5, Data6: 6);

        MagicPacket parsed = MagicProtocol.Parse(MagicProtocol.Build(packet));
        Assert.Equal(packet, parsed);
    }

    [Fact]
    public void Inventory_MoveIntoEmpty_And_Swap()
    {
        var inv = new Inventory();
        inv.Set(28, new InventoryItem(379001000, 1, 15000));
        inv.Set(30, new InventoryItem(500000000, 5, 0));

        // Move into an empty equip slot.
        Assert.True(inv.MoveItem(28, 5));
        Assert.Null(inv.Get(28));
        Assert.Equal(379001000, inv.Get(5)!.ItemId);

        // Swap two occupied slots.
        Assert.True(inv.MoveItem(30, 5));
        Assert.Equal(500000000, inv.Get(5)!.ItemId);
        Assert.Equal(379001000, inv.Get(30)!.ItemId);

        // Empty source is a no-op.
        Assert.False(inv.MoveItem(99, 1));
    }

    [Fact]
    public void InGame_SendItemMove_UpdatesInventoryOptimistically()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        ctx.InGame.Inventory.Set(28, new InventoryItem(379001000, 1, 15000));
        ctx.InGame.SendItemMove(ItemMoveDirection.InventoryToSlot, 379001000, 28, 5);

        // Optimistic local move applied and the request queued.
        Assert.Null(ctx.InGame.Inventory.Get(28));
        Assert.Equal(379001000, ctx.InGame.Inventory.Get(5)!.ItemId);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_MOVE, client.Sent[^1][0]);

        // Server confirmation raises the result event.
        bool? result = null;
        ctx.InGame.ItemMoveResult = res => result = res.Success;
        ctx.Machine.DispatchPacket([(byte)GameOpcode.WIZ_ITEM_MOVE, 0x01]);
        Assert.True(result);
    }

    [Fact]
    public void InGame_MagicBroadcast_RaisesEvent()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        MagicPacket? received = null;
        ctx.InGame.MagicReceived = m => received = m;
        var cast = new MagicPacket(MagicProtocol.Effecting, 490051, 42, 77, 0, 0, 0, 0, 0, 0);
        ctx.Machine.DispatchPacket(MagicProtocol.Build(cast));

        Assert.NotNull(received);
        Assert.Equal(490051, received!.Value.MagicId);
        Assert.Equal((short)42, received.Value.SourceId);
    }
}
