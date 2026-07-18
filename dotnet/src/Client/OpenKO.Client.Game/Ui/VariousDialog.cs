using System.Globalization;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the multi-page character sheet — port of <c>CUIVarious</c> and its
/// <c>CUIState</c> (status) / <c>CUIKnights</c> (clan) pages (Client/WarFare/UIVarious.cpp).
///
/// The status page mirrors <c>CUIVarious::UpdateAllStates</c>: it binds identity/level/exp,
/// HP/MP, attack/guard, weight, the five primary stats (with their item deltas) and the six
/// resistances to <see cref="LocalPlayer"/>. The five stat-up buttons
/// (<c>Btn_Strength</c>…<c>Btn_MagicAttack</c>) are shown only while a bonus point remains
/// (<c>UpdateBonusPointAndButtons</c>) and each spends one point via
/// <see cref="StatPointProtocol"/> (<c>MsgSendAblityPointChange</c>, +1).
///
/// The clan page mirrors <c>CUIKnights</c>: <c>List_clan_ChrID/Grade/Level/Job</c> are filled
/// from the MemberInfoAll broadcast; the officer-only management buttons
/// (<c>btn_clan_admit/Appoint/Remove</c>) send the corresponding knights packets, gated on the
/// local player's clan duty (<see cref="ClanDuty"/>).
///
/// The pages live in separate .uif files (szState, szKnights) loaded into the szVarious frame;
/// the executable passes those page roots in. The slide-open animation, quest page and the
/// friends list (Btn_Add/Delete/Whisper + List_Friends) are deferred — see the TODO 9.9 note.
/// </summary>
public sealed class VariousDialog
{
    // Stat button → WIZ_POINT_CHANGE type (CUIState::ReceiveMessage).
    private const byte TypeStrength = StatPointProtocol.Strength;
    private const byte TypeStamina = StatPointProtocol.Stamina;
    private const byte TypeDexterity = StatPointProtocol.Dexterity;
    private const byte TypeIntelligence = StatPointProtocol.Intelligence;
    private const byte TypeMagicAttack = StatPointProtocol.MagicAttack;

    // e_KnightsDuty (GameDef.h).
    public const byte DutyUnknown = 0;
    public const byte DutyChief = 1;
    public const byte DutyViceChief = 2;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiControl _statePage;
    private readonly UiControl _clanPage;

    // Status-page controls.
    private readonly UiStringControl? _id;
    private readonly UiStringControl? _class;
    private readonly UiStringControl? _race;
    private readonly UiStringControl? _nation;
    private readonly UiStringControl? _level;
    private readonly UiStringControl? _exp;
    private readonly UiStringControl? _hp;
    private readonly UiStringControl? _mp;
    private readonly UiStringControl? _ap;
    private readonly UiStringControl? _gp;
    private readonly UiStringControl? _weight;
    private readonly UiStringControl? _bonusPoint;
    private readonly UiStringControl? _realmPoint;
    private readonly UiStringControl? _strength;
    private readonly UiStringControl? _stamina;
    private readonly UiStringControl? _dexterity;
    private readonly UiStringControl? _intelligence;
    private readonly UiStringControl? _magicAttack;
    private readonly UiStringControl? _registFire;
    private readonly UiStringControl? _registCold;
    private readonly UiStringControl? _registLight;
    private readonly UiStringControl? _registMagic;
    private readonly UiStringControl? _registCurse;
    private readonly UiStringControl? _registPoison;
    private readonly UiControl? _btnStrength;
    private readonly UiControl? _btnStamina;
    private readonly UiControl? _btnDexterity;
    private readonly UiControl? _btnIntelligence;
    private readonly UiControl? _btnMagicAttack;

    // Clan-page controls.
    private readonly UiStringControl? _clanName;
    private readonly UiStringControl? _clanMemberCount;
    private readonly UiListControl? _listChrId;
    private readonly UiListControl? _listGrade;
    private readonly UiListControl? _listLevel;
    private readonly UiListControl? _listJob;
    private readonly UiControl? _btnAdmit;
    private readonly UiControl? _btnAppoint;
    private readonly UiControl? _btnRemove;

    // Friends-page controls (CUIFriends) — a client-local list persisted to a text file.
    private readonly UiControl _friendsPage;
    private readonly UiListControl? _listFriends;
    private readonly UiStringControl? _friendPageText;
    private readonly UiControl? _btnFriendAdd;
    private readonly UiControl? _btnFriendDelete;
    private readonly UiControl? _btnFriendWhisper;
    private readonly UiControl? _btnFriendParty;
    private readonly UiControl? _btnFriendRefresh;
    private readonly UiControl? _btnFriendPageUp;
    private readonly UiControl? _btnFriendPageDown;

    private readonly IFriendStore _friendStore;
    private readonly List<FriendEntry> _friends = [];
    private int _friendPage;

    // CUIFriends iLinePerPage — the C++ hard-codes 10 (the region-derived height is commented out).
    private const int FriendsPerPage = 10;

    private LocalPlayer? _local;

    /// <summary>One friend as tracked client-side (__FriendsInfo). Status stays inert (server no-op).</summary>
    private sealed class FriendEntry
    {
        public required string Name { get; init; }

        public short Id { get; set; } = -1;

        public bool Online { get; set; }

        public bool InParty { get; set; }
    }

    public VariousDialog(
        GameContext context,
        UiControl root,
        UiControl? statePage = null,
        UiControl? clanPage = null,
        UiControl? friendsPage = null,
        IFriendStore? friendStore = null)
    {
        _context = context;
        _root = root;
        _statePage = statePage ?? root;
        _clanPage = clanPage ?? root;
        _friendsPage = friendsPage ?? root;
        _friendStore = friendStore ?? new InMemoryFriendStore();

        _id = _statePage.GetChildById<UiStringControl>("Text_ID");
        _class = _statePage.GetChildById<UiStringControl>("Text_Class");
        _race = _statePage.GetChildById<UiStringControl>("Text_Race");
        _nation = _statePage.GetChildById<UiStringControl>("Text_Nation");
        _level = _statePage.GetChildById<UiStringControl>("Text_Level");
        _exp = _statePage.GetChildById<UiStringControl>("Text_Exp");
        _hp = _statePage.GetChildById<UiStringControl>("Text_HP");
        _mp = _statePage.GetChildById<UiStringControl>("Text_MP");
        _ap = _statePage.GetChildById<UiStringControl>("Text_AP");
        _gp = _statePage.GetChildById<UiStringControl>("Text_GP");
        _weight = _statePage.GetChildById<UiStringControl>("Text_Weight");
        _bonusPoint = _statePage.GetChildById<UiStringControl>("Text_BonusPoint");
        _realmPoint = _statePage.GetChildById<UiStringControl>("Text_RealmPoint");
        _strength = _statePage.GetChildById<UiStringControl>("Text_Strength");
        _stamina = _statePage.GetChildById<UiStringControl>("Text_Stamina");
        _dexterity = _statePage.GetChildById<UiStringControl>("Text_Dexterity");
        _intelligence = _statePage.GetChildById<UiStringControl>("Text_Intelligence");
        _magicAttack = _statePage.GetChildById<UiStringControl>("Text_MagicAttack");
        _registFire = _statePage.GetChildById<UiStringControl>("Text_RegistFire");
        _registCold = _statePage.GetChildById<UiStringControl>("Text_RegistIce");
        _registLight = _statePage.GetChildById<UiStringControl>("Text_RegistLightR");
        _registMagic = _statePage.GetChildById<UiStringControl>("Text_RegistMagic");
        _registCurse = _statePage.GetChildById<UiStringControl>("Text_RegistCurse");
        _registPoison = _statePage.GetChildById<UiStringControl>("Text_RegistPoison");
        _btnStrength = _statePage.GetChildById("Btn_Strength");
        _btnStamina = _statePage.GetChildById("Btn_Stamina");
        _btnDexterity = _statePage.GetChildById("Btn_Dexterity");
        _btnIntelligence = _statePage.GetChildById("Btn_Intelligence");
        _btnMagicAttack = _statePage.GetChildById("Btn_MagicAttack");

        _clanName = _clanPage.GetChildById<UiStringControl>("Text_ClansName");
        _clanMemberCount = _clanPage.GetChildById<UiStringControl>("Text_clan_MemberCount");
        _listChrId = _clanPage.GetChildById<UiListControl>("List_clan_ChrID");
        _listGrade = _clanPage.GetChildById<UiListControl>("List_clan_Grade");
        _listLevel = _clanPage.GetChildById<UiListControl>("List_clan_Level");
        _listJob = _clanPage.GetChildById<UiListControl>("List_clan_Job");
        _btnAdmit = _clanPage.GetChildById("btn_clan_admit");
        _btnAppoint = _clanPage.GetChildById("btn_clan_Appoint");
        _btnRemove = _clanPage.GetChildById("btn_clan_Remove");

        // Resolve the friends-page controls only when the page actually carries the friends list.
        // Btn_Party lowercases to the same id as the CUIVarious "btn_party" page tab, so binding it
        // on a plain frame root (missing szFriends page) would hijack the tab — guard against that.
        _listFriends = _friendsPage.GetChildById<UiListControl>("List_Friends");
        if (_listFriends != null)
        {
            _friendPageText = _friendsPage.GetChildById<UiStringControl>("String_Page");
            _btnFriendAdd = _friendsPage.GetChildById("Btn_Add");
            _btnFriendDelete = _friendsPage.GetChildById("Btn_Delete");
            _btnFriendWhisper = _friendsPage.GetChildById("Btn_Whisper");
            _btnFriendParty = _friendsPage.GetChildById("Btn_Party");
            _btnFriendRefresh = _friendsPage.GetChildById("Btn_Refresh");
            _btnFriendPageUp = _friendsPage.GetChildById("Btn_Page_Up");
            _btnFriendPageDown = _friendsPage.GetChildById("Btn_Page_Down");
        }

        _statePage.Message += OnMessage;
        if (!ReferenceEquals(_clanPage, _statePage))
            _clanPage.Message += OnMessage;
        if (!ReferenceEquals(_friendsPage, _statePage) && !ReferenceEquals(_friendsPage, _clanPage))
            _friendsPage.Message += OnMessage;
        if (!ReferenceEquals(_root, _statePage) && !ReferenceEquals(_root, _clanPage) && !ReferenceEquals(_root, _friendsPage))
            _root.Message += OnMessage;

        LoadFriendsFromStore();

        ShowStatePage();
        ChangeUiByDuty();
        _root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The current combat target id (fed by the executable) — the Admit invite target.</summary>
    public short TargetId { get; set; } = -1;

    /// <summary>The local player's clan duty (e_KnightsDuty); gates the management buttons.</summary>
    public byte ClanDuty { get; private set; } = DutyUnknown;

    /// <summary>Raised when Btn_Party is pressed (open the party window).</summary>
    public event Action? PartyPageRequested;

    public void Show() => _root.SetVisible(true);

    public void Hide() => _root.SetVisible(false);

    public void Toggle()
    {
        _root.SetVisible(!_root.Visible);
        if (_root.Visible && _local != null)
            FillState(_local);
    }

    /// <summary>Wire MyInfo (status refresh), the clan member broadcast and the friend status reply.</summary>
    public void Bind(InGameState inGame)
    {
        _local = inGame.World.Local;
        inGame.MyInfoReceived += FillState;
        inGame.KnightsReceived += OnKnights;
        inGame.FriendsReceived += OnFriendStatus;
    }

    /// <summary>Set the local clan duty and re-gate the management buttons.</summary>
    public void SetClanDuty(byte duty)
    {
        ClanDuty = duty;
        ChangeUiByDuty();
    }

    /// <summary>Set the displayed clan name (CUIKnights::UpdateKnightsName).</summary>
    public void SetClanName(string name)
    {
        if (_clanName != null)
            _clanName.Text = name;
    }

    /// <summary>CUIVarious::UpdateAllStates — bind the whole status page to the local player.</summary>
    public void FillState(LocalPlayer p)
    {
        _local = p;
        if (_id != null)
            _id.Text = p.Name;
        if (_class != null)
            _class.Text = p.Class.ToString(CultureInfo.InvariantCulture);
        if (_race != null)
            _race.Text = p.Race.ToString(CultureInfo.InvariantCulture);
        if (_nation != null)
            _nation.Text = p.Nation.ToString(CultureInfo.InvariantCulture);
        if (_level != null)
            _level.Text = p.Level.ToString(CultureInfo.InvariantCulture);
        if (_exp != null)
            _exp.Text = $"{p.Exp} / {p.MaxExp}";
        if (_hp != null)
            _hp.Text = $"{p.Hp} / {p.MaxHp}";
        if (_mp != null)
            _mp.Text = $"{p.Mp} / {p.MaxMp}";
        if (_ap != null)
            _ap.Text = p.TotalHit.ToString(CultureInfo.InvariantCulture);
        if (_gp != null)
            _gp.Text = p.TotalAc.ToString(CultureInfo.InvariantCulture);
        if (_weight != null)
            _weight.Text = string.Format(CultureInfo.InvariantCulture, "{0:F1}/{1:F1}", p.CurWeight * 0.1f, p.MaxWeight * 0.1f);
        if (_realmPoint != null)
            _realmPoint.Text = $"{p.Loyalty} / {p.LoyaltyMonthly}";

        SetStat(_strength, p.Str, p.ItemStr);
        SetStat(_stamina, p.Sta, p.ItemSta);
        SetStat(_dexterity, p.Dex, p.ItemDex);
        SetStat(_intelligence, p.Intel, p.ItemIntel);
        SetStat(_magicAttack, p.Cha, p.ItemCha);

        SetInt(_registFire, p.FireResist);
        SetInt(_registCold, p.ColdResist);
        SetInt(_registLight, p.LightningResist);
        SetInt(_registMagic, p.MagicResist);
        SetInt(_registCurse, p.DiseaseResist);
        SetInt(_registPoison, p.PoisonResist);

        UpdateBonusPointAndButtons(p.Points);
    }

    /// <summary>CUIState::UpdateBonusPointAndButtons — show the stat buttons only with points to spend.</summary>
    public void UpdateBonusPointAndButtons(int bonusPoint)
    {
        if (_bonusPoint != null)
            _bonusPoint.Text = bonusPoint.ToString(CultureInfo.InvariantCulture);

        bool enable = bonusPoint > 0;
        _btnStrength?.SetVisible(enable);
        _btnStamina?.SetVisible(enable);
        _btnDexterity?.SetVisible(enable);
        _btnIntelligence?.SetVisible(enable);
        _btnMagicAttack?.SetVisible(enable);
    }

    /// <summary>CGameProcMain::MsgRecv_Knights — fill the clan member lists from a MemberInfoAll broadcast.</summary>
    public void OnKnights(byte sub, byte[] payload)
    {
        if (sub != KnightsProtocol.MemberReq)
            return;

        KnightsProtocol.ClanMemberList members = KnightsProtocol.ParseMemberList(payload);
        PopulateMembers(members);
    }

    private void PopulateMembers(KnightsProtocol.ClanMemberList members)
    {
        if (_clanMemberCount != null)
            _clanMemberCount.Text = $"{members.Online} / {members.Total}";

        _listChrId?.ResetContent();
        _listGrade?.ResetContent();
        _listLevel?.ResetContent();
        _listJob?.ResetContent();

        foreach (KnightsProtocol.ClanMemberRow row in members.Members)
        {
            if (row.Connected)
            {
                _listGrade?.AddString(DutyText(row.Duty));
                _listChrId?.AddString(row.Name);
                _listLevel?.AddString(row.Level.ToString(CultureInfo.InvariantCulture));
                _listJob?.AddString(row.Class.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                _listGrade?.AddString("....");
                _listChrId?.AddString(row.Name);
                _listLevel?.AddString("....");
                _listJob?.AddString("....");
            }
        }
    }

    /// <summary>CUIKnights::ChangeUIByDuty — chief sees admit/appoint/remove, vice-chief only admit.</summary>
    private void ChangeUiByDuty()
    {
        bool chief = ClanDuty == DutyChief;
        bool viceChief = ClanDuty == DutyViceChief;
        _btnAdmit?.SetVisible(chief || viceChief);
        _btnAppoint?.SetVisible(chief);
        _btnRemove?.SetVisible(chief);
    }

    private void ShowStatePage()
    {
        if (ReferenceEquals(_statePage, _clanPage))
            return; // single combined tree (tests) — nothing to toggle
        _statePage.SetVisible(true);
        _clanPage.SetVisible(false);
        HideFriendsPage();
    }

    private void ShowClanPage()
    {
        if (!ReferenceEquals(_statePage, _clanPage))
        {
            _statePage.SetVisible(false);
            _clanPage.SetVisible(true);
            HideFriendsPage();
        }

        RequestMemberList();
    }

    /// <summary>Hide the friends page when it is a distinct .uif (never the shared frame root).</summary>
    private void HideFriendsPage()
    {
        if (!ReferenceEquals(_friendsPage, _root)
            && !ReferenceEquals(_friendsPage, _statePage)
            && !ReferenceEquals(_friendsPage, _clanPage))
            _friendsPage.SetVisible(false);
    }

    /// <summary>CUIVarious page tab → CUIFriends: show the friends page, refresh and query status.</summary>
    private void ShowFriendsPage()
    {
        if (!ReferenceEquals(_friendsPage, _root))
        {
            _statePage.SetVisible(false);
            _clanPage.SetVisible(false);
            _friendsPage.SetVisible(true);
        }

        UpdateFriendList();
        SendFriendQuery();
    }

    // ---- Friends list (CUIFriends) -----------------------------------------

    /// <summary>The friend names currently tracked (client-local; sorted like the C++ std::map).</summary>
    public IReadOnlyList<string> FriendNames => _friends.Select(f => f.Name).ToList();

    /// <summary>The current friends page index (0-based).</summary>
    public int FriendPage => _friendPage;

    private void LoadFriendsFromStore()
    {
        foreach (string name in _friendStore.Load())
            MemberAdd(name, -1, false, false);
        UpdateFriendList();
    }

    /// <summary>CUIFriends::MemberAdd — insert a friend (sorted, no duplicates). Returns true on add.</summary>
    public bool MemberAdd(string name, short id, bool online, bool inParty)
    {
        if (string.IsNullOrEmpty(name) || _friends.Any(f => f.Name == name))
            return false;

        var entry = new FriendEntry { Name = name, Id = id, Online = online, InParty = inParty };
        int idx = _friends.FindIndex(f => string.CompareOrdinal(f.Name, name) > 0);
        if (idx < 0)
            _friends.Add(entry);
        else
            _friends.Insert(idx, entry);
        return true;
    }

    /// <summary>CUIFriends::MemberDelete — erase a friend by name. Returns true on removal.</summary>
    public bool MemberDelete(string name)
    {
        int idx = _friends.FindIndex(f => f.Name == name);
        if (idx < 0)
            return false;
        _friends.RemoveAt(idx);
        return true;
    }

    /// <summary>CUIFriends::UpdateList — fill the visible page of the friends list control.</summary>
    public void UpdateFriendList()
    {
        if (_listFriends == null)
            return;

        int prevSel = _listFriends.CurSel;
        _listFriends.ResetContent();
        if (_friends.Count == 0)
            return;

        int pageMax = _friends.Count / FriendsPerPage;
        if (_friendPage < 0 || _friendPage > pageMax)
            return;

        int skip = _friendPage * FriendsPerPage;
        if (skip >= _friends.Count)
            return;

        if (_friendPageText != null)
            _friendPageText.Text = (_friendPage + 1).ToString(CultureInfo.InvariantCulture);

        for (int i = 0; i < FriendsPerPage && skip + i < _friends.Count; i++)
            _listFriends.AddString(_friends[skip + i].Name);

        _listFriends.SetCurSel(prevSel);
    }

    /// <summary>
    /// CUIFriends::MsgRecv_MemberInfo — apply the online/party status reply. Strict 1:1: the server
    /// is a no-op upstream (<c>#if 0</c>), so this is never actually invoked in play; kept faithful.
    /// </summary>
    public void OnFriendStatus(IReadOnlyList<FriendStatus> statuses)
    {
        foreach (FriendStatus s in statuses)
        {
            FriendEntry? entry = _friends.Find(f => f.Name == s.Name);
            if (entry == null)
                continue;
            entry.Id = s.Id;
            entry.Online = s.Online;
            entry.InParty = s.InParty;
        }

        UpdateFriendList();
    }

    private void SendFriendQuery()
    {
        if (_friends.Count == 0)
            return;
        _context.Client.Send(FriendProtocol.BuildRequest(FriendNames));
    }

    private bool HandleFriendButton(UiControl sender)
    {
        if (ReferenceEquals(sender, _btnFriendAdd))
            AddFriend();
        else if (ReferenceEquals(sender, _btnFriendDelete))
            DeleteFriend();
        else if (ReferenceEquals(sender, _btnFriendWhisper))
            WhisperFriend();
        else if (ReferenceEquals(sender, _btnFriendParty))
            InviteFriendToParty();
        else if (ReferenceEquals(sender, _btnFriendRefresh))
            SendFriendQuery();
        else if (ReferenceEquals(sender, _btnFriendPageUp))
            ChangeFriendPage(-1);
        else if (ReferenceEquals(sender, _btnFriendPageDown))
            ChangeFriendPage(+1);
        else
            return false;
        return true;
    }

    /// <summary>CUIFriends btn_add — add the current target by name, persist and query its status.</summary>
    public bool AddFriend()
    {
        if (TargetId < 0 || !_context.InGame.World.TryGet(TargetId, out RemotePlayer target)
            || target.Name.Length == 0)
            return false;

        if (!MemberAdd(target.Name, TargetId, true, false))
            return false;

        _friendStore.Save(FriendNames);
        _context.Client.Send(FriendProtocol.BuildRequest([target.Name]));
        UpdateFriendList();
        return true;
    }

    /// <summary>CUIFriends btn_delete — erase the selected friend and persist.</summary>
    public bool DeleteFriend()
    {
        if (SelectedFriendName() is not { Length: > 0 } name || !MemberDelete(name))
            return false;
        _friendStore.Save(FriendNames);
        UpdateFriendList();
        return true;
    }

    /// <summary>CUIFriends btn_whisper — pick the selected friend as the 1:1 chat target.</summary>
    private void WhisperFriend()
    {
        if (SelectedFriendName() is { Length: > 0 } name
            && WorldProtocol.BuildChatTarget(name) is { } packet)
            _context.Client.Send(packet);
    }

    /// <summary>CUIFriends btn_Party — invite the selected friend into a party.</summary>
    private void InviteFriendToParty()
    {
        if (SelectedFriendName() is { Length: > 0 } name)
            _context.Client.Send(PartyProtocol.BuildCreate(name));
    }

    private void ChangeFriendPage(int delta)
    {
        int prev = _friendPage;
        _friendPage += delta;
        if (_friendPage < 0)
        {
            _friendPage = 0;
        }
        else
        {
            int pageMax = (_friends.Count / FriendsPerPage) + 1;
            if (_friendPage >= pageMax)
                _friendPage = pageMax - 1;
        }

        if (_friendPage != prev)
        {
            UpdateFriendList();
            SendFriendQuery();
        }
    }

    private string? SelectedFriendName()
    {
        if (_listFriends is not { } list)
            return null;
        return list.GetString(list.CurSel, out string name) ? name : null;
    }

    /// <summary>Request the full clan member list (CUIKnights::MsgSend_MemberInfoAll).</summary>
    public void RequestMemberList() => _context.Client.Send(KnightsProtocol.BuildMemberInfoAll());

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        // Friends-page buttons share lowercased ids with the page tabs (Btn_Party), so resolve
        // them by control reference first (CUIFriends is a distinct .uif page).
        if (HandleFriendButton(sender))
            return;

        // The shipped .uif ids are inconsistently cased (e.g. btn_state vs Btn_clan_Remove), so
        // match case-insensitively — the original compares resolved control pointers.
        switch (sender.Id.ToLowerInvariant())
        {
            // ---- status page stat-up (only fires when a button is visible i.e. points remain) ----
            case "btn_strength":
                SpendStat(TypeStrength);
                break;
            case "btn_stamina":
                SpendStat(TypeStamina);
                break;
            case "btn_dexterity":
                SpendStat(TypeDexterity);
                break;
            case "btn_intelligence":
                SpendStat(TypeIntelligence);
                break;
            case "btn_magicattack":
                SpendStat(TypeMagicAttack);
                break;

            // ---- page tabs (CUIVarious::UpdatePageButtons) ----
            case "btn_state":
                ShowStatePage();
                break;
            case "btn_clan":
                ShowClanPage();
                break;
            case "btn_party":
                PartyPageRequested?.Invoke();
                break;
            case "btn_quest": // quest page deferred (light)
                break;
            case "btn_friends":
                ShowFriendsPage();
                break;
            case "btn_close":
                Hide();
                break;

            // ---- clan management (officer-gated by button visibility) ----
            case "btn_clan_admit":
                Admit();
                break;
            case "btn_clan_remove":
                Expel();
                break;
            case "btn_clan_appoint":
                Appoint();
                break;
            case "btn_clan_refresh":
                RequestMemberList();
                break;
        }
    }

    /// <summary>CUIState::MsgSendAblityPointChange — only when a bonus point remains.</summary>
    private void SpendStat(byte type)
    {
        if (_local is not { Points: > 0 })
            return;
        _context.Client.Send(StatPointProtocol.Build(type, 1));
    }

    /// <summary>CUIKnights::AdmitButtonHandler — invite the current target into the clan.</summary>
    private void Admit()
    {
        if (TargetId < 0)
            return;
        _context.Client.Send(KnightsProtocol.BuildJoin(TargetId));
    }

    /// <summary>CUIKnights::RemoveButtonHandler — expel the selected member by name.</summary>
    private void Expel()
    {
        if (SelectedMemberName() is { Length: > 0 } name)
            _context.Client.Send(KnightsProtocol.BuildExpel(name));
    }

    /// <summary>CUIKnights::AppointButtonHandler — appoint the selected member vice-chief.</summary>
    private void Appoint()
    {
        if (SelectedMemberName() is { Length: > 0 } name)
            _context.Client.Send(KnightsProtocol.BuildAppointViceChief(name));
    }

    private string? SelectedMemberName()
    {
        if (_listChrId is not { } list)
            return null;
        return list.GetString(list.CurSel, out string name) ? name : null;
    }

    private static void SetStat(UiStringControl? control, int value, int delta)
    {
        if (control != null)
            control.Text = delta > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}(+{1})", value, delta)
                : value.ToString(CultureInfo.InvariantCulture);
    }

    private static void SetInt(UiStringControl? control, int value)
    {
        if (control != null)
            control.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private static string DutyText(byte duty) => duty switch
    {
        DutyChief => "Chief",
        DutyViceChief => "Vice Chief",
        3 => "Punished",
        4 => "Trainee",
        5 => "Knight",
        6 => "Officer",
        _ => "...",
    };
}
