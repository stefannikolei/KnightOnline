using System.Globalization;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the skill-tree window — port of <c>CUISkillTreeDlg</c>
/// (Client/WarFare/UISkillTreeDlg.cpp). The shipped <c>*_skilltree_*.uif</c> carries the six
/// <see cref="UiAreaType.SkillTree"/> slot regions, the paging / tab / learn buttons and the
/// per-slot string labels; the skill icons are created at runtime
/// (<c>CUISkillTreeDlg::InitIconUpdate</c>) from the <see cref="SkillTableSet"/> and placed at
/// those slot regions.
///
/// The tree is a <c>[tab][page][slot]</c> grid (5 tabs — base + 4 specializations — × 7 pages
/// × 6 slots). A skill's tab comes from <c>NeedSkill % 10</c> (0 → base, 5..8 → the four
/// specialization tabs); it is <em>usable</em> when the player meets the required level (base
/// tab) or the tab's mastery pool (specialization tabs). Learning a point sends
/// WIZ_SKILLPT_CHANGE, optimistically bumps the pool and, for a specialization tab, re-runs the
/// population so newly unlocked skills light up. Pure/headless — only the icon textures are
/// strings the device renderer resolves.
/// </summary>
public sealed class SkillTreeDialog
{
    /// <summary>MAX_SKILL_KIND_OF — base tab + 4 specialization tabs.</summary>
    public const int TabCount = 5;

    /// <summary>MAX_SKILL_PAGE_NUM — pages per tab.</summary>
    public const int PageCount = 7;

    /// <summary>MAX_SKILL_IN_PAGE — slots per page.</summary>
    public const int SlotCount = 6;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly SkillTableSet _skills;
    private readonly IconDragState _drag;

    private readonly UiButton? _btnClose;
    private readonly UiButton? _btnLeft;
    private readonly UiButton? _btnRight;
    private readonly UiButton?[] _btnLearn = new UiButton?[8];         // btn_0..btn_7 → PointPushUpButton 1..8
    private readonly Dictionary<UiControl, int> _tabButtons = new();   // btn_public + spec tab buttons → tab index
    private readonly UiAreaControl?[] _slotArea = new UiAreaControl?[SlotCount];

    private readonly UiStringControl? _strSkillPoint;
    private readonly UiStringControl?[] _strPool = new UiStringControl?[7];      // string_0..6 → Skills[1..7]
    private readonly UiStringControl?[] _strList = new UiStringControl?[SlotCount]; // string_list_0..5
    private readonly UiStringControl? _strPage;

    private readonly SkillPlacement?[,,] _tree = new SkillPlacement?[TabCount, PageCount, SlotCount];

    private LocalPlayer? _local;
    private int _curTab;
    private int _curPage;

    /// <summary>A placed skill: its grid position, learn state, row data and runtime icon.</summary>
    public sealed record SkillPlacement(int Tab, int Page, int Slot, bool Usable, SkillRow Skill, UiIconControl Icon);

    public SkillTreeDialog(GameContext context, UiControl root, SkillTableSet skills, IconDragState drag)
    {
        _context = context;
        _root = root;
        _skills = skills;
        _drag = drag;
        _local = context.InGame.World.Local;

        _btnClose = root.GetChildById<UiButton>("btn_close");
        _btnLeft = root.GetChildById<UiButton>("btn_left");
        _btnRight = root.GetChildById<UiButton>("btn_right");
        for (int i = 0; i < 8; i++)
            _btnLearn[i] = root.GetChildById<UiButton>("btn_" + i.ToString(CultureInfo.InvariantCulture));

        ResolveTabButtons();

        for (int i = 0; i < SlotCount; i++)
            _slotArea[i] = root.GetChildAreaByOrder(UiAreaType.SkillTree, i);

        _strSkillPoint = root.GetChildById<UiStringControl>("string_skillpoint");
        for (int i = 0; i < _strPool.Length; i++)
            _strPool[i] = root.GetChildById<UiStringControl>("string_" + i.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < SlotCount; i++)
            _strList[i] = root.GetChildById<UiStringControl>("string_list_" + i.ToString(CultureInfo.InvariantCulture));
        _strPage = root.GetChildById<UiStringControl>("string_page");

        root.Message += OnMessage;
        root.SetVisible(false); // slides in on toggle (btn_skill / hotkey)
    }

    /// <summary>The runtime dialog root (registered with the UI manager).</summary>
    public UiControl Root => _root;

    /// <summary>The skill table the tree is built from (the hotkey slice validates against it).</summary>
    public SkillTableSet Skills => _skills;

    /// <summary>The tab currently shown (m_iCurKindOf, 0 = base).</summary>
    public int CurrentTab => _curTab;

    /// <summary>The page currently shown within the tab (m_iCurSkillPage).</summary>
    public int CurrentPage => _curPage;

    private void ResolveTabButtons()
    {
        void Add(string id, int tab)
        {
            if (_root.GetChildById<UiButton>(id) is { } b)
                _tabButtons[b] = tab;
        }

        Add("btn_public", 0);
        // First specialization tab (SetPageInIconRegion(1, 0)).
        foreach (string id in new[] { "btn_ranger0", "btn_blade0", "btn_mage0", "btn_cleric0", "btn_hunter0", "btn_berserker0", "btn_sorcerer0", "btn_shaman0" })
            Add(id, 1);
        // Second tab.
        foreach (string id in new[] { "btn_ranger1", "btn_blade1", "btn_mage1", "btn_cleric1", "btn_hunter1", "btn_berserker1", "btn_sorcerer1", "btn_shaman1" })
            Add(id, 2);
        // Third tab.
        foreach (string id in new[] { "btn_ranger2", "btn_blade2", "btn_mage2", "btn_cleric2", "btn_hunter2", "btn_berserker2", "btn_sorcerer2", "btn_shaman2" })
            Add(id, 3);
        // Master tab.
        Add("btn_master", 4);
    }

    /// <summary>
    /// Wire the in-world hook: rebuild the tree whenever the full MyInfo block lands. Additive
    /// (<c>+=</c>) so it does not clobber the state bar / inventory MyInfo hooks.
    /// </summary>
    public void Bind(InGameState inGame)
    {
        _local = inGame.World.Local;
        inGame.MyInfoReceived += _ => Rebuild();
    }

    /// <summary>Rebuild the whole tree from the current player class/level/skill pools.</summary>
    public void Rebuild()
    {
        _local = _context.InGame.World.Local;
        InitIconUpdate();
    }

    // ---- Population (CUISkillTreeDlg::InitIconUpdate) -----------------------

    private void InitIconUpdate()
    {
        ClearTree();

        if (_local is not { } player)
        {
            PageButtonInitialize();
            return;
        }

        int cls = player.Class;
        uint blockLow = (uint)(cls * 1000 + 1);
        uint blockHigh = (uint)((cls + 1) * 1000);

        foreach (SkillRow skill in _skills.All)
        {
            if (skill.Id < blockLow || skill.Id >= blockHigh)
                continue; // id / 1000 != class
            if (skill.Id >= SkillTableSet.UsableItemIdMin)
                continue; // usable-item, not a class skill

            int modulo = skill.NeedSkill % 10;
            int tab = modulo switch
            {
                0 => 0, // base
                5 => 1,
                6 => 2,
                7 => 3,
                8 => 4, // master
                _ => -1,
            };
            if (tab < 0)
                continue;

            bool usable = tab == 0
                ? skill.NeedLevel <= player.Level
                : skill.NeedLevel <= player.TabMastery(tab);

            AddSkillToPage(skill, tab, usable);
        }

        PageButtonInitialize();
    }

    private void ClearTree()
    {
        for (int t = 0; t < TabCount; t++)
            for (int p = 0; p < PageCount; p++)
                for (int s = 0; s < SlotCount; s++)
                {
                    if (_tree[t, p, s] is { } placed)
                    {
                        _root.RemoveChild(placed.Icon);
                        _tree[t, p, s] = null;
                    }
                }
    }

    private void AddSkillToPage(SkillRow skill, int tab, bool usable)
    {
        // Skip if the same id is already placed in this tab (dedupe by id).
        for (int p = 0; p < PageCount; p++)
            for (int s = 0; s < SlotCount; s++)
                if (_tree[tab, p, s]?.Skill.Id == skill.Id)
                    return;

        // First free [page][slot] in the tab.
        for (int p = 0; p < PageCount; p++)
            for (int s = 0; s < SlotCount; s++)
            {
                if (_tree[tab, p, s] != null)
                    continue;

                N3UiRect region = _slotArea[s]?.Region ?? default;
                UiIconControl icon = UiIconControl.CreateRuntime(region, (int)skill.Id);
                icon.DragState = _drag;
                icon.IconTexture = usable ? SkillIconFileName(skill.Id) : EnigmaIcon;
                icon.SkillDisabled = !usable;
                icon.Payload = skill;
                icon.SetVisible(false);
                _root.AddChild(icon);

                _tree[tab, p, s] = new SkillPlacement(tab, p, s, usable, skill, icon);
                return;
            }
    }

    private const string EnigmaIcon = @"UI\skillicon_enigma.dxt";

    /// <summary>CUISkillTreeDlg::AddSkillToPage icon name — <c>UI\skillicon_{id%100:00}_{id/100}.dxt</c>.</summary>
    public static string SkillIconFileName(uint id) =>
        string.Format(CultureInfo.InvariantCulture, @"UI\skillicon_{0:00}_{1}.dxt", id % 100, id / 100);

    private void PageButtonInitialize()
    {
        SetPageInIconRegion(0, 0);

        // string_skillpoint = unspent; string_0..6 = Skills[1..7] (Skills[8]/master pool undisplayed).
        if (_local is { } player)
        {
            SetString(_strSkillPoint, player.Skills[0]);
            for (int i = 0; i < _strPool.Length; i++)
                SetString(_strPool[i], player.Skills[i + 1]);
        }
    }

    // ---- Paging / tab (SetPageInIconRegion) --------------------------------

    /// <summary>Show tab <paramref name="tab"/> page <paramref name="page"/>; hide the rest and refresh labels.</summary>
    public void SetPageInIconRegion(int tab, int page)
    {
        if (tab is < 0 or >= TabCount || page is < 0 or >= PageCount)
            return;

        _curTab = tab;
        _curPage = page;

        for (int t = 0; t < TabCount; t++)
            for (int p = 0; p < PageCount; p++)
                for (int s = 0; s < SlotCount; s++)
                    _tree[t, p, s]?.Icon.SetVisible(t == tab && p == page);

        for (int s = 0; s < SlotCount; s++)
        {
            if (_tree[tab, page, s] is { } placed)
            {
                if (_strList[s] != null)
                {
                    _strList[s]!.Text = placed.Skill.Name;
                    _strList[s]!.SetVisible(true);
                }
            }
            else
            {
                _strList[s]?.SetVisible(false);
            }
        }

        SetString(_strPage, page + 1);
    }

    /// <summary>CUISkillTreeDlg::PageLeft.</summary>
    public void PageLeft()
    {
        if (_curPage > 0)
            SetPageInIconRegion(_curTab, _curPage - 1);
    }

    /// <summary>CUISkillTreeDlg::PageRight.</summary>
    public void PageRight()
    {
        if (_curPage < PageCount - 1)
            SetPageInIconRegion(_curTab, _curPage + 1);
    }

    // ---- Learning a point (PointPushUpButton) ------------------------------

    /// <summary>
    /// Port of <c>CUISkillTreeDlg::PointPushUpButton(iValue)</c> (iValue 1..8). Returns true when
    /// a point was actually spent (packet sent + pools bumped); false for any rejection. The C++
    /// rejects: no unspent points; the base pools 1..4 (not up-pushable in this client); the
    /// specialization tabs 5..8 while still a base class; the master tab 8 unless the class is a
    /// master (2nd promotion); and a pool that already reached the player's level.
    /// </summary>
    public bool Learn(int iValue)
    {
        if (_local is not { } player)
            return false;
        if (iValue is < 1 or > 8)
            return false;

        // No unspent points.
        if (player.Skills[0] == 0)
            return false;

        // Base pools 1..4 are not up-pushable here.
        if (iValue is >= 1 and <= 4)
            return false;

        int cls = player.Class;

        // Specialization tabs require a promoted class.
        if (ClassChangeProtocol.IsBaseClass((short)cls))
            return false;

        // Master tab (8) requires a master (2nd-promotion) class.
        if (iValue == 8 && !IsMasterClass(cls))
            return false;

        // Cannot raise a pool at/above the player's own level.
        if (player.Skills[iValue] >= player.Level)
            return false;

        _context.InGame.SendSkillPoint((byte)iValue);

        // Optimistic update: spend a point, bump the tab pool.
        player.Skills[0]--;
        player.Skills[iValue]++;

        int tabBackup = _curTab;
        int pageBackup = _curPage;

        // Specialization tabs may unlock skills → re-run the population.
        if (iValue is >= 5 and <= 8)
            InitIconUpdate();

        SetString(_strSkillPoint, player.Skills[0]);
        if (iValue - 1 < _strPool.Length)
            SetString(_strPool[iValue - 1], player.Skills[iValue]);

        SetPageInIconRegion(tabBackup, pageBackup);
        return true;
    }

    /// <summary>The eight master (2nd-promotion) classes carrying a master skill tab.</summary>
    private static bool IsMasterClass(int cls) =>
        cls is 106 or 108 or 110 or 112 or 206 or 208 or 210 or 212;

    // ---- Query (HasIDSkill) ------------------------------------------------

    /// <summary>
    /// True when the given skill id is placed in the tree (port of <c>HasIDSkill</c>). The
    /// hotkey slice validates dropped skills against this.
    /// </summary>
    public bool HasSkill(uint id)
    {
        for (int t = 0; t < TabCount; t++)
            for (int p = 0; p < PageCount; p++)
                for (int s = 0; s < SlotCount; s++)
                    if (_tree[t, p, s]?.Skill.Id == id)
                        return true;
        return false;
    }

    /// <summary>The placement of a skill id in the tree, or null.</summary>
    public SkillPlacement? FindPlacement(uint id)
    {
        for (int t = 0; t < TabCount; t++)
            for (int p = 0; p < PageCount; p++)
                for (int s = 0; s < SlotCount; s++)
                    if (_tree[t, p, s] is { } placed && placed.Skill.Id == id)
                        return placed;
        return null;
    }

    // ---- Message routing ---------------------------------------------------

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        if (ReferenceEquals(sender, _btnClose))
        {
            Hide();
            return;
        }

        if (ReferenceEquals(sender, _btnLeft))
        {
            PageLeft();
            return;
        }

        if (ReferenceEquals(sender, _btnRight))
        {
            PageRight();
            return;
        }

        for (int i = 0; i < 8; i++)
            if (ReferenceEquals(sender, _btnLearn[i]))
            {
                Learn(i + 1);
                return;
            }

        if (_tabButtons.TryGetValue(sender, out int tab))
            SetPageInIconRegion(tab, 0);
    }

    private static void SetString(UiStringControl? s, int value)
    {
        if (s != null)
            s.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    // ---- Show / hide -------------------------------------------------------

    public void Show() => _root.SetVisible(true);

    public void Hide() => _root.SetVisible(false);

    public void Toggle() => _root.SetVisible(!_root.Visible);
}
