using OpenKO.Client.Assets.Player;

namespace OpenKO.Client.Assets.Effects;

/// <summary>
/// A row of <c>__TABLE_FX</c> (Client/WarFare/GameDef.h:1187, <c>Data\fx.tbl</c>) —
/// one effect source: its id, display name, the <c>.fxb</c> effect-bundle filename,
/// the sound id fired with it, and an AOE flag. 1-based struct fields map to the
/// 0-based column index = field - 1.
/// </summary>
/// <param name="Id">field 01 — <c>dwID</c>, the effect id (also the value skill/item rows reference).</param>
/// <param name="Name">field 02 — <c>szName</c>, the effect's display name (CP949, usually empty).</param>
/// <param name="FileName">
/// field 03 — <c>szFN</c>, the <c>.fxb</c> bundle path. Exposed raw; the C++ client
/// lower-cases it before lookup, so callers resolving the file should apply
/// <c>ToLowerInvariant()</c> themselves.
/// </param>
/// <param name="SoundId">field 04 — <c>dwSoundID</c>, the <c>sound.tbl</c> id played with the effect (0 = none).</param>
/// <param name="Aoe">field 05 — <c>byAOE</c>, the area-of-effect flag.</param>
public readonly record struct FxSourceRow(uint Id, string Name, string FileName, uint SoundId, byte Aoe);

/// <summary>
/// Port of the client's <c>__TABLE_FX</c> table (<c>Data\fx.tbl</c>) keyed by effect
/// id. Resolves a skill/item FX id to its <c>.fxb</c> bundle + sound, exactly as the
/// C++ client does when spawning an effect (CN3FXMgr / skill-cast FX lookup).
/// </summary>
public sealed class FxSourceTable
{
    private readonly N3TableFile _table;

    public FxSourceTable(N3TableFile table) => _table = table;

    public static FxSourceTable LoadFromFile(string path) => new(N3TableFile.LoadFromFile(path));

    /// <summary>The effect source for an id, or null.</summary>
    public FxSourceRow? Find(uint fxId)
    {
        object[]? cells = _table.Find(fxId);
        return cells == null ? null : FromCells(cells);
    }

    /// <summary>Tries to resolve the effect source for an id (the C++ <c>Find</c>).</summary>
    public bool TryGet(uint fxId, out FxSourceRow row)
    {
        object[]? cells = _table.Find(fxId);
        if (cells == null)
        {
            row = default;
            return false;
        }

        row = FromCells(cells);
        return true;
    }

    private static FxSourceRow FromCells(object[] cells) => new(
        TblCell.U32(cells, 0),
        TblCell.Str(cells, 1),
        TblCell.Str(cells, 2),
        TblCell.U32(cells, 3),
        TblCell.U8(cells, 4));
}
