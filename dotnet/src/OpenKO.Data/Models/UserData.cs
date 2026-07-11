namespace OpenKO.Data.Models;

/// <summary>Port of <c>_ITEM_DATA</c> (Server/shared-server/_USER_DATA.h).</summary>
public struct ItemData
{
    public int Num;
    public short Duration;
    public short Count;
    public byte Flag;
    public ushort TimeRemaining;
    public long SerialNum;
}

/// <summary>Port of <c>_WAREHOUSE_ITEM_DATA</c>.</summary>
public struct WarehouseItemData
{
    public int Num;
    public short Duration;
    public short Count;
    public long SerialNum;
}

/// <summary>Port of <c>_USER_QUEST</c>.</summary>
public struct UserQuest
{
    public short QuestId;
    public byte QuestState;
}

/// <summary>
/// Port of the <c>_USER_DATA</c> record that lived in the KNIGHT_DB shared-memory
/// block. In the C# topology it is a plain object owned by the DB agent; the
/// packed-struct layout only ever mattered for the (removed) shared memory, the
/// DB blobs have their own explicit codec (UserDataBlobCodec).
/// </summary>
public sealed class UserData
{
    public string CharId = string.Empty;      // m_id
    public string AccountId = string.Empty;   // m_Accountid

    public byte Zone;
    public float CurX;
    public float CurZ;
    public float CurY;

    public byte Nation;
    public byte Race;
    public short Class;
    public byte HairColor;
    public byte Rank;
    public byte Title;
    public byte Level;
    public int Exp;
    public int Loyalty;
    public int LoyaltyMonthly;
    public byte Face;
    public byte City;
    public short Knights;
    public byte Fame;
    public short Hp;
    public short Mp;
    public short Sp;
    public byte Str;
    public byte Sta;
    public byte Dex;
    public byte Intel;
    public byte Cha;
    public byte Authority = GameConstants.AuthorityUser;
    public byte Points;
    public int Gold;
    public short Bind;
    public int Bank;

    public byte[] Skills = new byte[GameConstants.MaxSkills];
    public ItemData[] Items = new ItemData[GameConstants.InventoryTotal];
    public WarehouseItemData[] Warehouse = new WarehouseItemData[GameConstants.WarehouseMax];

    public byte Logout;
    public byte WarehouseFlag;
    public uint Time;                          // m_dwTime

    public byte PremiumType;
    public short PremiumTime;
    public int MannerPoint;

    public short QuestCount;
    public UserQuest[] Quests = new UserQuest[GameConstants.MaxQuest];

    /// <summary>ResetUserData: zero everything, then Authority = AUTHORITY_USER.</summary>
    public void Reset()
    {
        CharId = string.Empty;
        AccountId = string.Empty;
        Zone = 0;
        CurX = CurZ = CurY = 0;
        Nation = Race = HairColor = Rank = Title = Level = 0;
        Class = 0;
        Exp = Loyalty = LoyaltyMonthly = 0;
        Face = City = Fame = 0;
        Knights = 0;
        Hp = Mp = Sp = 0;
        Str = Sta = Dex = Intel = Cha = Points = 0;
        Gold = Bank = 0;
        Bind = 0;
        Array.Clear(Skills);
        Array.Clear(Items);
        Array.Clear(Warehouse);
        Logout = 0;
        WarehouseFlag = 0;
        Time = 0;
        PremiumType = 0;
        PremiumTime = 0;
        MannerPoint = 0;
        QuestCount = 0;
        Array.Clear(Quests);
        Authority = GameConstants.AuthorityUser;
    }
}
