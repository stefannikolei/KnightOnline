namespace OpenKO.Client.Assets.Player;

/// <summary>
/// A row of <c>__TABLE_PLAYER_LOOKS</c> (GameDef.h) — the base looks for a race
/// (UPC_DefaultLooks.tbl) or an NPC model (NPC_Looks.tbl): the skeleton and
/// animation files, the ten default part file names (indexed by
/// <see cref="KoPartPosition"/>), and the joint anchors for weapons/shield/cape.
/// </summary>
public sealed class PlayerLooksRow
{
    public uint Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string JointFileName { get; init; } = string.Empty;

    public string AniCtrlFileName { get; init; } = string.Empty;

    /// <summary>Default part file names, indexed by <see cref="KoPartPosition"/> (10 columns).</summary>
    public string[] PartFileNames { get; init; } = new string[10];

    public string SkinFileName { get; init; } = string.Empty;

    /// <summary>When set, a baked whole .n3chr replaces the runtime assembly.</summary>
    public string ChrFileName { get; init; } = string.Empty;

    public string FxPlugFileName { get; init; } = string.Empty;

    public int JointRightHand { get; init; }

    public int JointLeftHand { get; init; }

    /// <summary>Left forearm joint — the shield anchor.</summary>
    public int JointLeftForearm { get; init; }

    public int JointCloak { get; init; }

    internal static PlayerLooksRow FromCells(object[] cells)
    {
        var parts = new string[10];
        for (int i = 0; i < 10; i++)
            parts[i] = TblCell.Str(cells, 4 + i);

        return new PlayerLooksRow
        {
            Id = TblCell.U32(cells, 0),
            Name = TblCell.Str(cells, 1),
            JointFileName = TblCell.Str(cells, 2),
            AniCtrlFileName = TblCell.Str(cells, 3),
            PartFileNames = parts,
            SkinFileName = TblCell.Str(cells, 14),
            ChrFileName = TblCell.Str(cells, 15),
            FxPlugFileName = TblCell.Str(cells, 16),
            // 17 = iIdk1
            JointRightHand = TblCell.I32(cells, 18),
            JointLeftHand = TblCell.I32(cells, 19),
            JointLeftForearm = TblCell.I32(cells, 20),
            JointCloak = TblCell.I32(cells, 21),
        };
    }
}

/// <summary>
/// Port of <c>s_pTbl_UPC_Looks</c> / <c>s_pTbl_NPC_Looks</c> (GameBase.cpp) — a
/// <see cref="N3TableFile"/> whose DWORD id column keys the base-looks row (by
/// race for players, by model id for NPCs).
/// </summary>
public sealed class PlayerLooksTable
{
    private readonly N3TableFile _table;

    public PlayerLooksTable(N3TableFile table) => _table = table;

    public static PlayerLooksTable LoadFromFile(string path) =>
        new(N3TableFile.LoadFromFile(path));

    /// <summary>The looks row for a race (players) or model id (NPCs), or null.</summary>
    public PlayerLooksRow? Find(uint id)
    {
        object[]? cells = _table.Find(id);
        return cells == null ? null : PlayerLooksRow.FromCells(cells);
    }

    public PlayerLooksRow? Find(KoRace race) => Find((uint)race);
}
