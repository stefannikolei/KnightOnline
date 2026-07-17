using OpenKO.Client.Game.Ui;

namespace OpenKO.Client;

/// <summary>
/// File-backed <see cref="IHotkeyStore"/> — the registry replacement
/// (<c>CGameProcedure::RegPutSetting</c>/<c>RegGetSetting</c> "Count"/"Data{n}"). Persists the
/// hotkey grid to a small per-character binary under the user profile, keyed exactly like the
/// original: a <c>Count</c> header then <c>Data{n} = {row, col, skillId}</c> triples. Only the
/// executable constructs this (it touches real file paths); tests use
/// <see cref="InMemoryHotkeyStore"/>.
/// </summary>
public sealed class FileHotkeyStore : IHotkeyStore
{
    private const int Magic = 0x484B4559; // 'HKEY'

    private readonly string _path;

    public FileHotkeyStore(string account, string character)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenKO", "hotkeys");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, Sanitize(account) + "_" + Sanitize(character) + ".dat");
    }

    public void Save(IEnumerable<HotkeyEntry> entries)
    {
        List<HotkeyEntry> list = [.. entries];
        using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write(Magic);
        w.Write(list.Count);
        foreach (HotkeyEntry e in list)
        {
            w.Write(e.Page);
            w.Write(e.Slot);
            w.Write(e.SkillId);
        }
    }

    public IReadOnlyList<HotkeyEntry> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read);
            using var r = new BinaryReader(fs);
            if (r.ReadInt32() != Magic)
                return [];

            int count = r.ReadInt32();
            if (count is < 0 or > PageSlotMax)
                return [];

            var list = new List<HotkeyEntry>(count);
            for (int i = 0; i < count; i++)
            {
                int page = r.ReadInt32();
                int slot = r.ReadInt32();
                uint id = r.ReadUInt32();
                list.Add(new HotkeyEntry(page, slot, id));
            }

            return list;
        }
        catch (IOException)
        {
            // Truncated / unreadable file (EndOfStreamException is an IOException) → no hotkeys.
            return [];
        }
    }

    private const int PageSlotMax = HotKeyDialog.PageCount * HotKeyDialog.SlotCount;

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "default";
        Span<char> buf = stackalloc char[s.Length];
        for (int i = 0; i < s.Length; i++)
            buf[i] = Array.IndexOf(Path.GetInvalidFileNameChars(), s[i]) >= 0 ? '_' : s[i];
        return new string(buf);
    }
}
