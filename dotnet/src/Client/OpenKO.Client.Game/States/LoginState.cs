using OpenKO.Client.Game.Net;
using OpenKO.Core.Protocol;

namespace OpenKO.Client.Game.States;

/// <summary>
/// Port of CGameProcLogIn: connect to the login server, fetch the server list,
/// account-login, then connect to the chosen game server and run the
/// WIZ_VERSION_CHECK → WIZ_LOGIN handshake, branching to nation- or
/// character-select on the nation byte.
/// </summary>
public sealed class LoginState(GameContext context) : GameState
{
    public override string Name => "Login";

    /// <summary>The login server is connected by the host; ask for the server list.</summary>
    public override void Init() => context.Client.Send(LoginProtocol.BuildServerListRequest());

    /// <summary>MsgSend_AccountLogIn.</summary>
    public void SubmitAccountLogin(string account, string password)
    {
        context.Account = account;
        context.Password = password;
        context.Client.Send(LoginProtocol.BuildAccountLogin(account, password));
    }

    /// <summary>ConnectToGameServer: reconnect the link and start the version check.</summary>
    public void ConnectToGameServer(ServerListEntry server)
    {
        context.ServerName = server.Name;
        context.Client.Connect(server.Ip, GameContext.GameServerPort);
        context.Client.Send(GameProtocol.BuildVersionCheck());
    }

    public override bool ProcessPacket(ReadOnlySpan<byte> payload)
    {
        if (context.ProcessSharedPacket(payload))
            return true;

        switch ((LoginOpcode)payload[0])
        {
            case LoginOpcode.LS_SERVERLIST:
                context.Servers = LoginProtocol.ParseServerList(payload);
                context.ServerListReceived?.Invoke(context.Servers);
                return true;

            case LoginOpcode.LS_LOGIN_REQ:
            {
                AccountLoginResult result = LoginProtocol.ParseAccountLogin(payload);
                // The news request must go out on the login-server link BEFORE the
                // callback: an auto-login callback may call ConnectToGameServer,
                // which swaps the connection to Ebenezer — a login opcode arriving
                // there after the crypt handshake closes the session.
                if (result.Success)
                    context.Client.Send(LoginProtocol.BuildNewsRequest());
                context.AccountLoginResult?.Invoke(result);
                return true;
            }

            case LoginOpcode.LS_NEWS:
                context.NewsReceived?.Invoke(LoginProtocol.ParseNews(payload));
                return true;
        }

        switch ((GameOpcode)payload[0])
        {
            case GameOpcode.WIZ_LOGIN:
            {
                byte nation = GameProtocol.ParseGameLogin(payload);
                context.Nation = nation;
                context.NationResolved?.Invoke(nation);

                // 0 = not selected → nation select; 1/2 = Karus/El Morad → char select.
                if (nation == 0)
                    context.Machine.SetActive(context.NationSelect);
                else if (nation is 1 or 2)
                    context.Machine.SetActive(context.CharSelect);
                return true;
            }
        }

        return false;
    }
}
