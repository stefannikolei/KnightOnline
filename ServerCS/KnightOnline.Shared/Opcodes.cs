namespace KnightOnline.Shared;

/// <summary>
/// Packet opcodes for Knight Online servers
/// </summary>
public static class Opcodes
{
    // Login server opcodes
    public const byte LS_VERSION_REQ = 0x01;
    public const byte LS_DOWNLOADINFO_REQ = 0x02;
    public const byte LS_CRYPTION = 0xF2;
    public const byte LS_LOGIN_REQ = 0xF3;
    public const byte LS_MGAME_LOGIN = 0xF4;
    public const byte LS_SERVERLIST = 0xF5;
    public const byte LS_NEWS = 0xF6;

    // Game server opcodes (Client <-> Ebenezer)
    public const byte WIZ_LOGIN = 0x01;              // Account Login
    public const byte WIZ_NEW_CHAR = 0x02;           // Create Character DB
    public const byte WIZ_DEL_CHAR = 0x03;           // Delete Character DB
    public const byte WIZ_SEL_CHAR = 0x04;           // Select Character
    public const byte WIZ_SEL_NATION = 0x05;         // Select Nation
    public const byte WIZ_MOVE = 0x06;               // Move (1 Second)
    public const byte WIZ_USER_INOUT = 0x07;         // User Info Insert, delete
    public const byte WIZ_ATTACK = 0x08;             // General Attack
    public const byte WIZ_ROTATE = 0x09;             // Rotate
    public const byte WIZ_NPC_INOUT = 0x0A;          // NPC Info Insert, delete
    public const byte WIZ_NPC_MOVE = 0x0B;           // NPC Move (1 Second)
    public const byte WIZ_ALLCHAR_INFO_REQ = 0x0C;   // Account All Character Info Request
    public const byte WIZ_GAMESTART = 0x0D;          // Request Other User, NPC Info
    public const byte WIZ_MYINFO = 0x0E;             // User Detail Data Download
    public const byte WIZ_LOGOUT = 0x0F;             // Request Logout
    public const byte WIZ_CHAT = 0x10;               // User Chatting
    public const byte WIZ_DEAD = 0x11;               // User Dead
    public const byte WIZ_REGENE = 0x12;             // User Regeneration
    public const byte WIZ_TIME = 0x13;               // Game Timer
    public const byte WIZ_WEATHER = 0x14;            // Game Weather
    public const byte WIZ_REGIONCHANGE = 0x15;       // Region UserInfo Receive
    public const byte WIZ_REQ_USERIN = 0x16;         // Client Request UnRegistered User List
    public const byte WIZ_HP_CHANGE = 0x17;          // Current HP Download
    public const byte WIZ_MSP_CHANGE = 0x18;         // Current MP Download
    public const byte WIZ_ITEM_LOG = 0x19;           // Send To Agent for Writing Log
    public const byte WIZ_EXP_CHANGE = 0x1A;         // Current EXP Download
    public const byte WIZ_LEVEL_CHANGE = 0x1B;       // Max HP, MP, SP, Weight, Exp Download
    public const byte WIZ_NPC_REGION = 0x1C;         // NPC Region Change Receive
    public const byte WIZ_REQ_NPCIN = 0x1D;          // Client Request UnRegistered NPC List
    public const byte WIZ_WARP = 0x1E;               // User Remote Warp
    public const byte WIZ_ITEM_MOVE = 0x1F;          // User Item Move
    public const byte WIZ_NPC_EVENT = 0x20;          // User Click NPC Event
    public const byte WIZ_ITEM_TRADE = 0x21;         // Item Trade
    public const byte WIZ_TARGET_HP = 0x22;          // Attack Result Target HP
    public const byte WIZ_ITEM_DROP = 0x23;          // Zone Item Insert
    public const byte WIZ_BUNDLE_OPEN_REQ = 0x24;    // Zone Item list Request
    public const byte WIZ_TRADE_NPC = 0x25;          // ITEM Trade start
    public const byte WIZ_ITEM_GET = 0x26;           // Zone Item Get
    public const byte WIZ_ZONE_CHANGE = 0x27;        // Zone Change
    public const byte WIZ_POINT_CHANGE = 0x28;       // Str, Sta, dex, intel, cha, point up down
    public const byte WIZ_STATE_CHANGE = 0x29;       // User Sitdown or Stand
    public const byte WIZ_LOYALTY_CHANGE = 0x2A;     // Nation Contribution
    public const byte WIZ_VERSION_CHECK = 0x2B;      // Client version check
    public const byte WIZ_CRYPTION = 0x2C;           // Cryption
    public const byte WIZ_USERLOOK_CHANGE = 0x2D;    // User Slot Item Resource Change
    public const byte WIZ_NOTICE = 0x2E;             // Update Notice Alarm
    public const byte WIZ_PARTY = 0x2F;              // Party Related Packet
    public const byte WIZ_EXCHANGE = 0x30;           // Exchange Related Packet
    public const byte WIZ_MAGIC_PROCESS = 0x31;      // Magic Related Packet
    public const byte WIZ_SKILLPT_CHANGE = 0x32;     // User changed particular skill point
    public const byte WIZ_OBJECT_EVENT = 0x33;       // Map Object Event Occur (ex: Bind Point Setting)
    public const byte WIZ_CLASS_CHANGE = 0x34;       // 10 level over can change class
    public const byte WIZ_CHAT_TARGET = 0x35;        // Select Private Chanting User
    public const byte WIZ_CONCURRENTUSER = 0x36;     // Current Game User Count
    public const byte WIZ_DATASAVE = 0x37;           // User GameData DB Save Request
    public const byte WIZ_DURATION = 0x38;           // Item Durability Change
    public const byte WIZ_TIMENOTIFY = 0x39;         // Time Adaption Magic Time Notify Packet (2 Seconds)
    public const byte WIZ_REPAIR_NPC = 0x3A;         // Item Trade, Upgrade and Repair
    public const byte WIZ_ITEM_REPAIR = 0x3B;        // Item Repair Processing
    public const byte WIZ_KNIGHTS_PROCESS = 0x3C;    // Knights Related Packet
    public const byte WIZ_ITEM_COUNT_CHANGE = 0x3D;  // Item count change
    public const byte WIZ_KNIGHTS_LIST = 0x3E;       // All Knights List Info download
    public const byte WIZ_ITEM_REMOVE = 0x3F;        // Item Remove from inventory
    public const byte WIZ_OPERATOR = 0x40;           // Operator Authority Packet
    public const byte WIZ_SPEEDHACK_CHECK = 0x41;    // Speed Hack Using Check
    public const byte WIZ_COMPRESS_PACKET = 0x42;    // Data Compressing Packet
    public const byte WIZ_SERVER_CHECK = 0x43;       // Server Status Check Packet
    public const byte WIZ_CONTINOUS_PACKET = 0x44;   // Region Data Packet
    public const byte WIZ_WAREHOUSE = 0x45;          // Warehouse Open, In, Out
    public const byte WIZ_SERVER_CHANGE = 0x46;      // When you change the server
    public const byte WIZ_REPORT_BUG = 0x47;         // Report Bug to the manager
    public const byte WIZ_HOME = 0x48;               // Come back home
    public const byte WIZ_FRIEND_PROCESS = 0x49;     // Get the status of your friend
    public const byte WIZ_GOLD_CHANGE = 0x4A;        // When you get the gold of your enemy
    public const byte WIZ_WARP_LIST = 0x4B;          // Warp List by NPC or Object
    public const byte WIZ_VIRTUAL_SERVER = 0x4C;     // Battle zone Server Info packet (IP, Port)
    public const byte WIZ_ZONE_CONCURRENT = 0x4D;    // Battle zone concurrent users request packet
    public const byte WIZ_CORPSE = 0x4E;             // To have your corpse have an ID on top of it
    public const byte WIZ_PARTY_BBS = 0x4F;          // For the party wanted bulletin board service
    public const byte WIZ_MARKET_BBS = 0x50;         // For the market bulletin board service
    public const byte WIZ_KICKOUT = 0x51;            // Account ID forbid duplicate connection
    public const byte WIZ_CLIENT_EVENT = 0x52;       // Client Event (for quest)
    public const byte WIZ_MAP_EVENT = 0x53;          // Map event
    public const byte WIZ_WEIGHT_CHANGE = 0x54;      // Notify change of weight
    public const byte WIZ_SELECT_MSG = 0x55;         // Select Event Message
    public const byte WIZ_NPC_SAY = 0x56;            // NPC Say
    public const byte WIZ_BATTLE_EVENT = 0x57;       // Battle Event Result
    public const byte WIZ_AUTHORITY_CHANGE = 0x58;   // Authority change
    public const byte WIZ_EDIT_BOX = 0x59;           // Activate/Receive info from Input_Box
    public const byte WIZ_SANTA = 0x5A;              // Santa Claus event
    public const byte WIZ_ITEM_UPGRADE = 0x5B;       // Item upgrade
    public const byte WIZ_PACKET1 = 0x5C;            // Generic packet 1
    public const byte WIZ_PACKET2 = 0x5D;            // Generic packet 2
    public const byte WIZ_ZONEABILITY = 0x5E;        // Zone ability
    public const byte WIZ_EVENT = 0x5F;              // Event
    public const byte WIZ_STEALTH = 0x60;            // Stealth related
    public const byte WIZ_ROOM_PACKETPROCESS = 0x61; // Room system
    public const byte WIZ_ROOM = 0x62;               // Room
    public const byte WIZ_ROOM_MATCH = 0x63;         // Room match
    public const byte WIZ_QUEST = 0x64;              // Quest
    public const byte WIZ_PP_CARD = 0x65;            // PP Card
    public const byte WIZ_KISS = 0x66;               // Kiss
    public const byte WIZ_RECOMMEND_USER = 0x67;     // Recommend user
    public const byte WIZ_MERCHANT = 0x68;           // Merchant
    public const byte WIZ_MERCHANT_INOUT = 0x69;     // Merchant in/out
    public const byte WIZ_SHOPPING_MALL = 0x6A;      // Shopping mall
    public const byte WIZ_SERVER_INDEX = 0x6B;       // Server index
    public const byte WIZ_EFFECT = 0x6C;             // Effect
    public const byte WIZ_SIEGE = 0x6D;              // Siege
    public const byte WIZ_NAME_CHANGE = 0x6E;        // Name change
    public const byte WIZ_WEBPAGE = 0x6F;            // Webpage
    public const byte WIZ_CAPE = 0x70;               // Cape
    public const byte WIZ_PREMIUM = 0x71;            // Premium
    public const byte WIZ_HACKTOOL = 0x72;           // Hack tool
    public const byte WIZ_RENTAL = 0x73;             // Rental
    public const byte WIZ_REWARD_ITEMS = 0x74;       // Reward items
    public const byte WIZ_CHALLENGE = 0x75;          // Challenge
    public const byte WIZ_PET = 0x76;                // Pet
    public const byte WIZ_CHINA = 0x77;              // China specific
    public const byte WIZ_KING = 0x78;               // King
    public const byte WIZ_SKILLDATA = 0x79;          // Skill data
    public const byte WIZ_PROGRAMCHECK = 0x7A;       // Program check
    public const byte WIZ_BIFROST = 0x7B;            // Bifrost
    public const byte WIZ_SERVER_KILL = 0x7F;        // Server kill

    public const byte WIZ_DEBUG_STRING_PACKET = 0xFE; // Debug string
    public const byte WIZ_TEST_PACKET = 0xFF;         // Test packet

    // Database server specific opcodes
    public const byte WIZ_LOGIN_INFO = 0x50;         // DBAgent Communication (reused from game)

    public const byte DB_COUPON_EVENT = 0x10;        // Coupon event
    
    // Knights sub-opcodes
    public const byte KNIGHTS_CREATE = 0x11;         // Create knights clan
    public const byte KNIGHTS_JOIN = 0x12;           // Join knights clan
    public const byte KNIGHTS_WITHDRAW = 0x13;       // Withdraw from knights clan
    public const byte KNIGHTS_REMOVE = 0x14;         // Remove member
    public const byte KNIGHTS_DESTROY = 0x15;        // Destroy knights clan
    public const byte KNIGHTS_ADMIT = 0x16;          // Admit member
    public const byte KNIGHTS_REJECT = 0x17;         // Reject member
    public const byte KNIGHTS_PUNISH = 0x18;         // Punish member
    public const byte KNIGHTS_CHIEF = 0x19;          // Appoint chief
    public const byte KNIGHTS_VICECHIEF = 0x1A;      // Appoint vice chief
    public const byte KNIGHTS_OFFICER = 0x1B;        // Appoint officer
    public const byte KNIGHTS_ALLLIST_REQ = 0x1C;    // Request knights list
    public const byte KNIGHTS_MEMBER_REQ = 0x1D;     // Request all members
    public const byte KNIGHTS_CURRENT_REQ = 0x1E;    // Request current online list
    public const byte KNIGHTS_STASH = 0x1F;          // Knights stash
    public const byte KNIGHTS_MODIFY_FAME = 0x20;    // Modify member fame
    public const byte KNIGHTS_JOIN_REQ = 0x21;       // Request to join
    public const byte KNIGHTS_LIST_REQ = 0x22;       // Request knights list by index

    // Clan sub-opcodes
    public const byte CLAN_CREATE = 0x01;            // Create clan
    public const byte CLAN_JOIN = 0x02;              // Join clan

    // Update user data types
    public const byte UPDATE_LOGOUT = 0x01;
    public const byte UPDATE_ALL_SAVE = 0x02;
    public const byte UPDATE_PACKET_SAVE = 0x03;

    // Coupon event sub-opcodes
    public const byte CHECK_COUPON_EVENT = 0x01;
    public const byte UPDATE_COUPON_EVENT = 0x02;

    // Packet constants
    public const byte PACKET_START1 = 0xAA;
    public const byte PACKET_START2 = 0x55;
    public const byte PACKET_END1 = 0x55;
    public const byte PACKET_END2 = 0xAA;

    // Connection states
    public const byte STATE_CONNECTED = 0x01;
    public const byte STATE_DISCONNECTED = 0x02;
    public const byte STATE_GAMESTART = 0x03;

    // Socket types
    public const byte TYPE_ACCEPT = 0x01;
    public const byte TYPE_CONNECT = 0x02;

    // Nations
    public const byte UNIFY_NATION = 0;
    public const byte KARUS = 1;
    public const byte ELMORAD = 2;
}