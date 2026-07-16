using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for character creation — port of <c>CUICharacterCreate</c> +
/// <c>CGameProcCharacterCreate</c>: race buttons (per nation), class buttons,
/// face/hair arrows, name edit, stat +/- with a bonus pool seeded from
/// <c>Data\NewChrValue.tbl</c> (id = race*10000 + class), and btn_create sending
/// WIZ_NEW_CHAR via <see cref="CharCreateState.CreateCharacter"/>.
/// </summary>
public sealed class CharCreateDialog
{
    // e_Race (GameDef.h).
    public const byte RaceKaArkTuarek = 1;
    public const byte RaceKaTuarek = 2;
    public const byte RaceKaWrinkleTuarek = 3;
    public const byte RaceKaPuriTuarek = 4;
    public const byte RaceElBabarian = 11;
    public const byte RaceElMan = 12;
    public const byte RaceElWomen = 13;

    // e_Class basic ids (shared/globals.h).
    public const short ClassKaWarrior = 101;
    public const short ClassElWarrior = 201;

    // Stat order matches the wire (str, sta, dex, int, cha/MAP) and the
    // btn_<id>_left/right ids in the .uif.
    private static readonly string[] StatIds = ["str", "sta", "dex", "int", "map"];

    private readonly GameContext _context;
    private readonly N3TableFile? _initValues;
    private readonly UiEditControl? _editName;
    private readonly UiControl? _btnCreate;
    private readonly UiControl? _btnCancel;
    private readonly (string Id, UiControl? Btn, byte Race)[] _raceButtons;
    private readonly (UiControl? Btn, int ClassOffset)[] _classButtons;
    private readonly UiControl? _btnFaceLeft;
    private readonly UiControl? _btnFaceRight;
    private readonly UiControl? _btnHairLeft;
    private readonly UiControl? _btnHairRight;

    private readonly int[] _stats = new int[5]; // str, sta, dex, int, cha(MAP)
    private readonly int[] _statMin = new int[5];

    /// <summary>Raised on btn_cancel (back to char select).</summary>
    public event Action? BackRequested;

    public UiControl Root { get; }

    public byte Race { get; private set; }

    public short Class { get; private set; }

    public byte Face { get; private set; }

    public byte Hair { get; private set; }

    public int BonusPoints { get; private set; }

    public IReadOnlyList<int> Stats => _stats;

    public string CharName => _editName?.Text ?? string.Empty;

    public CharCreateDialog(GameContext context, UiControl root, N3TableFile? initValues = null)
    {
        _context = context;
        Root = root;
        _initValues = initValues;

        _editName = root.GetChildById<UiEditControl>("edit_name");
        _btnCreate = root.GetChildById("btn_create");
        _btnCancel = root.GetChildById("btn_cancel");
        _btnFaceLeft = root.GetChildById("btn_face_left");
        _btnFaceRight = root.GetChildById("btn_face_right");
        _btnHairLeft = root.GetChildById("btn_hair_left");
        _btnHairRight = root.GetChildById("btn_hair_right");

        _raceButtons = context.Nation == NationSelectState.Karus
            ?
            [
                ("btn_race_ka_at", root.GetChildById("btn_race_ka_at"), RaceKaArkTuarek),
                ("btn_race_ka_tu", root.GetChildById("btn_race_ka_tu"), RaceKaTuarek),
                ("btn_race_ka_wt", root.GetChildById("btn_race_ka_wt"), RaceKaWrinkleTuarek),
                ("btn_race_ka_pt", root.GetChildById("btn_race_ka_pt"), RaceKaPuriTuarek),
            ]
            :
            [
                ("btn_race_el_ba", root.GetChildById("btn_race_el_ba"), RaceElBabarian),
                ("btn_race_el_rm", root.GetChildById("btn_race_el_rm"), RaceElMan),
                ("btn_race_el_rf", root.GetChildById("btn_race_el_rf"), RaceElWomen),
            ];

        _classButtons =
        [
            (root.GetChildById("btn_class_warrior"), 0),
            (root.GetChildById("btn_class_rogue"), 1),
            (root.GetChildById("btn_class_mage"), 2),
            (root.GetChildById("btn_class_priest"), 3),
        ];

        // Defaults: first race of the nation, warrior.
        SetRace(_raceButtons[0].Race);
        SetClass(0);

        root.Message += OnMessage;
    }

    public void SetRace(byte race)
    {
        Race = race;
        LoadInitValues();
    }

    /// <summary>0=warrior 1=rogue 2=mage 3=priest; nation picks the 100/200 block.</summary>
    public void SetClass(int classOffset)
    {
        short baseClass = _context.Nation == NationSelectState.Karus ? ClassKaWarrior : ClassElWarrior;
        Class = (short)(baseClass + classOffset);
        LoadInitValues();
    }

    /// <summary>Seed stats + bonus pool from NewChrValue.tbl (columns str,sta,dex,int,mp,bonus).</summary>
    private void LoadInitValues()
    {
        // Server floor: 50 each; the table overrides per race/class.
        for (int i = 0; i < 5; i++)
            _stats[i] = 50;
        BonusPoints = 0;

        object[]? row = _initValues?.Find((uint)(Race * 10000 + Class));
        if (row != null && row.Length >= 8)
        {
            for (int i = 0; i < 5; i++)
                _stats[i] = Convert.ToInt32(row[2 + i]);
            BonusPoints = Convert.ToInt32(row[7]);
        }

        _stats.CopyTo(_statMin, 0); // can't drop below the seeded values
    }

    /// <summary>btn_<stat>_right: spend a bonus point (max byte range).</summary>
    public bool IncreaseStat(int index)
    {
        if (BonusPoints <= 0 || _stats[index] >= 255)
            return false;
        _stats[index]++;
        BonusPoints--;
        return true;
    }

    /// <summary>btn_<stat>_left: refund down to the seeded value.</summary>
    public bool DecreaseStat(int index)
    {
        if (_stats[index] <= _statMin[index])
            return false;
        _stats[index]--;
        BonusPoints++;
        return true;
    }

    /// <summary>btn_create — WIZ_NEW_CHAR with the composed values.</summary>
    public void Create()
    {
        if (CharName.Length == 0)
            return;

        _context.CharCreate.CreateCharacter(
            CharName, Race, Class, Face, Hair,
            (byte)_stats[0], (byte)_stats[1], (byte)_stats[2], (byte)_stats[3], (byte)_stats[4]);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        if (ReferenceEquals(sender, _btnCreate))
        {
            Create();
            return;
        }

        if (ReferenceEquals(sender, _btnCancel))
        {
            BackRequested?.Invoke();
            return;
        }

        if (ReferenceEquals(sender, _btnFaceLeft))
        {
            Face = (byte)Math.Max(0, Face - 1);
            return;
        }

        if (ReferenceEquals(sender, _btnFaceRight))
        {
            Face = (byte)Math.Min(3, Face + 1);
            return;
        }

        if (ReferenceEquals(sender, _btnHairLeft))
        {
            Hair = (byte)Math.Max(0, Hair - 1);
            return;
        }

        if (ReferenceEquals(sender, _btnHairRight))
        {
            Hair = (byte)Math.Min(2, Hair + 1);
            return;
        }

        foreach ((_, UiControl? btn, byte race) in _raceButtons)
        {
            if (ReferenceEquals(sender, btn))
            {
                SetRace(race);
                return;
            }
        }

        foreach ((UiControl? btn, int offset) in _classButtons)
        {
            if (ReferenceEquals(sender, btn))
            {
                SetClass(offset);
                return;
            }
        }

        // Stat +/- buttons: btn_str_right, btn_hp_left, ...
        for (int i = 0; i < StatIds.Length; i++)
        {
            if (sender.Id == $"btn_{StatIds[i]}_right")
            {
                IncreaseStat(i);
                return;
            }

            if (sender.Id == $"btn_{StatIds[i]}_left")
            {
                DecreaseStat(i);
                return;
            }
        }
    }
}
