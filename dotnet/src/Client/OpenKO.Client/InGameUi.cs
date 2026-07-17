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

    /// <summary>The dropped-item (loot box) dialog — null when the layout/tables failed to load.</summary>
    public DroppedItemDialog? DroppedItem { get; }

    /// <summary>The item image-tooltip — null when the layout failed to load.</summary>
    public ItemTooltipControl? ItemTooltip { get; }

    /// <summary>The repair tooltip — null when the layout failed to load.</summary>
    public RepairTooltipControl? RepairTooltip { get; }

    /// <summary>The countable stack-split popup (base_tradeedit) — null when the layout failed to load.</summary>
    public CountableItemEditDialog? CountableItemEdit { get; }

    /// <summary>The skill-tree window — null when the layout or skill table failed to load.</summary>
    public SkillTreeDialog? SkillTree { get; }

    /// <summary>The class-change (promotion) dialog — null when the layout failed to load.</summary>
    public ClassChangeDialog? ClassChange { get; }

    /// <summary>The icon hotkey bar — null when the layout or skill table failed to load.</summary>
    public HotKeyDialog? HotKey { get; }

    /// <summary>The multi-page character sheet (status + clan) — null when the layout failed to load.</summary>
    public VariousDialog? Various { get; }

    /// <summary>The party/force member window — null when the layout failed to load.</summary>
    public PartyOrForceDialog? PartyOrForce { get; }

    /// <summary>The clan browse/create/join window — null when the layout failed to load.</summary>
    public KnightsOperationDialog? KnightsOperation { get; }

    /// <summary>The clan-name entry popup — null when the layout failed to load.</summary>
    public CreateClanDialog? CreateClan { get; }

    /// <summary>The in-game shared message box (party/clan confirms) — null when the layout failed to load.</summary>
    public MessageBoxDialog? MessageBox { get; }

    /// <summary>The bank/warehouse window — null when the layout or item tables failed to load.</summary>
    public WareHouseDialog? WareHouse { get; }

    /// <summary>The NPC/object teleport menu — null when the layout failed to load.</summary>
    public WarpDialog? Warp { get; }

    /// <summary>The inn-keeper NPC menu — null when the layout failed to load.</summary>
    public InnDialog? Inn { get; }

    /// <summary>The anvil upgrade-select window — null when the layout failed to load.</summary>
    public UpgradeDialog? Upgrade { get; }

    private short? _targetId;

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

        // Dropped-item loot box — needs the item tables; hidden until a loot list arrives.
        UiControl? dropRoot = LoadDialog(table.DroppedItem(nation));
        if (dropRoot != null && items != null)
        {
            DroppedItem = new DroppedItemDialog(context, dropRoot, items, Manager.IconDrag);
            Manager.BindIconDragState(dropRoot);
        }
        else if (dropRoot == null)
        {
            Log?.Invoke("DroppedItem layout not found: " + table.DroppedItem(nation));
        }

        // Item / repair image-tooltips — passive, hidden by default.
        UiControl? infoRoot = LoadDialog(table.ItemInfo(nation));
        if (infoRoot != null)
            ItemTooltip = new ItemTooltipControl(infoRoot);
        else
            Log?.Invoke("ItemInfo tooltip layout not found: " + table.ItemInfo(nation));

        UiControl? repairRoot = LoadDialog(table.RepairTooltip(nation));
        if (repairRoot != null)
            RepairTooltip = new RepairTooltipControl(repairRoot);
        else
            Log?.Invoke("RepairTooltip layout not found: " + table.RepairTooltip(nation));

        // Countable stack-split popup (base_tradeedit) — the reusable modal quantity editor.
        UiControl? editRoot = LoadDialog(table.CountableItemEdit(nation));
        if (editRoot != null)
        {
            editRoot.SetPosCenter(w, h);
            CountableItemEdit = new CountableItemEditDialog(Manager, editRoot);
        }
        else
        {
            Log?.Invoke("CountableItemEdit layout not found: " + table.CountableItemEdit(nation));
        }

        // Skill tree — needs the skill table; hidden until btn_skill toggles it.
        UiControl? skillRoot = LoadDialog(table.SkillTree(nation));
        SkillTableSet? skills = TryLoadSkills(_resolver);
        if (skillRoot != null && skills != null)
        {
            skillRoot.SetPos(w - skillRoot.Width, 10); // slides in from the right edge
            SkillTree = new SkillTreeDialog(context, skillRoot, skills, Manager.IconDrag);
            Manager.BindIconDragState(skillRoot);
        }
        else if (skillRoot == null)
        {
            Log?.Invoke("SkillTree layout not found: " + table.SkillTree(nation));
        }
        else
        {
            Log?.Invoke("SkillTree skill table not found; skill tree disabled.");
        }

        // Class-change dialog — driven by the server reply; hidden by default.
        UiControl? classRoot = LoadDialog(table.ClassChange(nation));
        if (classRoot != null)
        {
            classRoot.SetPosCenter(w, h);
            ClassChange = new ClassChangeDialog(context, classRoot) { SkillTree = SkillTree };
        }
        else
        {
            Log?.Invoke("ClassChange layout not found: " + table.ClassChange(nation));
        }

        // Hotkey bar — always visible in-game, anchored bottom-centre above the command bar
        // (CGameProcMain). Needs the skill table; persists to a per-character file.
        UiControl? hotkeyRoot = LoadDialog(table.HotKey(nation));
        if (hotkeyRoot != null && skills != null)
        {
            hotkeyRoot.SetPos((w - hotkeyRoot.Width) / 2, h - cmdRoot.Height - hotkeyRoot.Height);
            IHotkeyStore store = new FileHotkeyStore(context.Account, ResolveCharacterName(context));
            HotKey = new HotKeyDialog(context, hotkeyRoot, skills, SkillTree, Manager.IconDrag, store);
            Manager.BindIconDragState(hotkeyRoot);
        }
        else if (hotkeyRoot == null)
        {
            Log?.Invoke("HotKey layout not found: " + table.HotKey(nation));
        }
        else
        {
            Log?.Invoke("HotKey skill table not found; hotkey bar disabled.");
        }

        // Character sheet (Various) — the szState + szKnights pages load into the szVarious frame
        // (CGameProcMain::InitUI); adding them as children lets one controller resolve both.
        UiControl? variousRoot = LoadDialog(table.Various(nation));
        UiControl? variousStateRoot = LoadDialog(table.State(nation));
        UiControl? variousClanRoot = LoadDialog(table.Knights(nation));
        if (variousRoot != null)
        {
            if (variousStateRoot != null)
                variousRoot.AddChild(variousStateRoot);
            if (variousClanRoot != null)
                variousRoot.AddChild(variousClanRoot);
            variousRoot.SetPos(0, 80); // slides in from the left (CUIVarious)
            Various = new VariousDialog(context, variousRoot, variousStateRoot, variousClanRoot);
        }
        else
        {
            Log?.Invoke("Various layout not found: " + table.Various(nation));
        }

        // Party/force member window — right side, auto-shows when a party forms.
        UiControl? partyRoot = LoadDialog(table.PartyOrForce(nation));
        if (partyRoot != null)
        {
            partyRoot.SetPos(w - partyRoot.Width, 100);
            PartyOrForce = new PartyOrForceDialog(context, partyRoot);
        }
        else
        {
            Log?.Invoke("PartyOrForce layout not found: " + table.PartyOrForce(nation));
        }

        // In-game shared message box for party/clan confirms (its own instance).
        UiControl? msgBoxRoot = LoadDialog(table.MessageBox(nation));
        if (msgBoxRoot != null)
        {
            msgBoxRoot.SetPosCenter(w, h);
            MessageBox = new MessageBoxDialog(msgBoxRoot);
        }

        // Clan browse/create/join window — centred, hidden until opened.
        UiControl? knightsOpRoot = LoadDialog(table.KnightsOperation(nation));
        if (knightsOpRoot != null)
        {
            knightsOpRoot.SetPosCenter(w, h);
            KnightsOperation = new KnightsOperationDialog(context, knightsOpRoot, MessageBox);
        }
        else
        {
            Log?.Invoke("KnightsOperation layout not found: " + table.KnightsOperation(nation));
        }

        // Clan-name entry popup — centred, hidden until Btn_Create.
        UiControl? createClanRoot = LoadDialog(table.InputClanName(nation));
        if (createClanRoot != null)
        {
            createClanRoot.SetPosCenter(w, h);
            CreateClan = new CreateClanDialog(context, createClanRoot);
        }

        // Warehouse (bank) window — centred, needs the item tables; pushed open on the server reply.
        UiControl? wareRoot = LoadDialog(table.WareHouse(nation));
        if (wareRoot != null && items != null)
        {
            wareRoot.SetPosCenter(w, h);
            WareHouse = new WareHouseDialog(context, wareRoot, items, Manager.IconDrag, CountableItemEdit);
            Manager.BindIconDragState(wareRoot);
        }
        else if (wareRoot == null)
        {
            Log?.Invoke("WareHouse layout not found: " + table.WareHouse(nation));
        }

        // Warp / teleport menu — centred, pushed open on the WIZ_WARP_LIST reply.
        UiControl? warpRoot = LoadDialog(table.ZoneChangeOrWarp(nation));
        if (warpRoot != null)
        {
            warpRoot.SetPosCenter(w, h);
            Warp = new WarpDialog(context, warpRoot);
        }
        else
        {
            Log?.Invoke("Warp layout not found: " + table.ZoneChangeOrWarp(nation));
        }

        // Inn-keeper NPC menu — centred, pushed open on the N3_SP_WARE_INN reply.
        UiControl? innRoot = LoadDialog(table.Inn(nation));
        if (innRoot != null)
        {
            innRoot.SetPosCenter(w, h);
            Inn = new InnDialog(context, innRoot);
        }

        // Anvil upgrade-select — centred, pushed open on the ITEM_UPGRADE_REQ reply.
        UiControl? upgradeRoot = LoadDialog(table.UpgradeSelect(nation));
        if (upgradeRoot != null)
        {
            upgradeRoot.SetPosCenter(w, h);
            Upgrade = new UpgradeDialog(context, upgradeRoot);
        }

        // Register with the manager (last added = topmost = input first). Chat sits above
        // the passive bars so its edit/buttons grab input; the death dialog floats on top.
        Manager.Add(stateRoot);
        Manager.Add(targetRoot);
        Manager.Add(cmdRoot);
        Manager.Add(msgRoot);
        Manager.Add(chatRoot);
        Manager.Add(deadRoot);

        // Loot box above the inventory; the tooltips and the modal edit float on top.
        if (dropRoot != null && items != null)
            Manager.Add(dropRoot);
        if (infoRoot != null)
            Manager.Add(infoRoot);
        if (repairRoot != null)
            Manager.Add(repairRoot);
        if (editRoot != null)
            Manager.Add(editRoot);

        // Skill tree + class change float above the HUD.
        if (skillRoot != null && skills != null)
            Manager.Add(skillRoot);
        if (classRoot != null)
            Manager.Add(classRoot);
        // Hotkey bar sits with the HUD (always visible), registered so its icons receive input.
        if (hotkeyRoot != null && skills != null)
            Manager.Add(hotkeyRoot);

        // Party window sits with the HUD; the character sheet / clan windows float above it,
        // the clan-name popup and the message box are topmost (modal-ish confirms).
        if (partyRoot != null)
            Manager.Add(partyRoot);
        if (variousRoot != null)
            Manager.Add(variousRoot);
        if (knightsOpRoot != null)
            Manager.Add(knightsOpRoot);
        if (createClanRoot != null)
            Manager.Add(createClanRoot);

        // Solo NPC/object interaction windows (warehouse/warp/inn/upgrade) float above the HUD;
        // the message box stays topmost (modal-ish confirms).
        if (wareRoot != null && items != null)
            Manager.Add(wareRoot);
        if (warpRoot != null)
            Manager.Add(warpRoot);
        if (innRoot != null)
            Manager.Add(innRoot);
        if (upgradeRoot != null)
            Manager.Add(upgradeRoot);

        if (msgBoxRoot != null)
            Manager.Add(msgBoxRoot);
    }

    /// <summary>The selected character's name (the hotkey store key), or a stable fallback.</summary>
    private static string ResolveCharacterName(GameContext context)
    {
        int i = context.SelectedCharIndex;
        if (i >= 0 && i < context.Characters.Count && context.Characters[i].CharId.Length > 0)
            return context.Characters[i].CharId;
        return context.InGame.World.Local.Name;
    }

    /// <summary>
    /// Load the skill table (Data\skill_magic_main_us.tbl), mirroring <see cref="TryLoadItems"/>.
    /// Returns null when the table is missing/unreadable.
    /// </summary>
    private static SkillTableSet? TryLoadSkills(KoPathResolver resolver, string lang = "us")
    {
        string? path = resolver.Resolve($"Data\\skill_magic_main_{lang}.tbl");
        if (path == null)
            return null;

        try
        {
            return SkillTableSet.LoadFromFile(path);
        }
        catch (Exception)
        {
            return null;
        }
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

        // Loot box: a bundle-open reply shows the dialog; a WIZ_ITEM_GET reply routes the pickup
        // into the inventory and refreshes the inventory dialog through its own populate path.
        if (DroppedItem is { } drop)
        {
            inGame.LootListReceived += bundle => drop.Populate(bundle.BundleId, bundle.Items);
            inGame.ItemGetReceived += drop.OnGetResult;
            drop.InventoryChanged += () => Inventory?.Populate(inGame.Inventory);
        }

        // Fold buttons toggle their window's visibility (CUIChat/CUIMessageWnd btn_off).
        Chat.FoldRequested += () => Chat.Root.SetVisible(!Chat.Root.Visible);
        MessageWnd.FoldRequested += () => MessageWnd.Root.SetVisible(!MessageWnd.Root.Visible);

        // Character sheet + clan/party windows (9.7).
        Various?.Bind(inGame);
        PartyOrForce?.Bind(inGame);
        KnightsOperation?.Bind(inGame);

        // Solo NPC/object interactions (9.8a): warehouse open reply, warp list, inn menu, upgrade req.
        WareHouse?.Bind(inGame);
        Warp?.Bind(inGame);
        Inn?.Bind(inGame);
        Upgrade?.Bind(inGame);

        // Warehouse deposits/withdraws refresh both the inventory dialog and the state-bar gold view
        // once the server confirms; the inventory model is shared, so a re-populate is enough.
        if (WareHouse is { } ware)
        {
            inGame.WarehouseReceived += (sub, _) =>
            {
                if (sub is OpenKO.Client.Game.Net.WarehouseProtocol.Input or OpenKO.Client.Game.Net.WarehouseProtocol.Output)
                    Inventory?.Populate(inGame.Inventory);
            };
        }

        // Inn buttons: btn_makeclan opens the found-clan flow; the trade-sell BBS is deferred.
        if (Inn is { } inn)
        {
            inn.FoundClanRequested += () => CreateClan?.Open();
            inn.SellBoardRequested += () => Log?.Invoke("Trade-sell BBS (CUITradeBBSSelector) is deferred.");
        }

        // Upgrade select: the item/ring upgrade anvil dialogs are deferred (C++ stubs).
        if (Upgrade is { } upgrade)
        {
            upgrade.ItemUpgradeRequested += npc => Log?.Invoke($"Item upgrade (npc {npc}) deferred: CUIItemUpgrade not implemented.");
            upgrade.RingUpgradeRequested += npc => Log?.Invoke($"Ring upgrade (npc {npc}) deferred: CUIRingUpgrade not implemented.");
        }

        // Character sheet's Party tab opens the party window; its clan-page invite target and the
        // command bar's party actions share the current combat target.
        if (Various is { } various)
            various.PartyPageRequested += () => PartyOrForce?.Root.SetVisible(PartyOrForce.MemberCount > 0);

        // Clan-name popup: Btn_Create in the operation window opens it; on confirm the cost box
        // gates the actual WIZ_KNIGHTS_PROCESS create (CUICreateClanName::MakeClan → MB_YESNO).
        if (KnightsOperation is { } knightsOp && CreateClan is { } createClan)
            knightsOp.CreateRequested += () => createClan.Open();
        if (CreateClan is { } cc2)
        {
            cc2.ConfirmRequested += name =>
            {
                if (MessageBox is { } box)
                    box.Show($"Found the clan \"{name}\"?", string.Empty, MessageBoxStyle.YesNo, r =>
                    {
                        if (r == MessageBoxResult.Yes)
                            cc2.Send();
                    });
                else
                    cc2.Send();
            };
        }

        // Command buttons / revival: logged for now; real behaviour lands in later slices.
        CmdBar.Command += id => Log?.Invoke($"Command: {id}");
        CmdBar.Command += id =>
        {
            if (id == "btn_inventory")
                ToggleInventory();
            else if (id == "btn_skill")
                ToggleSkillTree();
            else if (id == "btn_character")
                ToggleVarious();
            else if (id == "btn_invite")
                InviteTargetToParty();
            else if (id == "btn_disband")
                PartyOrForce?.Leave(_targetId ?? -1);
        };

        // Skill tree: rebuild from MyInfo (additive; doesn't clobber the state bar / inventory hooks).
        SkillTree?.Bind(inGame);

        // Hotkey bar: load persisted hotkeys on MyInfo and feed the game clock for drag-casts.
        HotKey?.Bind(inGame);

        // Class change: the server reply drives the dialog; a promotion rebuilds the skill tree.
        if (ClassChange is { } cc)
        {
            inGame.ClassChangeResult += cc.Open;
            // A promotion invalidates the old class's skills → flush the hotkey bar.
            if (HotKey is { } hk)
                cc.ClassChanged += hk.FlushAll;
        }
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

    /// <summary>Toggle the skill-tree window and rebuild it from the current player state when opening.</summary>
    public void ToggleSkillTree()
    {
        if (SkillTree is not { } tree)
            return;
        tree.Toggle();
        if (tree.Root.Visible)
        {
            tree.Rebuild();
            Manager.SetFocusedUi(tree.Root);
        }
    }

    /// <summary>Feed the live cursor to the inventory drag flow (mirrors CLocalInput MouseGetPos).</summary>
    public void SetCursor(int x, int y)
    {
        if (Inventory is { } inv)
            inv.Cursor = new UiPoint(x, y);
        if (DroppedItem is { } drop)
            drop.Cursor = new UiPoint(x, y);
        if (HotKey is { } hk)
            hk.Cursor = new UiPoint(x, y);
        if (WareHouse is { } ware)
            ware.Cursor = new UiPoint(x, y);
        UpdateItemTooltip(x, y);
    }

    /// <summary>Feed the current combat target id to the hotkey cast path (-1 = none).</summary>
    public void SetTarget(short? targetId)
    {
        _targetId = targetId;
        if (HotKey is { } hk)
            hk.TargetId = targetId ?? -1;
        if (Various is { } various)
            various.TargetId = targetId ?? -1;
    }

    /// <summary>Toggle the character sheet (Various), refreshing its status page from MyInfo when opening.</summary>
    public void ToggleVarious()
    {
        if (Various is not { } various)
            return;
        various.Toggle();
        if (various.Root.Visible)
            Manager.SetFocusedUi(various.Root);
    }

    /// <summary>Toggle the clan browse/create/join window, requesting the clan list when opening.</summary>
    public void ToggleKnightsOperation()
    {
        if (KnightsOperation is not { } knightsOp)
            return;
        knightsOp.Toggle();
        if (knightsOp.Root.Visible)
            Manager.SetFocusedUi(knightsOp.Root);
    }

    /// <summary>
    /// CGameProcMain::MsgSend_PartyOrForceCreate — invite the current target into the party by
    /// name (CREATE when solo, INSERT once a party of 2+ exists). No-op without a player target.
    /// </summary>
    public void InviteTargetToParty()
    {
        if (_targetId is not short id || !_context.InGame.World.Players.TryGetValue(id, out RemotePlayer? player))
            return;
        bool haveParty = PartyOrForce is { MemberCount: >= 2 };
        _context.InGame.SendParty(haveParty
            ? OpenKO.Client.Game.Net.PartyProtocol.BuildInvite(player.Name)
            : OpenKO.Client.Game.Net.PartyProtocol.BuildCreate(player.Name));
    }

    /// <summary>
    /// Route a number key (1-8, zero-based slot) to the hotkey bar's cast pipeline at the given game
    /// clock. Called by the executable when no chat edit is focused.
    /// </summary>
    public void TriggerHotkey(int slot, double gameSeconds) => HotKey?.TriggerSlot(slot, gameSeconds);

    /// <summary>
    /// Hover tooltip: while the inventory is open, show the item image-tooltip for the icon under
    /// the cursor (CUIInventory highlight → CUIImageTooltipDlg::DisplayTooltipsEnable), else hide.
    /// </summary>
    private void UpdateItemTooltip(int x, int y)
    {
        if (ItemTooltip is not { } tip || Inventory is not { } inv)
            return;

        if (inv.HoveredItem(new UiPoint(x, y)) is { } item && item.Basic != null && item.Ext != null)
        {
            LocalPlayer l = _context.InGame.World.Local;
            var player = new TooltipPlayer(
                l.Race, l.Level, l.Rank, l.Title,
                l.Str + l.ItemStr, l.Sta + l.ItemSta, l.Dex + l.ItemDex,
                l.Intel + l.ItemIntel, l.Cha + l.ItemCha, l.Gold);
            tip.Show(item.Basic, item.Ext, item.Durability, item.Count, x, y, player);
        }
        else
        {
            tip.Hide();
        }
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
