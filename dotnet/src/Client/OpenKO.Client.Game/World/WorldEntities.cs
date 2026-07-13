namespace OpenKO.Client.Game.World;

/// <summary>The local player (from WIZ_MYINFO): identity + spawn placement.</summary>
public sealed class LocalPlayer
{
    public short SocketId { get; set; }

    public string Name { get; set; } = string.Empty;

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public byte Nation { get; set; }

    public byte Race { get; set; }

    public short Class { get; set; }

    public byte Face { get; set; }

    public byte Hair { get; set; }

    public byte Level { get; set; }
}

/// <summary>A visible remote player (from WIZ_USER_INOUT / WIZ_REQ_USERIN).</summary>
public sealed class RemotePlayer
{
    public short Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte Nation { get; set; }

    public byte Level { get; set; }

    public byte Race { get; set; }

    public short Class { get; set; }

    public byte Face { get; set; }

    public byte Hair { get; set; }

    public short Direction { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}

/// <summary>
/// The client-side world roster (the CPlayerOtherMgr analog): the local player
/// plus the region-visible remote players keyed by their server socket id.
/// In-game packet handlers populate it; the renderer walks it.
/// </summary>
public sealed class WorldEntities
{
    private readonly Dictionary<short, RemotePlayer> _players = [];

    public LocalPlayer Local { get; } = new();

    public IReadOnlyDictionary<short, RemotePlayer> Players => _players;

    /// <summary>WIZ_USER_INOUT (in) — add or refresh a visible player.</summary>
    public void AddOrUpdate(RemotePlayer player) => _players[player.Id] = player;

    /// <summary>WIZ_USER_INOUT (out) — drop a player that left the region.</summary>
    public bool Remove(short id) => _players.Remove(id);

    public bool TryGet(short id, out RemotePlayer player) => _players.TryGetValue(id, out player!);

    /// <summary>WIZ_MOVE — apply a position update to the local or a remote entity.</summary>
    public void Move(short id, float x, float y, float z)
    {
        if (id == Local.SocketId)
        {
            Local.X = x;
            Local.Y = y;
            Local.Z = z;
        }
        else if (_players.TryGetValue(id, out RemotePlayer? player))
        {
            player.X = x;
            player.Y = y;
            player.Z = z;
        }
    }

    public void Clear() => _players.Clear();
}
