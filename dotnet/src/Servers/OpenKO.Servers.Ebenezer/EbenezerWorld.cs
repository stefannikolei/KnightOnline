namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// EbenezerApp user bookkeeping (stage-4.1 slice): the user slots by socket id
/// and the account lookup CUser::LoginProcess uses for duplicate logins.
/// </summary>
public sealed class EbenezerWorld
{
    public const int MaxUser = 3000; // MAX_USER (Ebenezer Define.h)

    /// <summary>User slots by socket id.</summary>
    public readonly GameUser?[] Users = new GameUser?[MaxUser];

    /// <summary>EbenezerApp::GetUserPtr(name, NameType::Account) — case-insensitive.</summary>
    public GameUser? GetUserByAccount(string accountId)
    {
        foreach (GameUser? user in Users)
        {
            if (user is not null
                && user.AccountId.Length > 0
                && string.Equals(user.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                return user;
        }

        return null;
    }

    /// <summary>Claims the smallest free socket slot, -1 when the server is full.</summary>
    public short Register(Func<short, GameUser> factory)
    {
        for (short i = 0; i < Users.Length; i++)
        {
            if (Users[i] is null)
            {
                Users[i] = factory(i);
                return i;
            }
        }

        return -1;
    }

    public void Unregister(short socketId)
    {
        if (socketId >= 0 && socketId < Users.Length)
            Users[socketId] = null;
    }
}
