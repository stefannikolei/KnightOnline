using OpenKO.Client.Game.Net;

namespace OpenKO.Client.Game.States;

/// <summary>
/// The shared game session state — the C# analog of the CGameProcedure statics
/// (s_pSocket, s_szAccount/Password/Server, s_iChrSelectIndex, the player's
/// nation/spawn). States read and mutate it and drive transitions through
/// <see cref="Machine"/>. Events let a UI/viewer (or a test) observe the flow.
/// </summary>
public sealed class GameContext
{
    public const int LoginServerPort = 15100; // SOCKET_PORT_LOGIN
    public const int GameServerPort = 15001;  // SOCKET_PORT_GAME

    public GameContext(IGameClient client)
    {
        Client = client;
        Machine = new GameStateMachine();
        Login = new LoginState(this);
        NationSelect = new NationSelectState(this);
        CharSelect = new CharSelectState(this);
        CharCreate = new CharCreateState(this);
        InGame = new InGameState(this);
    }

    public IGameClient Client { get; }

    public GameStateMachine Machine { get; }

    // The concrete state singletons (like the s_pProc* pointers).
    public LoginState Login { get; }

    public NationSelectState NationSelect { get; }

    public CharSelectState CharSelect { get; }

    public CharCreateState CharCreate { get; }

    public InGameState InGame { get; }

    // Shared session fields.
    public string Account { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public IReadOnlyList<ServerListEntry> Servers { get; set; } = [];

    public byte Nation { get; set; }

    public IReadOnlyList<CharacterSlot> Characters { get; set; } = [];

    public int SelectedCharIndex { get; set; } = -1;

    public SelectCharResult Spawn { get; set; }

    // Flow observation hooks (optional).
    public Action<IReadOnlyList<ServerListEntry>>? ServerListReceived { get; set; }

    public Action<NewsResult>? NewsReceived { get; set; }

    public Action<AccountLoginResult>? AccountLoginResult { get; set; }

    public Action<IReadOnlyList<CharacterSlot>>? CharactersReceived { get; set; }

    public Action<byte>? NationResolved { get; set; }

    public Action<SelectCharResult>? EnteredGame { get; set; }

    /// <summary>
    /// The shared base handling (CGameProcedure::ProcessPacket): the crypt
    /// handshake and character-select spawn that every state delegates to first.
    /// Returns true when consumed.
    /// </summary>
    public bool ProcessSharedPacket(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            return false;

        switch ((OpenKO.Core.Protocol.GameOpcode)payload[0])
        {
            case OpenKO.Core.Protocol.GameOpcode.WIZ_VERSION_CHECK:
            {
                VersionCheckResult version = GameProtocol.ParseVersionCheck(payload);
                Client.EnableCryption(version.PublicKey);
                // With the link now encrypted, log into the game server.
                Client.Send(GameProtocol.BuildGameLogin(Account, Password));
                return true;
            }

            case OpenKO.Core.Protocol.GameOpcode.WIZ_SEL_CHAR:
            {
                SelectCharResult spawn = GameProtocol.ParseSelectCharacter(payload);
                if (spawn.Success)
                {
                    Spawn = spawn;
                    Machine.SetActive(InGame);
                    EnteredGame?.Invoke(spawn);
                }

                return true;
            }

            default:
                return false;
        }
    }
}
