namespace OpenKO.Common;

// Port of shared/packets.h and Client/WarFare/PacketDef.h.
// Values are kept identical to the originals to preserve protocol compatibility.

/// <summary>Default ports (Client/WarFare/PacketDef.h).</summary>
public static class SocketPorts
{
    public const int Game = 15001;
    public const int Login = 15100;
}

/// <summary>e_GameOpcode — game server (Ebenezer) opcodes.</summary>
public enum GameOpcode : byte
{
    Login = 0x01,
    NewChar = 0x02,
    DelChar = 0x03,
    SelChar = 0x04,
    SelNation = 0x05,
    Move = 0x06,
    UserInOut = 0x07,
    Attack = 0x08,
    Rotate = 0x09,
    NpcInOut = 0x0A,
    NpcMove = 0x0B,
    AllCharInfoReq = 0x0C,
    GameStart = 0x0D,
    MyInfo = 0x0E,
    Logout = 0x0F,
    Chat = 0x10,
    Dead = 0x11,
    Regene = 0x12,
    Time = 0x13,
    Weather = 0x14,
    RegionChange = 0x15,
    ReqUserIn = 0x16,
    HpChange = 0x17,
    MspChange = 0x18,
    ItemLog = 0x19,
    ExpChange = 0x1A,
    LevelChange = 0x1B,
    NpcRegion = 0x1C,
    ReqNpcIn = 0x1D,
    Warp = 0x1E,
    ItemMove = 0x1F,
    NpcEvent = 0x20,
    ItemTrade = 0x21,
    TargetHp = 0x22,
    ItemDrop = 0x23,
    BundleOpenReq = 0x24,
    TradeNpc = 0x25,
    ItemGet = 0x26,
    ZoneChange = 0x27,
    PointChange = 0x28,
    StateChange = 0x29,
    LoyaltyChange = 0x2A,
    VersionCheck = 0x2B,
    Cryption = 0x2C,
    UserLookChange = 0x2D,
    Notice = 0x2E,
    Party = 0x2F,
    Exchange = 0x30,
    MagicProcess = 0x31,
    SkillPtChange = 0x32,
    ObjectEvent = 0x33,
    ClassChange = 0x34,
    ChatTarget = 0x35,
    ConcurrentUser = 0x36,
    DataSave = 0x37,
    Duration = 0x38,
    TimeNotify = 0x39,
    RepairNpc = 0x3A,
    ItemRepair = 0x3B,
    KnightsProcess = 0x3C,
    ItemCountChange = 0x3D,
    KnightsList = 0x3E,
    ItemRemove = 0x3F,
    Operator = 0x40,
    SpeedHackCheck = 0x41,
    CompressPacket = 0x42,
    ServerCheck = 0x43,
    ContinuousPacket = 0x44,
    Warehouse = 0x45,
    ServerChange = 0x46,
    ReportBug = 0x47,
    Home = 0x48,
    FriendProcess = 0x49,
    GoldChange = 0x4A,
    WarpList = 0x4B,
    VirtualServer = 0x4C,
    ZoneConcurrent = 0x4D,
    Corpse = 0x4E,
    PartyBbs = 0x4F,
    MarketBbs = 0x50,
    KickOut = 0x51,
    ClientEvent = 0x52,
    MapEvent = 0x53,
    WeightChange = 0x54,
    SelectMsg = 0x55,
    NpcSay = 0x56,
    BattleEvent = 0x57,
    AuthorityChange = 0x58,
    EditBox = 0x59,
    Santa = 0x5A,
    ItemUpgrade = 0x5B,
    Packet1 = 0x5C,
    Packet2 = 0x5D,
    ZoneAbility = 0x5E,
    Event = 0x5F,
    Stealth = 0x60,
    RoomPacketProcess = 0x61,
    Room = 0x62,
    RoomMatch = 0x63,
    Quest = 0x64,
    PpCard = 0x65,
    Kiss = 0x66,
    RecommendUser = 0x67,
    Merchant = 0x68,
    MerchantInOut = 0x69,
    ShoppingMall = 0x6A,
    ServerIndex = 0x6B,
    Effect = 0x6C,
    Siege = 0x6D,
    NameChange = 0x6E,
    WebPage = 0x6F,
    Cape = 0x70,
    Premium = 0x71,
    HackTool = 0x72,
    Rental = 0x73,
    RewardItems = 0x74,
    Challenge = 0x75,
    Pet = 0x76,
    China = 0x77,
    King = 0x78,
    SkillData = 0x79,
    ProgramCheck = 0x7A,
    Bifrost = 0x7B,
    ServerKill = 0x7F,
    DebugStringPacket = 0xFE,
    TestPacket = 0xFF,
}

/// <summary>e_LoginOpcode — login server opcodes.</summary>
public enum LoginOpcode : byte
{
    VersionReq = 0x01,
    DownloadInfoReq = 0x02,
    Cryption = 0xF2,
    LoginReq = 0xF3,
    MgameLogin = 0xF4,
    ServerList = 0xF5,
    News = 0xF6,
}

/// <summary>e_DBOpcode.</summary>
public enum DbOpcode : byte
{
    CouponEvent = 0x10,
    LoginInfo = 0x50,
    Heartbeat = 0x7F,
}

/// <summary>e_AuthResult.</summary>
public enum AuthResult : byte
{
    Ok = 0x01,
    NotFound = 0x02,
    InvalidPw = 0x03,
    Banned = 0x04,
    InGame = 0x05,
    Error = 0x06,
    Failed = 0xFF,
}

/// <summary>e_ChatType.</summary>
public enum ChatType
{
    General = 1,
    Private = 2,
    Party = 3,
    Force = 4,
    Shout = 5,
    Knights = 6,
    Public = 7,
    WarSystem = 8,
    Permanent = 9,
    EndPermanent = 10,
    MonumentNotice = 11,
    Gm = 12,
    Command = 13,
    Merchant = 14,
    Alliance = 15,
    Announcement = 17,
    SeekingParty = 19,
}

/// <summary>e_WeatherType.</summary>
public enum WeatherType
{
    Fine = 0x01,
    Rain = 0x02,
    Snow = 0x03,
}

/// <summary>e_ZoneChangeOpcode.</summary>
public enum ZoneChangeOpcode
{
    Loading = 1,
    Loaded = 2,
    Teleport = 3,
}

/// <summary>e_PartyOpcode.</summary>
public enum PartyOpcode
{
    Create = 0x01,
    Permit = 0x02,
    Insert = 0x03,
    Remove = 0x04,
    Delete = 0x05,
    HpChange = 0x06,
    LevelChange = 0x07,
    ClassChange = 0x08,
    StatusChange = 0x09,
    Register = 0x0A,
    Report = 0x0B,
    Promote = 0x1C,
    AllStatusChange = 0x1D,
}

/// <summary>e_ExchangeOpcode.</summary>
public enum ExchangeOpcode
{
    Req = 1,
    Agree = 2,
    Add = 3,
    OtherAdd = 4,
    Decide = 5,
    OtherDecide = 6,
    Done = 7,
    Cancel = 8,
}

/// <summary>e_MagicOpcode.</summary>
public enum MagicOpcode
{
    Casting = 1,
    Flying = 2,
    Effecting = 3,
    Fail = 4,
    DurationExpired = 5,
    Cancel = 6,
    CancelTransformation = 7,
    Type4Extend = 8,
    TransformList = 9,
    FailTransformation = 10,
    Unknown = 12,
    Cancel2 = 13,
}

/// <summary>e_KnightsOpcode.</summary>
public enum KnightsOpcode
{
    Create = 0x01,
    Join = 0x02,
    Withdraw = 0x03,
    Remove = 0x04,
    Destroy = 0x05,
    Admit = 0x06,
    Reject = 0x07,
    Punish = 0x08,
    Chief = 0x09,
    ViceChief = 0x0A,
    Officer = 0x0B,
    AllListReq = 0x0C,
    MemberReq = 0x0D,
    CurrentReq = 0x0E,
    Stash = 0x0F,
    ModifyFame = 0x10,
    JoinReq = 0x11,
    ListReq = 0x12,
}

/// <summary>e_WarehouseOpcode.</summary>
public enum WarehouseOpcode
{
    Open = 0x01,
    Input = 0x02,
    Output = 0x03,
    Move = 0x04,
    InvenMove = 0x05,
    Req = 0x10,
}
