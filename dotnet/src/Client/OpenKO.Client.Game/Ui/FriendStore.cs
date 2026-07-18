namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Persistence for the client-local friends list — the replacement for
/// <c>CUIFriends::SaveListToTextFile</c> / <c>LoadListFromTextFile</c>, which the original keeps
/// in an <c>{account}_{server}.txt</c> file (one name per line). Kept as an interface so the
/// headless controller/tests use an in-memory store while the executable binds a per-account file.
/// The friends list is entirely client-side — the server never owns it.
/// </summary>
public interface IFriendStore
{
    /// <summary>Persist the full set of friend names (replacing any prior contents).</summary>
    void Save(IEnumerable<string> names);

    /// <summary>Load the persisted friend names (empty when none were saved).</summary>
    IReadOnlyList<string> Load();
}

/// <summary>An in-memory <see cref="IFriendStore"/> for tests (and a null-object default).</summary>
public sealed class InMemoryFriendStore : IFriendStore
{
    private List<string> _names = [];

    public void Save(IEnumerable<string> names) => _names = [.. names];

    public IReadOnlyList<string> Load() => _names;
}
