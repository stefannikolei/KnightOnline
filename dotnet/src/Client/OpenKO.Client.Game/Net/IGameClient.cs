namespace OpenKO.Client.Game.Net;

/// <summary>
/// The connection surface the game states drive (CAPISocket, abstracted so the
/// flow states are headless-testable with a fake). Mirrors the reused
/// <c>s_pSocket</c>: one link that first talks to the login server, then
/// reconnects to the Ebenezer game server.
/// </summary>
public interface IGameClient
{
    bool CryptionEnabled { get; }

    /// <summary>Frames (and encrypts once keyed) a payload and queues it.</summary>
    void Send(ReadOnlySpan<byte> payload);

    /// <summary>Requests a (re)connect to a server endpoint (login → game).</summary>
    void Connect(string host, int port);

    /// <summary>CAPISocket::InitCrypt — enable encryption with the server key.</summary>
    void EnableCryption(ulong publicKey);
}
