namespace OpenKO.Client.Assets.Player;

/// <summary>
/// A <c>__TABLE_ITEM_BASIC</c> row (Item_Org*.tbl). The appearance fields the
/// runtime character assembly reads plus the inventory/tooltip/repair stat block
/// (name, requirements, price, durability, weight, defense). 1-based struct field
/// numbers from GameDef.h map to the 0-based column index = field - 1.
/// </summary>
public sealed class ItemBasicRow
{
    /// <summary>field 01 — encoded item id.</summary>
    public uint Id { get; init; }

    /// <summary>field 02 — ext table index (Item_Ext_&lt;ExtIndex&gt;.tbl).</summary>
    public byte ExtIndex { get; init; }

    /// <summary>field 03 — item name (CP949).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>field 04 — item description/remark (CP949).</summary>
    public string Remark { get; init; } = string.Empty;

    /// <summary>field 07 — encoded resource id → the part/plug model file name.</summary>
    public uint ResourceId { get; init; }

    /// <summary>field 08 — encoded icon id → the UI icon file name.</summary>
    public uint IconId { get; init; }

    /// <summary>field 11 — e_ItemClass; ITEM_CLASS_SHIELD selects the forearm joint.</summary>
    public byte Class { get; init; }

    /// <summary>field 12 — robe replaces both upper and lower.</summary>
    public bool IsRobeType { get; init; }

    /// <summary>field 13 — e_ItemPosition attach point; drives Part vs Plug.</summary>
    public KoItemPosition AttachPoint { get; init; }

    /// <summary>field 14 — required race.</summary>
    public byte NeedRace { get; init; }

    /// <summary>field 15 — required class.</summary>
    public byte NeedClass { get; init; }

    /// <summary>field 16 — weapon damage.</summary>
    public short Damage { get; init; }

    /// <summary>field 17 — attack speed (100 units = 1 second).</summary>
    public short AttackInterval { get; init; }

    /// <summary>field 18 — effective attack range (0.1 m units).</summary>
    public short AttackRange { get; init; }

    /// <summary>field 19 — weight (0.1 units).</summary>
    public short Weight { get; init; }

    /// <summary>field 20 — max durability.</summary>
    public short MaxDurability { get; init; }

    /// <summary>field 21 — base purchase price.</summary>
    public int Price { get; init; }

    /// <summary>field 22 — sale type (e_ItemSaleType; SALE_TYPE_FULL = 1).</summary>
    public int SaleType { get; init; }

    /// <summary>field 23 — defense.</summary>
    public short Defense { get; init; }

    /// <summary>field 24 — countable/stackable.</summary>
    public bool Countable { get; init; }

    /// <summary>field 27 — required level (signed; can be negative).</summary>
    public sbyte NeedLevel { get; init; }

    /// <summary>field 29 — required rank.</summary>
    public byte NeedRank { get; init; }

    /// <summary>field 30 — required title.</summary>
    public byte NeedTitle { get; init; }

    /// <summary>field 31 — required strength.</summary>
    public byte NeedStrength { get; init; }

    /// <summary>field 32 — required stamina.</summary>
    public byte NeedStamina { get; init; }

    /// <summary>field 33 — required dexterity.</summary>
    public byte NeedDexterity { get; init; }

    /// <summary>field 34 — required intelligence.</summary>
    public byte NeedInteli { get; init; }

    /// <summary>field 35 — required charisma/magic power.</summary>
    public byte NeedMagicAttack { get; init; }

    /// <summary>field 36 — vendor selling group.</summary>
    public byte SellGroup { get; init; }

    /// <summary>field 37 — item grade.</summary>
    public byte Grade { get; init; }

    internal static ItemBasicRow FromCells(object[] cells) => new()
    {
        Id = TblCell.U32(cells, 0),
        ExtIndex = TblCell.U8(cells, 1),
        Name = TblCell.Str(cells, 2),
        Remark = TblCell.Str(cells, 3),
        // 4 dwIDK0, 5 byIDK1
        ResourceId = TblCell.U32(cells, 6),
        IconId = TblCell.U32(cells, 7),
        // 8 dwSoundID0, 9 dwSoundID1
        Class = TblCell.U8(cells, 10),
        IsRobeType = TblCell.U8(cells, 11) != 0,
        AttachPoint = (KoItemPosition)TblCell.U8(cells, 12),
        NeedRace = TblCell.U8(cells, 13),
        NeedClass = TblCell.U8(cells, 14),
        Damage = TblCell.I16(cells, 15),
        AttackInterval = TblCell.I16(cells, 16),
        AttackRange = TblCell.I16(cells, 17),
        Weight = TblCell.I16(cells, 18),
        MaxDurability = TblCell.I16(cells, 19),
        Price = TblCell.I32(cells, 20),
        SaleType = TblCell.I32(cells, 21),
        Defense = TblCell.I16(cells, 22),
        Countable = TblCell.U8(cells, 23) != 0,
        // 24 dwEffectID1, 25 dwEffectID2
        NeedLevel = TblCell.S8(cells, 26),
        // 27 cIDK2
        NeedRank = TblCell.U8(cells, 28),
        NeedTitle = TblCell.U8(cells, 29),
        NeedStrength = TblCell.U8(cells, 30),
        NeedStamina = TblCell.U8(cells, 31),
        NeedDexterity = TblCell.U8(cells, 32),
        NeedInteli = TblCell.U8(cells, 33),
        NeedMagicAttack = TblCell.U8(cells, 34),
        SellGroup = TblCell.U8(cells, 35),
        Grade = TblCell.U8(cells, 36),
    };
}

/// <summary>
/// A <c>__TABLE_ITEM_EXT</c> row (Item_Ext_*.tbl): the per-extension overrides
/// and stat block applied on top of the basic row. The resource/icon ids override
/// the basic row when non-zero; the stat fields add to the item's totals. 1-based
/// GameDef.h field numbers map to the 0-based column index = field - 1.
/// </summary>
public sealed class ItemExtRow
{
    /// <summary>field 01 — ext row id (item id % 1000).</summary>
    public uint Id { get; init; }

    /// <summary>field 02 — name prefix/header (CP949).</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>field 06 — overrides the basic resource id when non-zero.</summary>
    public uint ResourceId { get; init; }

    /// <summary>field 07 — overrides the basic icon id when non-zero.</summary>
    public uint IconId { get; init; }

    /// <summary>field 08 — e_ItemAttrib (unique = 4 drives weapon-glow FX).</summary>
    public byte MagicOrRare { get; init; }

    /// <summary>field 09 — weapon damage.</summary>
    public short Damage { get; init; }

    /// <summary>field 10 — attack speed percentage (100% = normal).</summary>
    public short AttackIntervalPercentage { get; init; }

    /// <summary>field 11 — hit rate percentage modifier.</summary>
    public short HitRate { get; init; }

    /// <summary>field 12 — evasion rate percentage modifier.</summary>
    public short EvationRate { get; init; }

    /// <summary>field 13 — max durability.</summary>
    public short MaxDurability { get; init; }

    /// <summary>field 14 — purchase price multiplier.</summary>
    public short PriceMultiply { get; init; }

    /// <summary>field 15 — defense.</summary>
    public short Defense { get; init; }

    /// <summary>field 16 — defense vs daggers (percentage modifier).</summary>
    public short DefenseRateDagger { get; init; }

    /// <summary>field 17 — defense vs swords (percentage modifier).</summary>
    public short DefenseRateSword { get; init; }

    /// <summary>field 18 — defense vs blunt weapons (percentage modifier).</summary>
    public short DefenseRateBlow { get; init; }

    /// <summary>field 19 — defense vs axes (percentage modifier).</summary>
    public short DefenseRateAxe { get; init; }

    /// <summary>field 20 — defense vs spears (percentage modifier).</summary>
    public short DefenseRateSpear { get; init; }

    /// <summary>field 21 — defense vs arrows (percentage modifier).</summary>
    public short DefenseRateArrow { get; init; }

    /// <summary>field 22 — bonus fire damage.</summary>
    public byte DamageFire { get; init; }

    /// <summary>field 23 — bonus ice damage.</summary>
    public byte DamageIce { get; init; }

    /// <summary>field 24 — bonus thunder damage (byDamageThuner).</summary>
    public byte DamageThunder { get; init; }

    /// <summary>field 25 — bonus poison damage.</summary>
    public byte DamagePoison { get; init; }

    /// <summary>field 31 — bonus strength.</summary>
    public short BonusStr { get; init; }

    /// <summary>field 32 — bonus stamina.</summary>
    public short BonusSta { get; init; }

    /// <summary>field 33 — bonus dexterity.</summary>
    public short BonusDex { get; init; }

    /// <summary>field 34 — bonus intelligence.</summary>
    public short BonusInt { get; init; }

    /// <summary>field 35 — bonus charisma/magic power (siBonusMagicAttak).</summary>
    public short BonusMagicAttak { get; init; }

    /// <summary>field 36 — bonus HP.</summary>
    public short BonusHP { get; init; }

    /// <summary>field 37 — bonus MSP.</summary>
    public short BonusMSP { get; init; }

    /// <summary>field 38 — fire resistance.</summary>
    public short RegistFire { get; init; }

    /// <summary>field 39 — ice resistance.</summary>
    public short RegistIce { get; init; }

    /// <summary>field 40 — electric resistance.</summary>
    public short RegistElec { get; init; }

    /// <summary>field 41 — magic resistance.</summary>
    public short RegistMagic { get; init; }

    /// <summary>field 42 — poison resistance.</summary>
    public short RegistPoison { get; init; }

    /// <summary>field 43 — curse resistance.</summary>
    public short RegistCurse { get; init; }

    /// <summary>field 46 — required level.</summary>
    public short NeedLevel { get; init; }

    /// <summary>field 47 — required rank.</summary>
    public short NeedRank { get; init; }

    /// <summary>field 48 — required title.</summary>
    public short NeedTitle { get; init; }

    /// <summary>field 49 — required strength.</summary>
    public short NeedStrength { get; init; }

    /// <summary>field 50 — required stamina.</summary>
    public short NeedStamina { get; init; }

    /// <summary>field 51 — required dexterity.</summary>
    public short NeedDexterity { get; init; }

    /// <summary>field 52 — required intelligence.</summary>
    public short NeedInteli { get; init; }

    /// <summary>field 53 — required charisma/magic power.</summary>
    public short NeedMagicAttack { get; init; }

    internal static ItemExtRow FromCells(object[] cells) => new()
    {
        Id = TblCell.U32(cells, 0),
        Header = TblCell.Str(cells, 1),
        // 2 dwBaseID, 3 szRemark, 4 dwIDK0
        ResourceId = TblCell.U32(cells, 5),
        IconId = TblCell.U32(cells, 6),
        MagicOrRare = TblCell.U8(cells, 7),
        Damage = TblCell.I16(cells, 8),
        AttackIntervalPercentage = TblCell.I16(cells, 9),
        HitRate = TblCell.I16(cells, 10),
        EvationRate = TblCell.I16(cells, 11),
        MaxDurability = TblCell.I16(cells, 12),
        PriceMultiply = TblCell.I16(cells, 13),
        Defense = TblCell.I16(cells, 14),
        DefenseRateDagger = TblCell.I16(cells, 15),
        DefenseRateSword = TblCell.I16(cells, 16),
        DefenseRateBlow = TblCell.I16(cells, 17),
        DefenseRateAxe = TblCell.I16(cells, 18),
        DefenseRateSpear = TblCell.I16(cells, 19),
        DefenseRateArrow = TblCell.I16(cells, 20),
        DamageFire = TblCell.U8(cells, 21),
        DamageIce = TblCell.U8(cells, 22),
        DamageThunder = TblCell.U8(cells, 23),
        DamagePoison = TblCell.U8(cells, 24),
        // 25 byStillHP, 26 byDamageMP, 27 byStillMP, 28 byReturnPhysicalDamage, 29 bySoulBind
        BonusStr = TblCell.I16(cells, 30),
        BonusSta = TblCell.I16(cells, 31),
        BonusDex = TblCell.I16(cells, 32),
        BonusInt = TblCell.I16(cells, 33),
        BonusMagicAttak = TblCell.I16(cells, 34),
        BonusHP = TblCell.I16(cells, 35),
        BonusMSP = TblCell.I16(cells, 36),
        RegistFire = TblCell.I16(cells, 37),
        RegistIce = TblCell.I16(cells, 38),
        RegistElec = TblCell.I16(cells, 39),
        RegistMagic = TblCell.I16(cells, 40),
        RegistPoison = TblCell.I16(cells, 41),
        RegistCurse = TblCell.I16(cells, 42),
        // 43 dwEffectID1, 44 dwEffectID2
        NeedLevel = TblCell.I16(cells, 45),
        NeedRank = TblCell.I16(cells, 46),
        NeedTitle = TblCell.I16(cells, 47),
        NeedStrength = TblCell.I16(cells, 48),
        NeedStamina = TblCell.I16(cells, 49),
        NeedDexterity = TblCell.I16(cells, 50),
        NeedInteli = TblCell.I16(cells, 51),
        NeedMagicAttack = TblCell.I16(cells, 52),
    };
}

/// <summary>
/// Port of <c>s_pTbl_Items_Basic</c> + <c>s_pTbl_Items_Exts[MAX_ITEM_EXTENSION]</c>
/// (GameBase.cpp). An item id splits into a base row (<c>id/1000*1000</c>) and an
/// ext row (<c>id%1000</c>) inside the ext table selected by the base row's
/// <see cref="ItemBasicRow.ExtIndex"/> — the exact lookup used everywhere in the
/// C++ client.
/// </summary>
public sealed class ItemTableSet
{
    public const int MaxItemExtension = 24; // MAX_ITEM_EXTENSION

    /// <summary>SALE_TYPE_FULL (shared/globals.h): sells at the full buy price.</summary>
    public const int SaleTypeFull = 1;

    private readonly N3TableFile _basic;
    private readonly N3TableFile?[] _exts;

    public ItemTableSet(N3TableFile basic, IReadOnlyList<N3TableFile?> exts)
    {
        _basic = basic;
        _exts = new N3TableFile?[MaxItemExtension];
        for (int i = 0; i < MaxItemExtension && i < exts.Count; i++)
            _exts[i] = exts[i];
    }

    /// <summary>
    /// Resolve an item id into its (basic, ext) rows. Returns null basic if the
    /// base row is missing; ext may be null when its table is absent (the C++
    /// asserts and skips such items, which the assembler mirrors).
    /// </summary>
    public (ItemBasicRow? Basic, ItemExtRow? Ext) Find(uint itemId)
    {
        object[]? basicCells = _basic.Find(itemId / 1000 * 1000);
        if (basicCells == null)
            return (null, null);

        var basic = ItemBasicRow.FromCells(basicCells);
        ItemExtRow? ext = null;
        if (basic.ExtIndex < MaxItemExtension && _exts[basic.ExtIndex] is { } extTable)
        {
            object[]? extCells = extTable.Find(itemId % 1000);
            if (extCells != null)
                ext = ItemExtRow.FromCells(extCells);
        }

        return (basic, ext);
    }

    /// <summary>
    /// Port of <c>__IconItemSkill::GetBuyPrice</c> (IconItemSkill.cpp): the vendor
    /// purchase price is <c>basic.Price * ext.PriceMultiply</c>. Returns 0 when
    /// either row is missing (the C++ null-guards both).
    /// </summary>
    public static int GetBuyPrice(ItemBasicRow? basic, ItemExtRow? ext)
    {
        if (basic == null || ext == null)
            return 0;

        return basic.Price * ext.PriceMultiply;
    }

    /// <summary>
    /// Port of <c>__IconItemSkill::GetSellPrice</c> (IconItemSkill.cpp): the base
    /// value is <c>basic.Price * ext.PriceMultiply</c>, divided by 4 (premium) or 6
    /// (normal) unless the item is <see cref="SaleTypeFull"/>, and floored at 1.
    /// </summary>
    public static int GetSellPrice(ItemBasicRow? basic, ItemExtRow? ext, bool hasPremium = false)
    {
        if (basic == null || ext == null)
            return 0;

        const int premiumRatio = 4;
        const int normalRatio = 6;

        int sellPrice = basic.Price * ext.PriceMultiply;
        if (basic.SaleType != SaleTypeFull)
            sellPrice /= hasPremium ? premiumRatio : normalRatio;

        return sellPrice < 1 ? 1 : sellPrice;
    }
}
