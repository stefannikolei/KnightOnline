namespace OpenKO.Client.Assets.Player;

/// <summary>Player race (e_Race, GameDef.h). Karus 1–4, El Morad 11–13, NPC 100.</summary>
public enum KoRace
{
    All = 0,
    Arktuarek = 1,
    Tuarek = 2,
    WrinkleTuarek = 3,
    PuriTuarek = 4,
    Babarian = 11,
    Man = 12,
    Women = 13,
    Npc = 100,
    Unknown = -1,
}

/// <summary>Character body part slot (e_PartPosition, GameDef.h) — indexes CN3Chr parts.</summary>
public enum KoPartPosition
{
    Upper = 0,
    Lower = 1,
    Face = 2,
    Hands = 3,
    Feet = 4,
    HairHelmet = 5,
    Count = 6,
    Unknown = -1,
}

/// <summary>Character plug slot (e_PlugPosition, GameDef.h) — hands / back / grade.</summary>
public enum KoPlugPosition
{
    RightHand = 0,
    LeftHand = 1,
    Back = 2,
    KnightsGrade = 3,
    Count = 4,
    Unknown = -1,
}

/// <summary>What MakeResrcFileNameForUPC resolved an item into (e_ItemType).</summary>
public enum KoItemType
{
    Plug = 1,
    Part = 2,
    IconOnly = 3,
    Gold = 9,
    Songpyun = 10,
    Unknown = -1,
}

/// <summary>Item body attach point (e_ItemPosition = __TABLE_ITEM_BASIC.byAttachPoint).</summary>
public enum KoItemPosition
{
    Dual = 0,
    RightHand = 1,
    LeftHand = 2,
    TwoHandRight = 3,
    TwoHandLeft = 4,
    Upper = 5,
    Lower = 6,
    Head = 7,
    Gloves = 8,
    Shoes = 9,
    Ear = 10,
    Neck = 11,
    Finger = 12,
    Shoulder = 13,
    Belt = 14,
    Inventory = 15,
    Gold = 16,
    Songpyun = 17,
    Unknown = -1,
}

/// <summary>The 14 equip slots of the local-player paperdoll (e_ItemSlot).</summary>
public enum KoItemSlot
{
    EarRight = 0,
    Head = 1,
    EarLeft = 2,
    Neck = 3,
    Upper = 4,
    Shoulder = 5,
    HandRight = 6,
    Belt = 7,
    HandLeft = 8,
    RingRight = 9,
    Lower = 10,
    RingLeft = 11,
    Gloves = 12,
    Shoes = 13,
    Count = 14,
    Unknown = -1,
}

/// <summary>Item class (e_ItemClass) — only the entries the appearance path needs.</summary>
public static class KoItemClass
{
    public const byte Shield = 60;
}
