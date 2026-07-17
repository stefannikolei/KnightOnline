using System.Text;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.5-2 pins: the inventory data model (equip/backpack indexing,
/// count/durability mutation) and the WIZ_MYINFO parse now preserving the
/// rental flag + time-remaining fields it used to discard.
/// </summary>
public class InventoryModelTests
{
    [Fact]
    public void EquipAndBackpack_IndexingBands()
    {
        Assert.Equal(14, Inventory.EquipSlotCount);
        Assert.Equal(28, Inventory.BackpackSlotCount);
        Assert.Equal(42, Inventory.InventorySlotCount);

        Assert.True(Inventory.IsEquipSlot(0));
        Assert.True(Inventory.IsEquipSlot(13));
        Assert.False(Inventory.IsEquipSlot(14));
        Assert.False(Inventory.IsEquipSlot(-1));

        Assert.True(Inventory.IsBackpackSlot(14));
        Assert.True(Inventory.IsBackpackSlot(41));
        Assert.False(Inventory.IsBackpackSlot(13));
        Assert.False(Inventory.IsBackpackSlot(42));

        Assert.Equal(14, Inventory.BackpackIndex(0));
        Assert.Equal(41, Inventory.BackpackIndex(27));

        // The wire order of EquipSlot matches ITEM_SLOT_POS_*.
        Assert.Equal(6, (int)EquipSlot.HandRight);
        Assert.Equal(8, (int)EquipSlot.HandLeft);
        Assert.Equal(13, (int)EquipSlot.Shoes);
    }

    [Fact]
    public void EquipItem_BackpackItem_ResolveThroughBands()
    {
        var inv = new Inventory();
        inv.Set((int)EquipSlot.HandRight, new InventoryItem(379001000, 1, 12000));
        inv.Set(Inventory.BackpackIndex(0), new InventoryItem(810004000, 99, 0));

        Assert.Equal(379001000, inv.EquipItem(EquipSlot.HandRight)!.ItemId);
        Assert.Null(inv.EquipItem(EquipSlot.Head));
        Assert.Equal(810004000, inv.BackpackItem(0)!.ItemId);
        Assert.Null(inv.BackpackItem(1));
    }

    [Fact]
    public void SetCount_SetDurability_Clear_Mutate()
    {
        var inv = new Inventory();
        inv.Set(20, new InventoryItem(500000000, 10, 5000));

        Assert.True(inv.SetCount(20, 3));
        Assert.Equal(3, inv.Get(20)!.Count);

        Assert.True(inv.SetDurability(20, 4200));
        Assert.Equal((short)4200, inv.Get(20)!.Durability);

        // Non-positive count clears the slot.
        Assert.True(inv.SetCount(20, 0));
        Assert.Null(inv.Get(20));

        // Mutating an empty slot is a no-op false.
        Assert.False(inv.SetCount(20, 5));
        Assert.False(inv.SetDurability(20, 100));
        Assert.False(inv.Clear(20));
    }

    [Fact]
    public void ParseMyInfo_PreservesRentalFlagAndTime()
    {
        var local = new LocalPlayer();
        var inv = new Inventory();
        WorldProtocol.ParseMyInfoInto(MyInfoPacket(), local, inv);

        // Equip slot 6 (right hand): non-rental, flag 0.
        InventoryItem? weapon = inv.Get(6);
        Assert.NotNull(weapon);
        Assert.Equal(379001000, weapon!.ItemId);
        Assert.Equal((byte)0, weapon.Flag);
        Assert.Equal((short)0, weapon.TimeRemaining);

        // Backpack slot 14: rental item — flag + time-remaining now retained.
        InventoryItem? rental = inv.Get(14);
        Assert.NotNull(rental);
        Assert.Equal(810004000, rental!.ItemId);
        Assert.Equal(99, rental.Count);
        Assert.Equal((byte)2, rental.Flag);
        Assert.Equal((short)3600, rental.TimeRemaining);
    }

    private static byte[] MyInfoPacket()
    {
        var buffer = new byte[1024];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_MYINFO);
        w.SetShort(77);
        w.SetString1(Encoding.Latin1.GetBytes("Hero"));
        w.SetShort(6600);
        w.SetShort(5400);
        w.SetShort(130);
        w.SetByte(1);                                      // nation
        w.SetByte(3);                                      // race
        w.SetShort(105);                                   // class
        w.SetByte(2);                                      // face
        w.SetByte(4);                                      // hair
        w.SetByte(0);                                      // rank
        w.SetByte(0);                                      // title
        w.SetByte(72);                                     // level
        w.SetByte(5);                                      // points
        w.SetDWord(9_999_999);                             // max exp
        w.SetDWord(1_000_000);                             // exp
        w.SetDWord(50);                                    // loyalty
        w.SetDWord(20);                                    // loyalty monthly
        w.SetByte(0);                                      // city
        w.SetShort(0);                                     // knights
        w.SetByte(0);                                      // fame
        w.SetShort(0);                                     // alliance knights
        w.SetByte(0);                                      // flag
        w.SetString1([]);                                  // clan name
        w.SetByte(0);                                      // grade
        w.SetByte(0);                                      // ranking
        w.SetShort(0);                                     // mark version
        w.SetShort(-1);                                    // cape
        w.SetShort(1500); w.SetShort(1490);                // max hp, hp
        w.SetShort(800); w.SetShort(790);                  // max mp, mp
        w.SetShort(3000); w.SetShort(1200);                // max/cur weight
        w.SetByte(120); w.SetByte(15);                     // str, item str
        w.SetByte(110); w.SetByte(0);                      // sta, item sta
        w.SetByte(90); w.SetByte(0);                       // dex, item dex
        w.SetByte(80); w.SetByte(0);                       // int, item int
        w.SetByte(70); w.SetByte(0);                       // cha, item cha
        w.SetShort(350); w.SetShort(420);                  // total hit, total ac
        w.SetByte(1); w.SetByte(2); w.SetByte(3);          // fire, cold, lightning
        w.SetByte(4); w.SetByte(5); w.SetByte(7);          // magic, disease, poison
        w.SetDWord(1_234_567);                             // gold
        w.SetByte(1);                                      // authority
        w.SetByte(0); w.SetByte(0);                        // knights rank, personal rank
        byte[] skills = [9, 8, 7, 6, 5, 4, 3, 2, 1];
        foreach (byte s in skills)
            w.SetByte(s);
        for (int i = 0; i < WorldProtocol.InventorySlotCount; i++)
        {
            uint num = i switch { 6 => 379001000u, 14 => 810004000u, _ => 0u };
            short count = i == 14 ? (short)99 : (short)0;
            byte flag = i == 14 ? (byte)2 : (byte)0;             // rental flag on the backpack item
            short timeRemaining = i == 14 ? (short)3600 : (short)0;
            w.SetDWord(num);
            w.SetShort(0);                                 // duration
            w.SetShort(count);                             // count
            w.SetByte(flag);                               // flag
            w.SetShort(timeRemaining);                     // time remaining
        }

        w.SetByte(0);                                      // account status
        w.SetByte(1);                                      // premium type
        w.SetShort(300);                                   // premium time
        w.SetByte(0);                                      // is chicken
        w.SetDWord(1000);                                  // manner point
        return w.Written.ToArray();
    }
}
