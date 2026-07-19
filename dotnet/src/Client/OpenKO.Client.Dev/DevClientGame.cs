using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client;

/// <summary>
/// The dev/debug client: subclasses the shared <see cref="KnightOnlineGame"/> and
/// re-adds the CLI/debug capabilities the clean game deliberately drops —
/// <c>--offline &lt;zone&gt;</c> zone rendering, scripted <c>--account</c> auto-login,
/// the <c>--screenshot</c> dump and the immediate-mode text HUD. All of this lives
/// here so none of it is compiled into the clean <c>OpenKO.Client</c> exe.
/// </summary>
public sealed class DevClientGame(ClientOptions options, OpenKO.Client.Configuration.GameSettings settings)
    : KnightOnlineGame(options.ToConfig(), settings)
{
    private readonly ClientOptions _options = options;
    private int _framesDrawn;

    // ---- Start-up mode selection --------------------------------------------

    protected override void OnStart()
    {
        // --offline renders a zone with no server; --account scripts the login;
        // otherwise fall through to the base interactive online flow.
        if (_options.OfflineZone != null && Config.DataPath != null)
            StartOfflineZone();
        else
            base.OnStart();
    }

    /// <summary>
    /// The interactive frontend is replaced by the scripted auto-login when
    /// <c>--account</c> is given; otherwise the base interactive frontend is used.
    /// </summary>
    protected override void SetupLoginUi()
    {
        if (_options.Account.Length > 0)
            WireAutoLogin();
        else
            base.SetupLoginUi();
    }

    // ---- Offline zone demo ---------------------------------------------------

    private void StartOfflineZone()
    {
        Context = new GameContext(new NullGameClient());
        try
        {
            string gtd = Path.Combine(_options.DataPath!, "Zones", _options.OfflineZone + ".gtd");
            var resolver = new KoPathResolver(_options.DataPath!);

            // Zone minimap texture (CUIStateBar::LoadMap) — the zone .dxt beside the terrain.
            MinimapFile = $"Zones\\{_options.OfflineZone}.dxt";

            // Place the player at the map centre, on the terrain surface.
            BuildZoneScene(gtd, resolver, useCentreSpawn: true, spawn: default);
            float centre = MapWorldSize * 0.5f;

            Context.Spawn = new SelectCharResult(
                1, 0, (ushort)(centre * 10f), (ushort)(centre * 10f), (short)(PlayerPos.Y * 10f), 1);
            Context.InGame.World.Local.X = PlayerPos.X;
            Context.InGame.World.Local.Y = PlayerPos.Y;
            Context.InGame.World.Local.Z = PlayerPos.Z;
            Context.Machine.SetActive(Context.InGame);
            EnsureInGameUi();
            Log($"Offline zone '{_options.OfflineZone}' loaded ({TerrainData!.MapSize} tiles).");
        }
        catch (Exception ex)
        {
            Log($"Zone load failed: {ex.Message}");
        }
    }

    // ---- Scripted auto-login (--account) ------------------------------------

    private void WireAutoLogin()
    {
        Context.ServerListReceived = servers =>
        {
            Log($"Server list: {servers.Count} server(s).");
            Context.Login.SubmitAccountLogin(_options.Account, _options.Password);
        };
        Context.AccountLoginResult = result =>
        {
            Log($"Account login: result {result.Result}.");
            if (result.Success && Context.Servers.Count > 0)
                Context.Login.ConnectToGameServer(Context.Servers[0]);
        };
        Context.NationResolved = nation => Log($"Nation: {nation}.");
        Context.CharactersReceived = chars =>
        {
            int slot = -1;
            for (int i = 0; i < chars.Count; i++)
                if (!chars[i].IsEmpty) { slot = i; break; }

            Log(slot >= 0 ? $"Selecting character '{chars[slot].CharId}'." : "No characters on the account.");
            if (slot >= 0)
                Context.CharSelect.SelectCharacter(slot);
        };
    }

    // ---- Debug overlay + screenshot dump ------------------------------------

    protected override void OnAfterDraw(GameTime gameTime)
    {
        // The immediate-mode debug HUD stands in for the real HUD when it could not
        // be built (no --data / not yet in-game).
        if (!HasInGameHud)
            DrawHud();

        // --screenshot: dump the back buffer after a few frames, then exit.
        if (_options.ScreenshotPath != null && ++_framesDrawn == 30)
        {
            SaveScreenshot(_options.ScreenshotPath);
            Console.WriteLine($"Screenshot: {_options.ScreenshotPath}");
            Exit();
        }
    }

    private void DrawHud()
    {
        DynamicSpriteFont title = Fonts.GetUiFont(18);
        DynamicSpriteFont body = Fonts.GetUiFont(11);

        SpriteBatch.Begin();
        SpriteBatch.DrawString(title, "Knight Online — OpenKO C# Port", new Vector2(16, 12), Color.White);

        string state = Context.Machine.Active?.Name ?? "—";
        SpriteBatch.DrawString(body, $"State: {state}", new Vector2(16, 44), new Color(180, 210, 255));

        if (TerrainData != null)
        {
            SpriteBatch.DrawString(body, "WASD move · ←→ camera · click target · Esc quit",
                new Vector2(GraphicsDevice.Viewport.Width - 330, 44), new Color(150, 160, 180));
            SpriteBatch.DrawString(body, $"Target: {Selection}  {TargetHp}",
                new Vector2(GraphicsDevice.Viewport.Width - 330, 62), new Color(255, 200, 160));
        }

        if (Context.Machine.Active == Context.InGame)
        {
            var l = Context.InGame.World.Local;
            SpriteBatch.DrawString(body,
                $"Zone {Context.Spawn.Zone}  pos ({l.X:F0}, {l.Y:F0}, {l.Z:F0})  " +
                $"players: {Context.InGame.World.Players.Count}  npcs: {Context.InGame.World.Npcs.Count}",
                new Vector2(16, 62), new Color(180, 255, 200));

            // Full character sheet once the WIZ_MYINFO block has landed (level > 0).
            if (l.Level > 0)
            {
                SpriteBatch.DrawString(body,
                    $"{l.Name}  Lv {l.Level}   HP {l.Hp}/{l.MaxHp}   MP {l.Mp}/{l.MaxMp}   " +
                    $"AC {l.TotalAc}   Gold {l.Gold:N0}",
                    new Vector2(16, 80), new Color(255, 230, 160));
                SpriteBatch.DrawString(body,
                    $"STR {l.Str}+{l.ItemStr}  STA {l.Sta}+{l.ItemSta}  DEX {l.Dex}+{l.ItemDex}  " +
                    $"INT {l.Intel}+{l.ItemIntel}  CHA {l.Cha}+{l.ItemCha}   items {Context.InGame.Inventory.Slots.Count}",
                    new Vector2(16, 96), new Color(200, 220, 180));
            }
        }

        int y = GraphicsDevice.Viewport.Height - 16 - LogLines.Count * 16;
        foreach (string line in LogLines)
        {
            SpriteBatch.DrawString(body, line, new Vector2(16, y), new Color(200, 200, 200));
            y += 16;
        }

        SpriteBatch.End();
    }

    private void SaveScreenshot(string path)
    {
        int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var data = new Color[w * h];
        GraphicsDevice.GetBackBufferData(data);
        using var tex = new Texture2D(GraphicsDevice, w, h);
        tex.SetData(data);
        using FileStream fs = File.Create(path);
        tex.SaveAsPng(fs, w, h);
    }
}

/// <summary>A no-op client for the offline zone demo (no networking).</summary>
internal sealed class NullGameClient : IGameClient
{
    public bool CryptionEnabled => false;

    public void Send(ReadOnlySpan<byte> payload) { }

    public void Connect(string host, int port) { }

    public void EnableCryption(ulong publicKey) { }
}
