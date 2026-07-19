using Microsoft.Extensions.Configuration;
using OpenKO.Client;

// The clean game takes no CLI args: the server endpoint + data path come from
// appsettings.json (section "Client") and Client__* environment variables.
IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

ClientConfig config = configuration.GetSection("Client").Get<ClientConfig>() ?? new ClientConfig();

// Auto-detect the asset corpus (walk up to Client/Data) when it is not configured.
config.DataPath ??= ClientConfig.FindDataPath();

// Graphics/sound settings from options.json next to the exe (the C++ Option.ini that
// Option.exe writes and WarFare.exe reads). A missing file yields normalised defaults.
OpenKO.Client.Configuration.GameSettings settings = OpenKO.Client.Configuration.GameSettingsStore.Load();

using var game = new KnightOnlineGame(config, settings);
game.Run();
return 0;
