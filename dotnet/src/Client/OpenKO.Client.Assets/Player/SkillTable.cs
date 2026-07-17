namespace OpenKO.Client.Assets.Player;

/// <summary>
/// A <c>__TABLE_UPC_SKILL</c> row (skill_magic_main*.tbl). The skill-tree / cast
/// metadata: identity + display strings, the target/level/skill gating, the MSP/HP
/// and item costs, cast/recast timing and the two magic sub-table type selectors.
/// 1-based struct field numbers from GameDef.h map to the 0-based column index =
/// field - 1.
/// </summary>
public sealed class SkillRow
{
    /// <summary>field 01 — skill id (job*1000 + n).</summary>
    public uint Id { get; init; }

    /// <summary>field 02 — English name (CP949).</summary>
    public string EngName { get; init; } = string.Empty;

    /// <summary>field 03 — localized name (CP949).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>field 04 — description/tooltip (CP949).</summary>
    public string Desc { get; init; } = string.Empty;

    /// <summary>field 05 — caster start animation.</summary>
    public int SelfAnimId1 { get; init; }

    /// <summary>field 15 — target type/moral.</summary>
    public int Target { get; init; }

    /// <summary>field 16 — required player level.</summary>
    public int NeedLevel { get; init; }

    /// <summary>field 17 — required skill; the low digit (% 10) selects the skill tab.</summary>
    public int NeedSkill { get; init; }

    /// <summary>field 18 — MSP consumed.</summary>
    public int ExhaustMsp { get; init; }

    /// <summary>field 19 — HP consumed.</summary>
    public int ExhaustHp { get; init; }

    /// <summary>field 20 — required item (e_ItemClass / 10 encoding).</summary>
    public uint NeedItem { get; init; }

    /// <summary>field 21 — item consumed on cast.</summary>
    public uint ExhaustItem { get; init; }

    /// <summary>field 22 — cast time.</summary>
    public int CastTime { get; init; }

    /// <summary>field 23 — cooldown (recast) time.</summary>
    public int ReCastTime { get; init; }

    /// <summary>field 26 — success rate.</summary>
    public int PercentSuccess { get; init; }

    /// <summary>field 27 — primary magic sub-table type.</summary>
    public uint FirstTableType { get; init; }

    /// <summary>field 28 — secondary magic sub-table type.</summary>
    public uint SecondTableType { get; init; }

    /// <summary>field 29 — effective skill range.</summary>
    public int ValidDist { get; init; }

    internal static SkillRow FromCells(object[] cells) => new()
    {
        Id = TblCell.U32(cells, 0),
        EngName = TblCell.Str(cells, 1),
        Name = TblCell.Str(cells, 2),
        Desc = TblCell.Str(cells, 3),
        SelfAnimId1 = TblCell.I32(cells, 4),
        // 5 iSelfAnimID2 .. 13 iTargetPart
        Target = TblCell.I32(cells, 14),
        NeedLevel = TblCell.I32(cells, 15),
        NeedSkill = TblCell.I32(cells, 16),
        ExhaustMsp = TblCell.I32(cells, 17),
        ExhaustHp = TblCell.I32(cells, 18),
        NeedItem = TblCell.U32(cells, 19),
        ExhaustItem = TblCell.U32(cells, 20),
        CastTime = TblCell.I32(cells, 21),
        ReCastTime = TblCell.I32(cells, 22),
        // 23 fIDK0, 24 fIDK1
        PercentSuccess = TblCell.I32(cells, 25),
        FirstTableType = TblCell.U32(cells, 26),
        SecondTableType = TblCell.U32(cells, 27),
        ValidDist = TblCell.I32(cells, 28),
    };
}

/// <summary>
/// Port of <c>s_pTbl_Skill</c> (GameBase.cpp, <c>Data\skill_magic_main&lt;lang&gt;.tbl</c>).
/// Rows are exposed both keyed by id (<see cref="Find"/>) and in table order
/// (<see cref="All"/> / the indexer), mirroring the C++ <c>Find</c> /
/// <c>GetIndexedData</c> pair the skill tree walks.
/// </summary>
public sealed class SkillTableSet
{
    /// <summary>UIITEM_TYPE_USABLE_ID_MIN — skill ids at/above this are usable-items, not class skills.</summary>
    public const uint UsableItemIdMin = 450000;

    private readonly SkillRow[] _rows;
    private readonly Dictionary<uint, SkillRow> _byId = [];

    public SkillTableSet(N3TableFile table)
    {
        _rows = new SkillRow[table.Rows.Count];
        for (int i = 0; i < _rows.Length; i++)
        {
            SkillRow row = SkillRow.FromCells(table.Rows[i]);
            _rows[i] = row;
            _byId[row.Id] = row; // last wins on a duplicate id, like the C++ map
        }
    }

    public static SkillTableSet LoadFromFile(string path) => new(N3TableFile.LoadFromFile(path));

    /// <summary>Table-order rows (the C++ <c>GetIndexedData</c> enumeration).</summary>
    public IReadOnlyList<SkillRow> All => _rows;

    public int Count => _rows.Length;

    public SkillRow this[int index] => _rows[index];

    /// <summary>The row keyed by its skill id, or null (the C++ <c>Find</c>).</summary>
    public SkillRow? Find(uint id) => _byId.TryGetValue(id, out SkillRow? row) ? row : null;
}
