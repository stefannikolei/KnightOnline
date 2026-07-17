using OpenKO.Client.Assets;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Typed view over <c>Data\UIs_us.tbl</c> (<c>__TABLE_UI_RESRC</c>, GameDef.h) —
/// the per-nation map of dialog .uif filenames. Row id 1 = Karus, 2 = El Morad;
/// the frontend (login) loads before a nation exists and uses a random row like
/// the original (<c>GameProcLogIn_1298::Init</c>).
/// </summary>
public sealed class UiResourceTable
{
    // 1-based column positions from GameDef.h (index 0 is the uint id).
    private const int ColLogin = 1;           // 02 szLogIn
    private const int ColCmd = 2;             // 03 szCmd
    private const int ColChat = 3;            // 04 szChat
    private const int ColMsgOutput = 4;       // 05 szMsgOutput
    private const int ColStateBar = 5;        // 06 szStateBar
    private const int ColVarious = 6;         // 07 szVarious (multi-page sheet frame)
    private const int ColState = 7;           // 08 szState (the CUIState status page)
    private const int ColKnights = 8;         // 09 szKnights (the CUIKnights clan page)
    private const int ColPartyOrForce = 19;   // 20 szPartyOrForce
    private const int ColPartyBBS = 20;       // 21 szPartyBBS (recruitment board — dialog deferred)
    private const int ColKnightsOperation = 37; // 38 szKnightsOperation
    private const int ColInputClanName = 44;  // 45 szInputClanName (CUICreateClanName)
    private const int ColClanPage = 72;       // 73 szClanPage
    private const int ColInventory = 11;      // 12 szInventory
    private const int ColDroppedItem = 13;    // 14 szDroppedItem
    private const int ColTargetBar = 14;      // 15 szTargetBar
    private const int ColItemInfo = 28;       // 29 szItemInfo (image tooltip)
    private const int ColPersonalTrade = 29;  // 30 szPersonalTrade (CUIPerTradeDlg)
    private const int ColCountableEdit = 30;  // 31 szPersonalTradeEdit (base_tradeedit)
    private const int ColRepairTooltip = 34;  // 35 szRepairTooltip
    private const int ColSkillTree = 16;      // 17 szSkillTree
    private const int ColHotKey = 17;         // 18 szHotKey
    private const int ColClassChange = 38;    // 39 szClassChange
    private const int ColMiniMap = 18;        // 19 szMiniMap
    private const int ColCharacterCreate = 23; // 24 szCharacterCreate
    private const int ColCharacterSelect = 24; // 25 szCharacterSelect
    private const int ColMessageBox = 26;     // 27 szMessageBox
    private const int ColZoneChangeOrWarp = 32; // 33 szZoneChangeOrWarp
    private const int ColExchangeRepair = 33; // 34 szExchangeRepair
    private const int ColWareHouse = 40;      // 41 szWareHouse
    private const int ColInn = 43;            // 44 szInn
    private const int ColUpgradeSelect = 142; // 143 szUpgradeSelect
    private const int ColDead = 52;           // 53 szDead
    private const int ColNationSelect = 55;   // 56 szNationSelect
    private const int ColLoginIntro = 118;    // 119 szLoginIntro
    private const int ColNationSelectNew = 129; // 130 szNationSelectNew
    private const int ColHelp = 21;           // 22 szHelp
    private const int ColNotice = 22;         // 23 szNotice
    private const int ColNpcEvent = 31;       // 32 szNpcEvent
    private const int ColQuestMenu = 49;      // 50 szQuestMenu
    private const int ColQuestTalk = 50;      // 51 szQuestTalk
    private const int ColLevelGuide = 79;     // 80 szLvlGuide
    private const int ColExitMenu = 84;       // 85 szExitMenu

    private readonly N3TableFile _table;

    private UiResourceTable(N3TableFile table) => _table = table;

    public static UiResourceTable LoadFromFile(string path)
        => new(N3TableFile.LoadFromFile(path));

    /// <summary>Column value for the given nation row (1=Karus, 2=El Morad).</summary>
    private string Get(int nation, int column)
    {
        object[]? row = _table.Find((uint)(nation is 1 or 2 ? nation : 1));
        return row != null && column < row.Length ? (string)row[column] : string.Empty;
    }

    public string Login(int nation) => Get(nation, ColLogin);

    public string Cmd(int nation) => Get(nation, ColCmd);

    public string Dead(int nation) => Get(nation, ColDead);

    public string LoginIntro(int nation) => Get(nation, ColLoginIntro);

    public string NationSelect(int nation)
    {
        string v = Get(nation, ColNationSelectNew);
        return v.Length > 0 ? v : Get(nation, ColNationSelect);
    }

    public string CharacterSelect(int nation) => Get(nation, ColCharacterSelect);

    public string CharacterCreate(int nation) => Get(nation, ColCharacterCreate);

    public string MessageBox(int nation) => Get(nation, ColMessageBox);

    public string Chat(int nation) => Get(nation, ColChat);

    public string MsgOutput(int nation) => Get(nation, ColMsgOutput);

    public string StateBar(int nation) => Get(nation, ColStateBar);

    public string Inventory(int nation) => Get(nation, ColInventory);

    public string DroppedItem(int nation) => Get(nation, ColDroppedItem);

    /// <summary>szItemInfo — the image tooltip dialog (CUIImageTooltipDlg).</summary>
    public string ItemInfo(int nation) => Get(nation, ColItemInfo);

    /// <summary>szRepairTooltip — the repair tooltip dialog (CUIRepairTooltipDlg).</summary>
    public string RepairTooltip(int nation) => Get(nation, ColRepairTooltip);

    /// <summary>szPersonalTrade — the player-to-player trade window (CUIPerTradeDlg).</summary>
    public string PersonalTrade(int nation) => Get(nation, ColPersonalTrade);

    /// <summary>szPersonalTradeEdit — the countable stack-split popup (base_tradeedit).</summary>
    public string CountableItemEdit(int nation) => Get(nation, ColCountableEdit);

    public string TargetBar(int nation) => Get(nation, ColTargetBar);

    public string SkillTree(int nation) => Get(nation, ColSkillTree);

    /// <summary>szClassChange — the promotion dialog (CUIClassChange).</summary>
    public string ClassChange(int nation) => Get(nation, ColClassChange);

    public string HotKey(int nation) => Get(nation, ColHotKey);

    public string MiniMap(int nation) => Get(nation, ColMiniMap);

    /// <summary>szVarious — the multi-page character sheet frame (CUIVarious).</summary>
    public string Various(int nation) => Get(nation, ColVarious);

    /// <summary>szState — the status page loaded into the Various frame (CUIState).</summary>
    public string State(int nation) => Get(nation, ColState);

    /// <summary>szKnights — the clan page loaded into the Various frame (CUIKnights).</summary>
    public string Knights(int nation) => Get(nation, ColKnights);

    /// <summary>szPartyOrForce — the party/force member window (CUIPartyOrForce).</summary>
    public string PartyOrForce(int nation) => Get(nation, ColPartyOrForce);

    /// <summary>szPartyBBS — the party recruitment board (CUIPartyBBS — dialog deferred to a later slice).</summary>
    public string PartyBBS(int nation) => Get(nation, ColPartyBBS);

    /// <summary>szKnightsOperation — the clan browse/create/join window (CUIKnightsOperation).</summary>
    public string KnightsOperation(int nation) => Get(nation, ColKnightsOperation);

    /// <summary>szInputClanName — the clan-name entry popup (CUICreateClanName).</summary>
    public string InputClanName(int nation) => Get(nation, ColInputClanName);

    /// <summary>szClanPage — the clan cape/emblem page resource.</summary>
    public string ClanPage(int nation) => Get(nation, ColClanPage);

    /// <summary>szWareHouse — the bank/warehouse storage window (CUIWareHouseDlg).</summary>
    public string WareHouse(int nation) => Get(nation, ColWareHouse);

    /// <summary>szZoneChangeOrWarp — the NPC/object teleport menu (CUIWarp).</summary>
    public string ZoneChangeOrWarp(int nation) => Get(nation, ColZoneChangeOrWarp);

    /// <summary>szExchangeRepair — the NPC exchange/repair menu (repair via CItemRepairMgr).</summary>
    public string ExchangeRepair(int nation) => Get(nation, ColExchangeRepair);

    /// <summary>szInn — the inn-keeper NPC menu (CUIInn: warehouse / found-clan / trade board).</summary>
    public string Inn(int nation) => Get(nation, ColInn);

    /// <summary>szUpgradeSelect — the anvil upgrade-select window (CUIUpgradeSelect).</summary>
    public string UpgradeSelect(int nation) => Get(nation, ColUpgradeSelect);

    /// <summary>szHelp — the paged help window (CUIHelp).</summary>
    public string Help(int nation) => Get(nation, ColHelp);

    /// <summary>szNotice — the login/update notice banner (CUINotice).</summary>
    public string Notice(int nation) => Get(nation, ColNotice);

    /// <summary>szNpcEvent — the NPC event/vendor entry menu (CUINPCEvent).</summary>
    public string NpcEvent(int nation) => Get(nation, ColNpcEvent);

    /// <summary>szQuestMenu — the NPC quest menu (CUIQuestMenu).</summary>
    public string QuestMenu(int nation) => Get(nation, ColQuestMenu);

    /// <summary>szQuestTalk — the NPC talk window (CUIQuestTalk).</summary>
    public string QuestTalk(int nation) => Get(nation, ColQuestTalk);

    /// <summary>szLvlGuide — the level-based quest guide (CUILevelGuide).</summary>
    public string LevelGuide(int nation) => Get(nation, ColLevelGuide);

    /// <summary>szExitMenu — the in-game exit menu (CUIExitMenu).</summary>
    public string ExitMenu(int nation) => Get(nation, ColExitMenu);
}
