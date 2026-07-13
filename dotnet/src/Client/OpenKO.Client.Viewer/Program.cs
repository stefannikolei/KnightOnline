using OpenKO.Client.Viewer;

string? dataPath = null;
string? startScene = null;
string? screenshotPath = null;
bool vsync = true;
bool fullscreen = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data" when i + 1 < args.Length:
            dataPath = args[++i];
            break;
        case "--scene" when i + 1 < args.Length:
            startScene = args[++i];
            break;
        case "--screenshot" when i + 1 < args.Length:
            screenshotPath = args[++i];
            break;
        case "--novsync":
            vsync = false;
            break;
        case "--fullscreen":
            fullscreen = true;
            break;
        case "--help":
            Console.WriteLine(
                "usage: OpenKO.Client.Viewer [--data <Client/Data>] [--scene <name>] " +
                "[--screenshot <png>] [--novsync] [--fullscreen]  (F toggles fullscreen)");
            return 0;
    }
}

dataPath ??= FindDataPath();

using var game = new ViewerGame(dataPath, startScene, screenshotPath, vsync, fullscreen);
game.AddScene(new CharSelectScene());
game.AddScene(new TerrainScene());
game.AddScene(new CharacterScene());
game.AddScene(new UiBrowserScene());
game.AddScene(new MeshBrowserScene());
game.AddScene(new ShapeBrowserScene());
game.AddScene(new EmptyScene());
game.Run();
return 0;

// Walk up from the working directory looking for Client/Data (same
// convention as the test corpus lookup).
static string? FindDataPath()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "Client", "Data");
        if (Directory.Exists(candidate))
            return candidate;
    }

    return null;
}
