using OpenKO.Data.Models;

namespace OpenKO.Servers.Aujard;

/// <summary>
/// Replaces the KNIGHT_DB shared-memory <c>_USER_DATA</c> array: a fixed-size
/// in-process slot store the DB agent reads/writes and (in the C# topology)
/// Ebenezer accesses through the agent API instead of shared memory.
/// </summary>
public sealed class UserDataStore
{
    public const int DefaultCapacity = 3000; // MAX_USER

    private readonly UserData[] _slots;

    public UserDataStore(int capacity = DefaultCapacity)
    {
        _slots = new UserData[capacity];
        for (int i = 0; i < capacity; i++)
            _slots[i] = new UserData();
    }

    public int Capacity => _slots.Length;

    public UserData? Get(int userId)
        => userId >= 0 && userId < _slots.Length ? _slots[userId] : null;

    /// <summary>Port of GetUserPtr: finds an online user by charId (case-insensitive).</summary>
    public UserData? FindByCharId(string charId, out int userId)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].CharId.Length > 0
                && string.Equals(_slots[i].CharId, charId, StringComparison.OrdinalIgnoreCase))
            {
                userId = i;
                return _slots[i];
            }
        }

        userId = -1;
        return null;
    }

    /// <summary>Port of ResetUserData.</summary>
    public void Reset(int userId)
    {
        Get(userId)?.Reset();
    }
}
