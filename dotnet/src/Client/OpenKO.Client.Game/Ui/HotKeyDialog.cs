using System.Globalization;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the icon hotkey bar — port of <c>CUIHotKeyDlg</c>
/// (Client/WarFare/UIHotKeyDlg.cpp). The shipped <c>*_hotkey_*.uif</c> carries the eight
/// <see cref="UiAreaType.SkillHotkey"/> slot regions, the <c>btn_up</c>/<c>btn_down</c> page
/// buttons and the per-slot count strings (ids <c>"0".."7"</c>) / tooltip strings
/// (ids <c>"10".."17"</c>); the skill icons are created at runtime and placed at those slot
/// regions (<c>CUIHotKeyDlg::InitIconUpdate</c>).
///
/// The bar is an <c>8×8</c> [page][slot] grid (<see cref="PageCount"/> pages of
/// <see cref="SlotCount"/> keys 1-8). Skills come from the skill tree (learned class skills) or
/// usable-item skills (id ≥ <see cref="SkillTableSet.UsableItemIdMin"/>). Dropping a slot on
/// itself casts it at the current target; number keys 1-8 do the same via
/// <see cref="TriggerSlot"/>. The layout persists to an <see cref="IHotkeyStore"/> on every change
/// (the registry replacement) and reloads on open, validating each entry. Pure/headless — the cast
/// gate lives in <see cref="MagicCastManager"/>, invoked through <see cref="InGameState.CastSkill"/>.
/// </summary>
public sealed class HotKeyDialog
{
    /// <summary>MAX_SKILL_HOTKEY_PAGE.</summary>
    public const int PageCount = 8;

    /// <summary>MAX_SKILL_IN_HOTKEY — number keys 1-8.</summary>
    public const int SlotCount = 8;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly SkillTableSet _skills;
    private readonly SkillTreeDialog? _tree;
    private readonly IconDragState _drag;
    private readonly IHotkeyStore _store;

    private readonly UiAreaControl?[] _slotArea = new UiAreaControl?[SlotCount];
    private readonly UiStringControl?[] _countStr = new UiStringControl?[SlotCount];   // id "0".."7"
    private readonly UiStringControl?[] _tooltipStr = new UiStringControl?[SlotCount]; // id "10".."17"
    private readonly UiButton? _btnUp;
    private readonly UiButton? _btnDown;

    private readonly uint[,] _skillId = new uint[PageCount, SlotCount];
    private readonly UiIconControl?[,] _icon = new UiIconControl?[PageCount, SlotCount];

    private Inventory _inv;
    private InGameState? _inGame;
    private int _curPage;
    private int _dragSrcOrder = -1;
    private bool _loaded;

    public HotKeyDialog(
        GameContext context,
        UiControl root,
        SkillTableSet skills,
        SkillTreeDialog? tree,
        IconDragState drag,
        IHotkeyStore store)
    {
        _context = context;
        _root = root;
        _skills = skills;
        _tree = tree;
        _drag = drag;
        _store = store;
        _inv = context.InGame.Inventory;

        for (int i = 0; i < SlotCount; i++)
        {
            _slotArea[i] = root.GetChildAreaByOrder(UiAreaType.SkillHotkey, i);
            _countStr[i] = root.GetChildById<UiStringControl>(i.ToString(CultureInfo.InvariantCulture));
            _tooltipStr[i] = root.GetChildById<UiStringControl>((i + 10).ToString(CultureInfo.InvariantCulture));
        }

        _btnUp = root.GetChildById<UiButton>("btn_up");
        _btnDown = root.GetChildById<UiButton>("btn_down");

        root.Message += OnMessage;
        root.SetVisible(true); // the hotkey bar is always visible in-game (no toggle)
    }

    /// <summary>The runtime dialog root (registered with the UI manager).</summary>
    public UiControl Root => _root;

    /// <summary>The current page (m_iCurPage, 0-based).</summary>
    public int CurrentPage => _curPage;

    /// <summary>The current combat target id fed by the executable (-1 = none).</summary>
    public short TargetId { get; set; } = -1;

    /// <summary>The live cursor (fed each frame by the executable; tests set it directly).</summary>
    public UiPoint Cursor { get; set; }

    /// <summary>Game-clock seconds source for drag-triggered casts (number keys pass their own now).</summary>
    public Func<double> NowSeconds { get; set; } = static () => 0;

    /// <summary>The skill id placed at a [page][slot] (0 = empty).</summary>
    public uint SkillAt(int page, int slot) => InBounds(page, slot) ? _skillId[page, slot] : 0;

    // ---- Wiring ------------------------------------------------------------

    /// <summary>
    /// Wire the in-world hooks. Loads the persisted hotkeys once the first MyInfo lands (after the
    /// skill tree has populated, so class-skill validation via <see cref="SkillTreeDialog.HasSkill"/>
    /// resolves), and repoints the inventory used for the consumable count strings.
    /// </summary>
    public void Bind(InGameState inGame)
    {
        _inGame = inGame;
        _inv = inGame.Inventory;
        inGame.MyInfoReceived += _ =>
        {
            if (!_loaded)
                LoadFromStore();
        };
    }

    // ---- Placement (SetReceiveSelectedSkill / ReceiveIconDrop) --------------

    /// <summary>
    /// Place a skill id at [page][slot], replacing any occupant. Rejects an id that is neither a
    /// usable-item skill (≥ <see cref="SkillTableSet.UsableItemIdMin"/>) nor a learned class skill
    /// (<see cref="SkillTreeDialog.HasSkill"/>), or that is missing from the skill table. Persists.
    /// </summary>
    public bool SetSkill(int page, int slot, uint skillId)
    {
        if (!SetSkillInternal(page, slot, skillId))
            return false;

        Save();
        if (page == _curPage)
            RefreshStrings();
        return true;
    }

    /// <summary>The first empty slot on the current page (GetEmptySlotIndex), or -1.</summary>
    public int GetEmptySlotIndex()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_skillId[_curPage, i] == 0)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Quick-add a skill dragged/right-clicked out of the skill tree into the first empty slot on
    /// the current page (CUIHotKeyDlg::SetReceiveSelectedSkill via GetEmptySlotIndex). Returns false
    /// when the page is full or the skill is invalid.
    /// </summary>
    public bool ReceiveSkillFromTree(uint skillId)
    {
        int slot = GetEmptySlotIndex();
        return slot >= 0 && SetSkill(_curPage, slot, skillId);
    }

    /// <summary>
    /// Accept a usable item dropped from the inventory (CUIHotKeyDlg::SetReceiveSelectedItem): its
    /// <c>dwEffectID1</c> skill id must be ≥ <see cref="SkillTableSet.UsableItemIdMin"/>. Places it
    /// in the first empty slot on the current page.
    /// </summary>
    public bool ReceiveItemFromInventory(uint effectSkillId)
    {
        if (effectSkillId < SkillTableSet.UsableItemIdMin)
            return false;
        int slot = GetEmptySlotIndex();
        return slot >= 0 && SetSkill(_curPage, slot, effectSkillId);
    }

    private bool SetSkillInternal(int page, int slot, uint skillId)
    {
        if (!InBounds(page, slot) || skillId == 0)
            return false;

        bool valid = skillId >= SkillTableSet.UsableItemIdMin || _tree?.HasSkill(skillId) == true;
        if (!valid)
            return false;

        SkillRow? skill = _skills.Find(skillId);
        if (skill == null)
            return false;

        RemoveSlot(page, slot);

        N3UiRect region = _slotArea[slot]?.Region ?? default;
        UiIconControl icon = UiIconControl.CreateRuntime(region, (int)skillId);
        icon.DragState = _drag;
        icon.IconTexture = SkillTreeDialog.SkillIconFileName(skillId);
        icon.ItemSkillId = (int)skillId;
        icon.Payload = skill;
        icon.SetVisible(page == _curPage);
        _root.AddChild(icon);

        _skillId[page, slot] = skillId;
        _icon[page, slot] = icon;
        return true;
    }

    private void RemoveSlot(int page, int slot)
    {
        if (_icon[page, slot] is { } icon)
            _root.RemoveChild(icon);
        _icon[page, slot] = null;
        _skillId[page, slot] = 0;
    }

    // ---- Cast (DoOperate / EffectTriggerByHotKey) --------------------------

    /// <summary>
    /// Number-key trigger (EffectTriggerByHotKey): cast the current page's slot skill at the current
    /// target. Returns the cast outcome (a no-skill / hidden slot returns a non-success default).
    /// </summary>
    public CastResult TriggerSlot(int slot, double nowSeconds)
    {
        if (slot is < 0 or >= SlotCount)
            return default;

        uint id = _skillId[_curPage, slot];
        if (id == 0 || _icon[_curPage, slot] is not { Visible: true })
            return default;

        SkillRow? skill = _skills.Find(id);
        if (skill == null || _inGame == null)
            return default;

        return _inGame.CastSkill(skill, TargetId, nowSeconds);
    }

    // ---- Paging (SetHotKeyPage / PageUp / PageDown) ------------------------

    /// <summary>Show page <paramref name="page"/>; hide every other page's icons and refresh labels.</summary>
    public void SetPage(int page)
    {
        if (page is < 0 or >= PageCount)
            return;

        _curPage = page;
        for (int p = 0; p < PageCount; p++)
            for (int s = 0; s < SlotCount; s++)
                _icon[p, s]?.SetVisible(p == page);

        RefreshStrings();
    }

    /// <summary>CUIHotKeyDlg::PageUp — to a lower page number.</summary>
    public void PageUp()
    {
        if (_curPage > 0)
            SetPage(_curPage - 1);
    }

    /// <summary>CUIHotKeyDlg::PageDown — to a higher page number.</summary>
    public void PageDown()
    {
        if (_curPage < PageCount - 1)
            SetPage(_curPage + 1);
    }

    // ---- Class change flush (ClassChangeHotkeyFlush) -----------------------

    /// <summary>
    /// CUIHotKeyDlg::ClassChangeHotkeyFlush — clear every page (a promotion invalidates the old
    /// class's skills) and persist the now-empty set. Wired to
    /// <see cref="ClassChangeDialog.ClassChanged"/>.
    /// </summary>
    public void FlushAll()
    {
        for (int p = 0; p < PageCount; p++)
            for (int s = 0; s < SlotCount; s++)
                RemoveSlot(p, s);

        _curPage = 0;
        Save();
        RefreshStrings();
    }

    // ---- Persistence (CloseIconRegistry / InitIconUpdate) ------------------

    /// <summary>Persist the full [page][slot] grid (CloseIconRegistry).</summary>
    public void Save()
    {
        var entries = new List<HotkeyEntry>();
        for (int p = 0; p < PageCount; p++)
            for (int s = 0; s < SlotCount; s++)
                if (_skillId[p, s] != 0)
                    entries.Add(new HotkeyEntry(p, s, _skillId[p, s]));

        _store.Save(entries);
    }

    /// <summary>
    /// Load the persisted hotkeys (InitIconUpdate): clear the grid, then place each stored entry
    /// that still validates (class skill still known, or usable-item id, and present in the table).
    /// Invalid entries are dropped silently, as the C++ <c>continue</c> does.
    /// </summary>
    public void LoadFromStore()
    {
        for (int p = 0; p < PageCount; p++)
            for (int s = 0; s < SlotCount; s++)
                RemoveSlot(p, s);

        foreach (HotkeyEntry e in _store.Load())
            SetSkillInternal(e.Page, e.Slot, e.SkillId);

        _loaded = true;
        SetPage(0);
    }

    // ---- String labels (DisplayCountStr / DisplayTooltipStr) ---------------

    private void RefreshStrings()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            UiStringControl? cs = _countStr[i];
            UiStringControl? ts = _tooltipStr[i];
            uint id = _skillId[_curPage, i];

            if (id != 0 && _skills.Find(id) is { } skill)
            {
                if (ts != null)
                    ts.Text = skill.Name;

                if (cs != null)
                {
                    if (skill.ExhaustItem != 0)
                    {
                        cs.Text = _inv.CountById((int)skill.ExhaustItem).ToString(CultureInfo.InvariantCulture);
                        cs.SetVisible(true);
                    }
                    else
                    {
                        cs.SetVisible(false);
                    }
                }
            }
            else
            {
                cs?.SetVisible(false);
            }

            // Tooltip labels are shown by the device layer only on hover (deferred); hide by default.
            ts?.SetVisible(false);
        }
    }

    // ---- Message routing (CUIHotKeyDlg::ReceiveMessage) --------------------

    private void OnMessage(UiControl sender, uint msg)
    {
        switch (msg)
        {
            case UiMsg.ButtonClick:
                if (ReferenceEquals(sender, _btnUp))
                    PageUp();
                else if (ReferenceEquals(sender, _btnDown))
                    PageDown();
                break;

            case UiMsg.IconDownFirst:
                OnIconDownFirst(sender);
                break;

            case UiMsg.IconDown:
                _drag.SelectedIcon.Icon?.MoveToCursor(Cursor);
                break;

            case UiMsg.IconUp:
                OnIconUp();
                break;
        }
    }

    private void OnIconDownFirst(UiControl sender)
    {
        if (sender is not UiIconControl icon)
            return;

        int order = FindSlotOnCurrentPage(icon);
        if (order < 0)
            return;

        _dragSrcOrder = order;
        _drag.SelectedIcon.Location = new UiWndIconInfo
        {
            Wnd = UiWnd.Hotkey,
            District = UiWndDistrict.SkillHotkey,
            Order = order,
        };
        _drag.SelectedIcon.Item = _skills.Find(_skillId[_curPage, order]);
        _drag.SelectedIcon.Icon = icon;
        icon.MoveToCursor(Cursor);
    }

    private void OnIconUp()
    {
        if (_drag.SelectedIcon.Icon is not { } icon || _dragSrcOrder < 0)
        {
            _drag.SelectedIcon.Clear();
            _dragSrcOrder = -1;
            return;
        }

        int src = _dragSrcOrder;

        if (IsInDialog(Cursor))
        {
            int dest = GetAreaOrderAt(Cursor);
            if (dest == src)
            {
                // Dropped on itself → cast at the current target.
                SnapHome(icon, src);
                CastSlot(src);
            }
            else if (dest == -1)
            {
                // Inside the bar but off any slot → delete.
                RemoveSlot(_curPage, src);
                Save();
                RefreshStrings();
            }
            else
            {
                // Move into another slot (overwriting its occupant, like the C++).
                if (_skillId[_curPage, dest] != 0)
                    RemoveSlot(_curPage, dest);

                _skillId[_curPage, dest] = _skillId[_curPage, src];
                _icon[_curPage, dest] = icon;
                _skillId[_curPage, src] = 0;
                _icon[_curPage, src] = null;
                SnapHome(icon, dest);
                Save();
                RefreshStrings();
            }
        }
        else
        {
            // Dropped outside the bar → delete.
            RemoveSlot(_curPage, src);
            Save();
            RefreshStrings();
        }

        _drag.SelectedIcon.Clear();
        _dragSrcOrder = -1;
    }

    private void CastSlot(int slot)
    {
        uint id = _skillId[_curPage, slot];
        if (id == 0 || _inGame == null || _skills.Find(id) is not { } skill)
            return;
        _inGame.CastSkill(skill, TargetId, NowSeconds());
    }

    // ---- Helpers -----------------------------------------------------------

    private int FindSlotOnCurrentPage(UiIconControl icon)
    {
        for (int i = 0; i < SlotCount; i++)
            if (ReferenceEquals(_icon[_curPage, i], icon))
                return i;
        return -1;
    }

    private int GetAreaOrderAt(UiPoint cursor)
    {
        for (int i = 0; i < SlotCount; i++)
            if (_slotArea[i]?.IsIn(cursor.X, cursor.Y) == true)
                return i;
        return -1;
    }

    private bool IsInDialog(UiPoint cursor) => UiRectMath.IsIn(_root.Region, cursor.X, cursor.Y);

    private void SnapHome(UiIconControl icon, int order)
    {
        if (_slotArea[order] is { } area)
            icon.SetIconRegion(area.Region);
    }

    private static bool InBounds(int page, int slot) =>
        page is >= 0 and < PageCount && slot is >= 0 and < SlotCount;
}
