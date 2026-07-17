using OpenKO.Client.Assets.Player;

namespace OpenKO.Client.Assets.Audio;

/// <summary>
/// A row of <c>__TABLE_SOUND</c> (Client/N3Base/N3SndDef.h:17, <c>Data\sound.tbl</c>) —
/// one sound resource: its id, wave filename, type, and the max number of concurrent
/// instances. 1-based struct fields map to the 0-based column index = field - 1.
/// </summary>
/// <param name="Id">field 01 — <c>dwID</c>, the sound id (referenced by <c>fx.tbl</c> and gameplay events).</param>
/// <param name="FileName">
/// field 02 — <c>szFN</c>, the <c>.wav</c> path. Exposed raw; the C++ client lower-cases
/// it before lookup, so callers should apply <c>ToLowerInvariant()</c> themselves.
/// </param>
/// <param name="Type">field 03 — <c>iType</c>, the sound category (SFX / voice / ambient, per N3SndDef.h).</param>
/// <param name="NumInst">field 04 — <c>iNumInst</c>, the maximum simultaneous instances of this sound.</param>
public readonly record struct SoundRow(uint Id, string FileName, int Type, int NumInst);

/// <summary>
/// Port of the client's <c>__TABLE_SOUND</c> table (<c>Data\sound.tbl</c>) keyed by
/// sound id. Resolves a sound id to its wave file + playback limits, exactly as the
/// C++ client does when a sound is requested (CN3SndMgr).
/// </summary>
public sealed class SoundTable
{
    private readonly N3TableFile _table;

    public SoundTable(N3TableFile table) => _table = table;

    public static SoundTable LoadFromFile(string path) => new(N3TableFile.LoadFromFile(path));

    /// <summary>The sound resource for an id, or null.</summary>
    public SoundRow? Find(uint id)
    {
        object[]? cells = _table.Find(id);
        return cells == null ? null : FromCells(cells);
    }

    /// <summary>Tries to resolve the sound resource for an id (the C++ <c>Find</c>).</summary>
    public bool TryGet(uint id, out SoundRow row)
    {
        object[]? cells = _table.Find(id);
        if (cells == null)
        {
            row = default;
            return false;
        }

        row = FromCells(cells);
        return true;
    }

    private static SoundRow FromCells(object[] cells) => new(
        TblCell.U32(cells, 0),
        TblCell.Str(cells, 1),
        TblCell.I32(cells, 2),
        TblCell.I32(cells, 3));
}
