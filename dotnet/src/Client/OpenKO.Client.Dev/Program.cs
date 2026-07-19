using OpenKO.Client;

if (args.Contains("--help"))
{
    Console.WriteLine(
        "usage: OpenKO.Client.Dev [--data <Client/Data>]\n" +
        "                         [--server <host[:port]> --account <id> --password <pw>]\n" +
        "                         [--offline <zone>] [--screenshot <png>]\n" +
        "  --server   connect and auto-run the login → char-select → in-game flow\n" +
        "  --offline  render a zone (e.g. 'moradon') with no server\n" +
        "  Esc quits.");
    return 0;
}

ClientOptions options = ClientOptions.Parse(args);

// Graphics/sound settings from options.json next to the exe (Option.ini equivalent).
OpenKO.Client.Configuration.GameSettings settings = OpenKO.Client.Configuration.GameSettingsStore.Load();

using var game = new DevClientGame(options, settings);
game.Run();
return 0;
