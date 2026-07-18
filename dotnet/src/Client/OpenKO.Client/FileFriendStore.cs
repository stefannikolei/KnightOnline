using OpenKO.Client.Game.Ui;

namespace OpenKO.Client;

/// <summary>
/// File-backed <see cref="IFriendStore"/> — the replacement for
/// <c>CUIFriends::SaveListToTextFile</c> / <c>LoadListFromTextFile</c>. The original keeps the
/// list in an <c>{account}_{server}.txt</c> next to the client, one name per line; this writes the
/// same one-name-per-line text under the user profile config dir so the friends list survives a
/// restart. Purely client-side — the server never owns it. Only the executable constructs this
/// (it touches real file paths); tests use <see cref="InMemoryFriendStore"/>.
/// </summary>
public sealed class FileFriendStore : IFriendStore
{
    private readonly string _path;

    public FileFriendStore(string account, string server)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenKO", "friends");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, Sanitize(account) + "_" + Sanitize(server) + ".txt");
    }

    public void Save(IEnumerable<string> names)
    {
        // Mirror CUIFriends: keep only plausible names (3 < len <= 22 in the C++ loader).
        IEnumerable<string> valid = names.Where(n => n.Length is > 0 and <= 20);
        File.WriteAllLines(_path, valid);
    }

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            var names = new List<string>();
            foreach (string raw in File.ReadAllLines(_path))
            {
                string name = raw.Trim();
                if (name.Length is > 0 and <= 20)
                    names.Add(name);
            }

            return names;
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static string Sanitize(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Length == 0 ? "_" : value;
    }
}
