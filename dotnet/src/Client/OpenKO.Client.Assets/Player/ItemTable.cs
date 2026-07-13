namespace OpenKO.Client.Assets.Player;

/// <summary>
/// The appearance-relevant fields of a <c>__TABLE_ITEM_BASIC</c> row
/// (Item_Org*.tbl). The full stat block is skipped — only what the runtime
/// character assembly and the FX hooks read is carried.
/// </summary>
public sealed class ItemBasicRow
{
    public uint Id { get; init; }

    public byte ExtIndex { get; init; }

    /// <summary>col 07 — encoded resource id → the part/plug model file name.</summary>
    public uint ResourceId { get; init; }

    /// <summary>col 08 — encoded icon id → the UI icon file name.</summary>
    public uint IconId { get; init; }

    /// <summary>col 11 — e_ItemClass; ITEM_CLASS_SHIELD selects the forearm joint.</summary>
    public byte Class { get; init; }

    /// <summary>col 12 — robe replaces both upper and lower.</summary>
    public bool IsRobeType { get; init; }

    /// <summary>col 13 — e_ItemPosition attach point; drives Part vs Plug.</summary>
    public KoItemPosition AttachPoint { get; init; }

    internal static ItemBasicRow FromCells(object[] cells) => new()
    {
        Id = TblCell.U32(cells, 0),
        ExtIndex = TblCell.U8(cells, 1),
        // 2 szName, 3 szRemark, 4 dwIDK0, 5 byIDK1
        ResourceId = TblCell.U32(cells, 6),
        IconId = TblCell.U32(cells, 7),
        // 8 dwSoundID0, 9 dwSoundID1
        Class = TblCell.U8(cells, 10),
        IsRobeType = TblCell.U8(cells, 11) != 0,
        AttachPoint = (KoItemPosition)TblCell.U8(cells, 12),
    };
}

/// <summary>
/// The appearance/FX fields of a <c>__TABLE_ITEM_EXT</c> row (Item_Ext_*.tbl).
/// The resource/icon ids override the basic row when non-zero.
/// </summary>
public sealed class ItemExtRow
{
    public uint Id { get; init; }

    /// <summary>col 06 — overrides the basic resource id when non-zero.</summary>
    public uint ResourceId { get; init; }

    /// <summary>col 07 — overrides the basic icon id when non-zero.</summary>
    public uint IconId { get; init; }

    /// <summary>col 08 — e_ItemAttrib (unique = 4 drives weapon-glow FX).</summary>
    public byte MagicOrRare { get; init; }

    public byte DamageFire { get; init; }

    public byte DamageIce { get; init; }

    public byte DamageThunder { get; init; }

    public byte DamagePoison { get; init; }

    internal static ItemExtRow FromCells(object[] cells) => new()
    {
        Id = TblCell.U32(cells, 0),
        // 1 szHeader, 2 dwBaseID, 3 szRemark, 4 dwIDK0
        ResourceId = TblCell.U32(cells, 5),
        IconId = TblCell.U32(cells, 6),
        MagicOrRare = TblCell.U8(cells, 7),
        // 8..20 damage/defense/durability block
        DamageFire = TblCell.U8(cells, 21),
        DamageIce = TblCell.U8(cells, 22),
        DamageThunder = TblCell.U8(cells, 23),
        DamagePoison = TblCell.U8(cells, 24),
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
}
