using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Aujard;
using Xunit;

namespace OpenKO.Servers.Tests;

public class UserDataBlobCodecTests
{
    private static readonly Dictionary<int, ItemRow> Items = new()
    {
        [120010000] = new ItemRow(120010000, 0),          // non-stackable weapon
        [910100000] = new ItemRow(910100000, 1),          // stackable (countable)
        [900000000] = new ItemRow(900000000, 2),          // coins
    };

    private static ItemRow? Lookup(int id) => Items.GetValueOrDefault(id);

    [Fact]
    public void InventoryBlobsRoundTrip()
    {
        var user = new UserData();
        user.Items[0] = new ItemData { Num = 120010000, Duration = 5000, Count = 1, SerialNum = 0x1122334455667788 };
        user.Items[GameConstants.SlotMax] = new ItemData { Num = 910100000, Duration = 0, Count = 250, SerialNum = 42 };

        (byte[] items, byte[] serials) = UserDataBlobCodec.BuildInventoryBlobs(user);

        Assert.Equal(GameConstants.InventoryTotal * 8, items.Length);
        Assert.Equal(GameConstants.InventoryTotal * 8, serials.Length);

        var reloaded = new UserData();
        UserDataBlobCodec.ApplyInventoryBlobs(reloaded, items, serials, Lookup);

        Assert.Equal(120010000, reloaded.Items[0].Num);
        Assert.Equal(5000, reloaded.Items[0].Duration);
        Assert.Equal(1, reloaded.Items[0].Count);
        Assert.Equal(0x1122334455667788, reloaded.Items[0].SerialNum);
        Assert.Equal(250, reloaded.Items[GameConstants.SlotMax].Count);
    }

    [Fact]
    public void UnknownItemsAreDropped()
    {
        var user = new UserData();
        user.Items[3] = new ItemData { Num = 999999999, Duration = 1, Count = 1, SerialNum = 7 };

        (byte[] items, byte[] serials) = UserDataBlobCodec.BuildInventoryBlobs(user);

        var reloaded = new UserData();
        var dropped = new List<int>();
        UserDataBlobCodec.ApplyInventoryBlobs(reloaded, items, serials, Lookup, dropped.Add);

        Assert.Equal(0, reloaded.Items[3].Num);
        Assert.Equal([999999999], dropped);
    }

    [Fact]
    public void CountablesWithZeroCountAreWiped_CountsAboveMaxClamp()
    {
        var user = new UserData();
        user.Items[1] = new ItemData { Num = 910100000, Duration = 0, Count = 0, SerialNum = 1 };  // countable, 0 → wiped
        user.Items[2] = new ItemData { Num = 120010000, Duration = 9, Count = 12000, SerialNum = 2 }; // > 9999 → clamp

        (byte[] items, byte[] serials) = UserDataBlobCodec.BuildInventoryBlobs(user);

        var reloaded = new UserData();
        UserDataBlobCodec.ApplyInventoryBlobs(reloaded, items, serials, Lookup);

        Assert.Equal(0, reloaded.Items[1].Num);
        Assert.Equal(GameConstants.ItemCountMax, reloaded.Items[2].Count);
    }

    [Fact]
    public void ShortBlobsReadAsZeroes()
    {
        var user = new UserData();
        // 8-byte items blob only covers slot 0 partially; everything reads as zero/default.
        UserDataBlobCodec.ApplyInventoryBlobs(user, new byte[8], new byte[4], Lookup);
        Assert.All(user.Items, item => Assert.Equal(0, item.Num));

        UserDataBlobCodec.ApplySkillsBlob(user, new byte[3]);
        Assert.All(user.Skills, skill => Assert.Equal(0, skill));
    }

    [Fact]
    public void QuestBlobRoundTripAndValidation()
    {
        var user = new UserData();
        user.Quests[0] = new UserQuest { QuestId = 42, QuestState = 2 };
        user.Quests[1] = new UserQuest { QuestId = 101, QuestState = 1 };  // id > 100 → wiped
        user.Quests[2] = new UserQuest { QuestId = 7, QuestState = 4 };    // state > 3 → wiped
        user.Quests[3] = new UserQuest { QuestId = 99, QuestState = 3 };

        (byte[] blob, short total) = UserDataBlobCodec.BuildQuestBlob(user);

        Assert.Equal(GameConstants.MaxQuest * 3, blob.Length);
        Assert.Equal(2, total);
        Assert.Equal(0, user.Quests[1].QuestId); // wiped in place, like the C++

        var reloaded = new UserData();
        short reloadedTotal = UserDataBlobCodec.ApplyQuestBlob(reloaded, blob);
        Assert.Equal(2, reloadedTotal);
        Assert.Equal(42, reloaded.Quests[0].QuestId);
        Assert.Equal(99, reloaded.Quests[3].QuestId);
    }

    [Fact]
    public void WarehouseBlobsRoundTrip_ZeroCountBecomesOne()
    {
        var user = new UserData();
        user.Warehouse[0] = new WarehouseItemData { Num = 120010000, Duration = 100, Count = 0, SerialNum = 5 };
        user.Warehouse[191] = new WarehouseItemData { Num = 910100000, Duration = 0, Count = 30, SerialNum = 6 };

        (byte[] items, byte[] serials) = UserDataBlobCodec.BuildWarehouseBlobs(user);

        Assert.Equal(GameConstants.WarehouseMax * 8, items.Length);
        Assert.Equal(GameConstants.WarehouseMax * 8, serials.Length);

        var reloaded = new UserData();
        UserDataBlobCodec.ApplyWarehouseBlobs(reloaded, items, serials, Lookup);

        Assert.Equal(1, reloaded.Warehouse[0].Count); // count <= 0 floors to 1
        Assert.Equal(30, reloaded.Warehouse[191].Count);
        Assert.Equal(6, reloaded.Warehouse[191].SerialNum);
    }

    [Theory]
    [InlineData(101, 120010000, 5000)]
    [InlineData(104, 190010000, 10000)]
    [InlineData(204, 190050000, 10000)]
    public void StarterWeaponForFreshCharacters(short cls, int expectedItem, short expectedDuration)
    {
        var user = new UserData { Level = 1, Exp = 0, Gold = 0, Class = cls };

        UserDataBlobCodec.ApplyStarterWeapon(user);

        Assert.Equal(expectedItem, user.Items[GameConstants.SlotMax].Num);
        Assert.Equal(expectedDuration, user.Items[GameConstants.SlotMax].Duration);
    }

    [Fact]
    public void NoStarterWeaponForExistingCharacters()
    {
        var user = new UserData { Level = 5, Exp = 100, Gold = 50, Class = 101 };

        UserDataBlobCodec.ApplyStarterWeapon(user);

        Assert.Equal(0, user.Items[GameConstants.SlotMax].Num);
    }

    [Fact]
    public void UserDataStoreFindAndReset()
    {
        var store = new UserDataStore(4);
        store.Get(2)!.CharId = "Knight";
        store.Get(2)!.Gold = 1000;

        Assert.NotNull(store.FindByCharId("knight", out int userId));
        Assert.Equal(2, userId);
        Assert.Null(store.FindByCharId("nobody", out _));

        store.Reset(2);
        Assert.Equal(string.Empty, store.Get(2)!.CharId);
        Assert.Equal(0, store.Get(2)!.Gold);
        Assert.Equal(GameConstants.AuthorityUser, store.Get(2)!.Authority);
    }
}
