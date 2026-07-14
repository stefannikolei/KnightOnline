using OpenKO.Client.Assets.Player;

namespace OpenKO.Client.Assets.Zones;

/// <summary>
/// A row of <c>__TABLE_ZONE</c> (Zones.tbl, GameDef.h) — the map file references
/// and settings for one zone id: the terrain (.gtd), color/light maps, minimap,
/// sky setting and the enemy-indicator flag. Only the fields the client renderer
/// needs are surfaced; the rest of the row is skipped.
/// </summary>
public sealed class ZoneRow
{
    public uint Id { get; init; }

    /// <summary>col 02 — the terrain .gtd file.</summary>
    public string TerrainFileName { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>col 04 — the color-map .tct file.</summary>
    public string ColorMapFileName { get; init; } = string.Empty;

    /// <summary>col 05 — the light-map .tlt file.</summary>
    public string LightMapFileName { get; init; } = string.Empty;

    /// <summary>col 08 — the minimap .dxt file.</summary>
    public string MiniMapFileName { get; init; } = string.Empty;

    /// <summary>col 09 — the N3Sky setting file/name.</summary>
    public string SkySetting { get; init; } = string.Empty;

    /// <summary>col 10 — whether enemy players are shown on the minimap.</summary>
    public bool IndicateEnemyPlayer { get; init; }

    internal static ZoneRow FromCells(object[] cells) => new()
    {
        Id = TblCell.U32(cells, 0),
        TerrainFileName = TblCell.Str(cells, 1),
        Name = TblCell.Str(cells, 2),
        ColorMapFileName = TblCell.Str(cells, 3),
        LightMapFileName = TblCell.Str(cells, 4),
        // 5 OPD, 6 OPDEXT
        MiniMapFileName = TblCell.Str(cells, 7),
        SkySetting = TblCell.Str(cells, 8),
        IndicateEnemyPlayer = TblCell.I32(cells, 9) != 0,
    };
}

/// <summary>
/// Port of <c>s_pTbl_Zones</c> (GameBase.cpp) — <c>Data\Zones.tbl</c> keyed by
/// zone id. Resolves a spawn's zone id to its map files, exactly as the C++
/// client does when entering a zone (CGameProcMain zone load / N3Terrain).
/// </summary>
public sealed class ZoneTable
{
    private readonly N3TableFile _table;

    public ZoneTable(N3TableFile table) => _table = table;

    public static ZoneTable LoadFromFile(string path) => new(N3TableFile.LoadFromFile(path));

    /// <summary>The zone row for an id (as sent in WIZ_MYINFO / spawn), or null.</summary>
    public ZoneRow? Find(uint zoneId)
    {
        object[]? cells = _table.Find(zoneId);
        return cells == null ? null : ZoneRow.FromCells(cells);
    }

    public ZoneRow? Find(int zoneId) => Find((uint)zoneId);
}
