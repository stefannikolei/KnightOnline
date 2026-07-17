using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Client.Game.World;

namespace OpenKO.Client;

/// <summary>
/// Device-level glue for the in-game HUD (state bar / target bar / chat / message
/// window / command bar / dead dialog): loads the per-nation .uif layouts via
/// <c>UIs_us.tbl</c>, builds the dialog controllers, anchors each dialog like
/// <c>CGameProcMain::CreateUIs</c> (state bar top-left, target bar top-centre, command
/// bar bottom-centre, chat bottom-left, message window beside chat), draws them through
/// the <see cref="UiScreenRenderer"/> and paints the chat / message scrollback line by
/// line. The in-game analogue of <see cref="FrontendUi"/>; replaces the immediate-mode
/// <c>DrawHud</c> once the player has entered the world.
/// </summary>
public sealed class InGameUi : IDisposable
{
    private readonly GameContext _context;
    private readonly GraphicsDevice _device;
    private readonly KoPathResolver _resolver;
    private readonly TextureCache _textures;
    private readonly UiScreenRenderer _screen;
    private readonly SpriteBatch _spriteBatch;
    private readonly FontService _fonts;

    private readonly UiControl? _chatOutput;   // text0 — chat scrollback region
    private readonly UiControl? _msgOutput;    // text_message — system message region

    public UiManager Manager { get; } = new();

    public StateBarDialog StateBar { get; }

    public TargetBarDialog TargetBar { get; }

    public ChatDialog Chat { get; }

    public MessageWndDialog MessageWnd { get; }

    public CmdBarDialog CmdBar { get; }

    public DeadDialog Dead { get; }

    /// <summary>The inventory dialog — null when the layout or item tables failed to load.</summary>
    public InventoryDialog? Inventory { get; }

    public event Action<string>? Log;

    public InGameUi(GameContext context, GraphicsDevice device, FontService fonts, string dataPath)
    {
        _context = context;
        _device = device;
        _fonts = fonts;
        _resolver = new KoPathResolver(dataPath);
        _textures = new TextureCache(device, _resolver);
        _screen = new UiScreenRenderer(device, _textures, fonts);
        _spriteBatch = new SpriteBatch(device);

        int nation = context.Nation is 1 or 2 ? context.Nation : 1;

        string tbl = _resolver.Resolve("Data\\UIs_us.tbl")
            ?? throw new FileNotFoundException("Data\\UIs_us.tbl not found under " + dataPath);
        var table = UiResourceTable.LoadFromFile(tbl);

        UiControl stateRoot = LoadDialog(table.StateBar(nation))
            ?? throw new FileNotFoundException("StateBar layout not found: " + table.StateBar(nation));
        UiControl targetRoot = LoadDialog(table.TargetBar(nation))
            ?? throw new FileNotFoundException("TargetBar layout not found: " + table.TargetBar(nation));
        UiControl cmdRoot = LoadDialog(table.Cmd(nation))
            ?? throw new FileNotFoundException("Cmd layout not found: " + table.Cmd(nation));
        UiControl chatRoot = LoadDialog(table.Chat(nation))
            ?? throw new FileNotFoundException("Chat layout not found: " + table.Chat(nation));
        UiControl msgRoot = LoadDialog(table.MsgOutput(nation))
            ?? throw new FileNotFoundException("MsgOutput layout not found: " + table.MsgOutput(nation));
        UiControl deadRoot = LoadDialog(table.Dead(nation))
            ?? throw new FileNotFoundException("Dead layout not found: " + table.Dead(nation));

        // CGameProcMain::CreateUIs anchoring (resolution-relative).
        int w = device.Viewport.Width;
        int h = device.Viewport.Height;

        // Command bar — bottom-centre.
        cmdRoot.SetPos((w - cmdRoot.Width) / 2, h - cmdRoot.Height);
        // Chat — bottom-left, sitting above the command bar.
        chatRoot.SetPos(0, h - (chatRoot.Height + cmdRoot.Height));
        // Message window — immediately to the right of the chat window.
        msgRoot.SetPos(chatRoot.Region.Right, chatRoot.Region.Top);
        // State bar — top-left; target bar — top-centre.
        stateRoot.SetPos(0, 0);
        targetRoot.SetPos((w - targetRoot.Width) / 2, 0);
        // Dead dialog — centred (shown only on local death).
        deadRoot.SetPosCenter(w, h);

        // Build the controllers around the roots.
        StateBar = new StateBarDialog(context, stateRoot);
        TargetBar = new TargetBarDialog(context, targetRoot);
        CmdBar = new CmdBarDialog(context, cmdRoot);
        Chat = new ChatDialog(context, chatRoot);
        MessageWnd = new MessageWndDialog(context, msgRoot);
        Dead = new DeadDialog(context, deadRoot);

        _chatOutput = chatRoot.GetChildById("text0");
        _msgOutput = msgRoot.GetChildById("text_message");

        // Inventory — centred, hidden until toggled (btn_inventory / hotkey). Needs the item
        // tables; degrades gracefully (no dialog) when the layout or tables are missing.
        UiControl? invRoot = LoadDialog(table.Inventory(nation));
        ItemTableSet? items = TryLoadItems(_resolver);
        if (invRoot != null && items != null)
        {
            invRoot.SetPosCenter(w, h);
            Inventory = new InventoryDialog(context, invRoot, items, Manager.IconDrag);
            Manager.BindIconDragState(invRoot);
            Manager.Add(invRoot);
        }
        else if (invRoot == null)
        {
            Log?.Invoke("Inventory layout not found: " + table.Inventory(nation));
        }
        else
        {
            Log?.Invoke("Inventory item tables not found; inventory disabled.");
        }

        // Register with the manager (last added = topmost = input first). Chat sits above
        // the passive bars so its edit/buttons grab input; the death dialog floats on top.
        Manager.Add(stateRoot);
        Manager.Add(targetRoot);
        Manager.Add(cmdRoot);
        Manager.Add(msgRoot);
        Manager.Add(chatRoot);
        Manager.Add(deadRoot);
    }

    /// <summary>
    /// Load the item tables (Data\Item_Org_us.tbl + Item_Ext_i_us.tbl), mirroring
    /// <c>CharacterFactory.TryLoad</c>. Returns null when the base table is missing.
    /// </summary>
    private static ItemTableSet? TryLoadItems(KoPathResolver resolver, string lang = "us")
    {
        string? itemPath = resolver.Resolve($"Data\\Item_Org_{lang}.tbl");
        if (itemPath == null)
            return null;

        try
        {
            var basic = N3TableFile.LoadFromFile(itemPath);
            var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
            for (int i = 0; i < ItemTableSet.MaxItemExtension; i++)
            {
                string? extPath = resolver.Resolve($"Data\\Item_Ext_{i}_{lang}.tbl");
                if (extPath != null)
                    exts[i] = N3TableFile.LoadFromFile(extPath);
            }

            return new ItemTableSet(basic, exts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Wire the in-game hooks the executable does NOT already own: MyInfo/HpChange feed the
    /// state bar and WIZ_CHAT feeds the chat window. TargetHpReceived and EntityDied are left
    /// to the executable (it routes them to <see cref="TargetBar"/> / <see cref="Dead"/>).
    /// </summary>
    public void Bind(InGameState inGame)
    {
        StateBar.Bind(inGame);   // MyInfoReceived + HpChanged
        Chat.Bind(inGame);       // ChatReceived

        // Inventory: repopulate on MyInfo (additive, doesn't clobber the state bar's hook) and
        // commit/rollback the drag on the WIZ_ITEM_MOVE reply, refreshing the HP/MP bars whose
        // maxima the equip change recomputed.
        if (Inventory is { } inv)
        {
            inv.Bind(inGame);
            inGame.ItemMoveResult += res =>
            {
                inv.OnItemMoveResult(res.Success);
                if (res.Success)
                {
                    LocalPlayer l = inGame.World.Local;
                    StateBar.UpdateHp(l.Hp, l.MaxHp);
                    StateBar.UpdateMp(l.Mp, l.MaxMp);
                }
            };
        }

        // Fold buttons toggle their window's visibility (CUIChat/CUIMessageWnd btn_off).
        Chat.FoldRequested += () => Chat.Root.SetVisible(!Chat.Root.Visible);
        MessageWnd.FoldRequested += () => MessageWnd.Root.SetVisible(!MessageWnd.Root.Visible);

        // Command buttons / revival: logged for now; real behaviour lands in later slices.
        CmdBar.Command += id => Log?.Invoke($"Command: {id}");
        CmdBar.Command += id =>
        {
            if (id == "btn_inventory")
                ToggleInventory();
        };
        Dead.RevivalRequested += type =>
        {
            Log?.Invoke($"Revival requested (type {type}).");
            Dead.Hide();
        };
    }

    /// <summary>Per-frame poll of the state bar values that have no push event (MP/EXP/pos).</summary>
    public void Tick()
    {
        if (!ReferenceEquals(_context.Machine.Active, _context.InGame))
            return;

        var l = _context.InGame.World.Local;
        StateBar.UpdateMp(l.Mp, l.MaxMp);
        StateBar.UpdateExp(l.Exp, l.MaxExp);
        StateBar.UpdatePosition(l.X, l.Z);
    }

    /// <summary>Toggle the inventory window and repopulate it from the current model when opening.</summary>
    public void ToggleInventory()
    {
        if (Inventory is not { } inv)
            return;
        inv.Toggle();
        if (inv.Root.Visible)
        {
            inv.Populate(_context.InGame.Inventory);
            Manager.SetFocusedUi(inv.Root);
        }
    }

    /// <summary>Feed the live cursor to the inventory drag flow (mirrors CLocalInput MouseGetPos).</summary>
    public void SetCursor(int x, int y)
    {
        if (Inventory is { } inv)
            inv.Cursor = new UiPoint(x, y);
    }

    /// <summary>Draw the dialogs, then paint the chat / message scrollback line by line.</summary>
    public void Draw(double timeSeconds)
    {
        _screen.Draw(Manager, timeSeconds);

        bool chatLines = Chat.Root.Visible && Chat.Lines.Count > 0 && _chatOutput != null;
        bool msgLines = MessageWnd.Root.Visible && MessageWnd.Lines.Count > 0 && _msgOutput != null;
        if (!chatLines && !msgLines)
            return;

        _spriteBatch.Begin();
        if (chatLines)
            DrawScrollback(_chatOutput!.Region, Chat.Lines);
        if (msgLines)
            DrawScrollback(_msgOutput!.Region, MessageWnd.Lines);
        _spriteBatch.End();
    }

    /// <summary>
    /// The .uif scrollback slot (text0 / text_message) is a single string the original paints
    /// line by line; draw the newest lines bottom-anchored inside the region.
    /// </summary>
    private void DrawScrollback(N3UiRect region, IReadOnlyList<ChatLine> lines)
    {
        DynamicSpriteFont font = _fonts.GetUiFont(11);
        float lineHeight = font.LineHeight > 0 ? font.LineHeight : 14f;
        int height = Math.Max(region.Bottom - region.Top, (int)lineHeight);
        int maxLines = Math.Max(1, Math.Min(8, (int)(height / lineHeight)));

        int count = Math.Min(maxLines, lines.Count);
        float y = region.Bottom - lineHeight;
        for (int i = 0; i < count; i++)
        {
            ChatLine line = lines[lines.Count - 1 - i];
            _spriteBatch.DrawString(
                font, line.Text, new Vector2(region.Left, y), ColorInterop.FromArgb(line.Color));
            y -= lineHeight;
        }
    }

    /// <summary>Enter with the chat edit focused submits the line (CUIChat EDIT_RETURN).</summary>
    public void SubmitChatReturn() => Chat.SubmitInput();

    private UiControl? LoadDialog(string uifName)
    {
        if (uifName.Length == 0)
            return null;

        string? path = _resolver.Resolve(uifName);
        if (path == null)
        {
            Log?.Invoke($"UI layout not found: {uifName}");
            return null;
        }

        try
        {
            var layout = new N3UiBase();
            layout.LoadFromFile(path);
            return UiControlFactory.Build(layout);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"UI layout failed: {uifName}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _screen.Dispose();
        _spriteBatch.Dispose();
        _textures.Dispose();
    }
}
