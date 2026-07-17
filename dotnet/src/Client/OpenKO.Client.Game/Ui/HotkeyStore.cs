namespace OpenKO.Client.Game.Ui;

/// <summary>One persisted hotkey placement — the C++ <c>CHotkeyData(row, col, dwID)</c>.</summary>
public readonly record struct HotkeyEntry(int Page, int Slot, uint SkillId);

/// <summary>
/// Persistence for the hotkey bar — the replacement for the original registry
/// (<c>CGameProcedure::RegPutSetting</c>/<c>RegGetSetting</c> "Count"/"Data{n}"). Kept as an
/// interface so the headless controller/tests use an in-memory store while the executable binds a
/// per-character file. <see cref="HotKeyDialog"/> saves on every change and loads on open.
/// </summary>
public interface IHotkeyStore
{
    /// <summary>Persist the full set of placements (replacing any prior contents).</summary>
    void Save(IEnumerable<HotkeyEntry> entries);

    /// <summary>Load the persisted placements (empty when none were saved).</summary>
    IReadOnlyList<HotkeyEntry> Load();
}

/// <summary>An in-memory <see cref="IHotkeyStore"/> for tests (and a null-object default).</summary>
public sealed class InMemoryHotkeyStore : IHotkeyStore
{
    private List<HotkeyEntry> _entries = [];

    public void Save(IEnumerable<HotkeyEntry> entries) => _entries = [.. entries];

    public IReadOnlyList<HotkeyEntry> Load() => _entries;
}
