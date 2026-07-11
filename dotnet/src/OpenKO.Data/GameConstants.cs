namespace OpenKO.Data;

/// <summary>Constants from <c>shared/globals.h</c> and <c>Server/Aujard/Define.h</c>.</summary>
public static class GameConstants
{
    /// <summary>14 equipped item slots.</summary>
    public const int SlotMax = 14;

    /// <summary>28 inventory slots.</summary>
    public const int HaveMax = 28;

    /// <summary>Equip + inventory slots (INVENTORY_TOTAL).</summary>
    public const int InventoryTotal = SlotMax + HaveMax;

    /// <summary>192 warehouse item slots.</summary>
    public const int WarehouseMax = 192;

    public const int MaxQuest = 100;

    public const int ItemCountMax = 9999;

    public const int MaxSkills = 9;

    // Equipment slot indices (shared/globals.h)
    public const int SlotHead = 1;
    public const int SlotBreast = 4;
    public const int SlotShoulder = 5;
    public const int SlotRightHand = 6;
    public const int SlotLeftHand = 8;
    public const int SlotLeg = 10;
    public const int SlotGlove = 12;
    public const int SlotFoot = 13;

    // e_Authority (subset)
    public const byte AuthorityUser = 1;
}

/// <summary>UPDATE_* codes from <c>Server/Aujard/Define.h</c>.</summary>
public enum UserUpdateType : byte
{
    Logout = 0x01,     // UPDATE_LOGOUT
    AllSave = 0x02,    // UPDATE_ALL_SAVE
    PacketSave = 0x03, // UPDATE_PACKET_SAVE
}

/// <summary>NEW_CHAR_* result codes from <c>Server/Aujard/Define.h</c>.</summary>
public enum NewCharResult : short
{
    Error = -1,
    Success = 0,
    NoFreeSlot = 1,
    InvalidRace = 2,
    NameInUse = 3,
    SyncError = 4,
}
