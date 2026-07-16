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
    private const int ColInventory = 11;      // 12 szInventory
    private const int ColDroppedItem = 13;    // 14 szDroppedItem
    private const int ColTargetBar = 14;      // 15 szTargetBar
    private const int ColSkillTree = 16;      // 17 szSkillTree
    private const int ColHotKey = 17;         // 18 szHotKey
    private const int ColMiniMap = 18;        // 19 szMiniMap
    private const int ColCharacterCreate = 23; // 24 szCharacterCreate
    private const int ColCharacterSelect = 24; // 25 szCharacterSelect
    private const int ColMessageBox = 26;     // 27 szMessageBox
    private const int ColDead = 52;           // 53 szDead
    private const int ColNationSelect = 55;   // 56 szNationSelect
    private const int ColLoginIntro = 118;    // 119 szLoginIntro
    private const int ColNationSelectNew = 129; // 130 szNationSelectNew

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

    public string TargetBar(int nation) => Get(nation, ColTargetBar);

    public string SkillTree(int nation) => Get(nation, ColSkillTree);

    public string HotKey(int nation) => Get(nation, ColHotKey);

    public string MiniMap(int nation) => Get(nation, ColMiniMap);
}
