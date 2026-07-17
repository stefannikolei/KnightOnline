using System.Globalization;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;

namespace OpenKO.Client.Game.Ui;

/// <summary>The seven tooltip text colours (CUIImageTooltipDlg member D3DCOLORs).</summary>
public enum TooltipColor
{
    White,
    Blue,
    Yellow,
    Green,
    Gold,
    Ivory,
    Red,
}

/// <summary>One composed tooltip line (text + colour), the pure output of the compose pass.</summary>
public readonly record struct TooltipLine(string Text, TooltipColor Color);

/// <summary>
/// The player values the tooltip colours its requirement lines against (red when unmet).
/// When null is passed to the compose pass every requirement line stays white (the item is
/// described without a wearer context, e.g. a vendor preview before login).
/// </summary>
public readonly record struct TooltipPlayer(
    byte Race,
    int Level,
    int Rank,
    int Title,
    int Strength,
    int Stamina,
    int Dexterity,
    int Intelligence,
    int MagicAttack,
    int Gold);

/// <summary>
/// Controller for the item image-tooltip — port of <c>CUIImageTooltipDlg</c>
/// (Client/WarFare/UIImageTooltipDlg.cpp). The shipped <c>*_iteminfo_*.uif</c> carries 30
/// <c>string_0..string_29</c> lines and a <c>mins</c> background image; the compose pass
/// (<see cref="Compose"/>) turns a resolved item (basic + ext + durability + count) into the
/// ordered list of (text, colour) lines exactly as <c>CalcTooltipStringNumAndWrite</c> does,
/// and <see cref="Show"/> writes them into the string widgets and positions the dialog near a
/// cursor point. The compose logic is static and headless so it is unit-testable without a
/// device; the control only touches the parsed widget tree.
/// </summary>
public sealed class ItemTooltipControl
{
    /// <summary>MAX_TOOLTIP_COUNT — the string_0..string_29 line budget.</summary>
    public const int MaxLines = 30;

    /// <summary>MIN_WORDS_TO_SPLIT_DESC — remark wraps into two lines at/above this word count.</summary>
    private const int MinWordsToSplitDesc = 5;

    /// <summary>e_ItemAttrib ITEM_ATTRIB_UNIQUE — the header-named, gold-coloured rarity.</summary>
    private const byte AttribUnique = 4;

    /// <summary>e_ItemAttrib ITEM_ATTRIB_UPGRADE.</summary>
    private const byte AttribUpgrade = 5;

    private readonly UiControl _root;
    private readonly UiStringControl?[] _lines = new UiStringControl?[MaxLines];
    private readonly UiControl? _background;

    public ItemTooltipControl(UiControl root)
    {
        _root = root;
        for (int i = 0; i < MaxLines; i++)
            _lines[i] = root.GetChildById<UiStringControl>("string_" + i.ToString(CultureInfo.InvariantCulture));
        _background = root.GetChildById("mins");
        _root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>
    /// Compose the ordered tooltip lines for an item — a faithful port of
    /// <c>CUIImageTooltipDlg::CalcTooltipStringNumAndWrite</c>. Pure: no widget/device access, so
    /// callers (and tests) get the exact line list and colours. <paramref name="player"/> drives
    /// the red/white requirement colouring; pass null to describe the item without a wearer.
    /// </summary>
    public static IReadOnlyList<TooltipLine> Compose(
        ItemBasicRow? basic,
        ItemExtRow? ext,
        int durability,
        int count,
        TooltipPlayer? player = null,
        bool showPrice = false,
        bool isBuy = false,
        bool hasPremium = false)
    {
        var lines = new List<TooltipLine>(MaxLines);
        if (basic == null || ext == null)
            return lines;

        // Gold — a single white "count name" line (the C++ short-circuits here).
        if (basic.AttachPoint == KoItemPosition.Gold)
        {
            lines.Add(new TooltipLine(
                string.Format(CultureInfo.InvariantCulture, "{0}  {1}", count, basic.Name), TooltipColor.White));
            return lines;
        }

        // Name — coloured by rarity; unique uses the ext header, others append "(+N)" enchant.
        TooltipColor nameColor = RarityColor(ext.MagicOrRare);
        string nameText;
        if (ext.MagicOrRare != AttribUnique)
        {
            uint enchant = ext.Id % 10;
            nameText = enchant != 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}(+{1})", basic.Name, enchant)
                : basic.Name;
        }
        else
        {
            nameText = ext.Header;
        }

        lines.Add(new TooltipLine(nameText, nameColor));

        bool countable = basic.Countable;

        // Class of item (weapon/armour/…) — only for non-countable items.
        if (!countable)
            lines.Add(new TooltipLine(ItemClassText(basic.Class), TooltipColor.White));

        // Required race.
        if (basic.NeedRace != 0)
        {
            TooltipColor c = player is { } pr && pr.Race != basic.NeedRace ? TooltipColor.Red : TooltipColor.White;
            lines.Add(new TooltipLine(RaceText(basic.NeedRace), c));
        }

        // Required class — the C++ colours by class-kind membership; the kind mapping is deferred,
        // so the line is described white (the numeric stat/level lines below still colour red).
        if (basic.NeedClass != 0)
            lines.Add(new TooltipLine(ClassText(basic.NeedClass), TooltipColor.White));

        // Damage.
        int damage = basic.Damage + ext.Damage;
        if (damage != 0)
            lines.Add(new TooltipLine(Fmt("Attack Power: {0}", damage), TooltipColor.White));

        // Attack speed bucket (interval * ext percentage / 100).
        float interval = basic.AttackInterval * (ext.AttackIntervalPercentage / 100.0f);
        if (interval != 0)
            lines.Add(new TooltipLine("Attack Speed: " + AttackSpeedText(interval), TooltipColor.White));

        // Attack range (0.1 m units).
        if (basic.AttackRange != 0)
            lines.Add(new TooltipLine(Fmt("Attack Range: {0:0.0}", basic.AttackRange / 10.0f), TooltipColor.White));

        if (ext.HitRate != 0)
            lines.Add(new TooltipLine(Fmt("Hit Rate: +{0}%", ext.HitRate), TooltipColor.White));
        if (ext.EvationRate != 0)
            lines.Add(new TooltipLine(Fmt("Evasion Rate: +{0}%", ext.EvationRate), TooltipColor.White));

        if (basic.Weight != 0)
            lines.Add(new TooltipLine(Fmt("Weight: {0:0.0}", basic.Weight * 0.1f), TooltipColor.White));

        // Durability (only when the item actually wears — max != 1).
        int maxDur = basic.MaxDurability + ext.MaxDurability;
        if (maxDur != 1)
        {
            lines.Add(new TooltipLine(Fmt("Max Durability: {0}", maxDur), TooltipColor.White));
            lines.Add(new TooltipLine(Fmt("Durability: {0}", durability), TooltipColor.White));
        }

        int defense = basic.Defense + ext.Defense;
        if (defense != 0)
            lines.Add(new TooltipLine(Fmt("Defense: {0}", defense), TooltipColor.White));

        // Defense rates (green).
        AddIf(lines, ext.DefenseRateDagger, "Defense vs Dagger: {0}%", TooltipColor.Green);
        AddIf(lines, ext.DefenseRateSword, "Defense vs Sword: {0}%", TooltipColor.Green);
        AddIf(lines, ext.DefenseRateBlow, "Defense vs Blunt: {0}%", TooltipColor.Green);
        AddIf(lines, ext.DefenseRateAxe, "Defense vs Axe: {0}%", TooltipColor.Green);
        AddIf(lines, ext.DefenseRateSpear, "Defense vs Spear: {0}%", TooltipColor.Green);
        AddIf(lines, ext.DefenseRateArrow, "Defense vs Arrow: {0}%", TooltipColor.Green);

        // Elemental attack (green). byStillHP/byDamageMP/byStillMP are absent from the ItemExtRow
        // model and are deferred.
        AddIf(lines, ext.DamageFire, "Fire Damage: {0}", TooltipColor.Green);
        AddIf(lines, ext.DamageIce, "Ice Damage: {0}", TooltipColor.Green);
        AddIf(lines, ext.DamageThunder, "Lightning Damage: {0}", TooltipColor.Green);
        AddIf(lines, ext.DamagePoison, "Poison Damage: {0}", TooltipColor.Green);

        // Stat bonuses (green) — the C++ order: STR, STA, HP, DEX, MSP, INT, magic attack.
        AddIf(lines, ext.BonusStr, "STR +{0}", TooltipColor.Green);
        AddIf(lines, ext.BonusSta, "HP/STA +{0}", TooltipColor.Green);
        AddIf(lines, ext.BonusHP, "HP +{0}", TooltipColor.Green);
        AddIf(lines, ext.BonusDex, "DEX +{0}", TooltipColor.Green);
        AddIf(lines, ext.BonusMSP, "MP/SP +{0}", TooltipColor.Green);
        AddIf(lines, ext.BonusInt, "INT +{0}", TooltipColor.Green);
        AddIf(lines, ext.BonusMagicAttak, "Magic Attack +{0}", TooltipColor.Green);

        // Resistances (green).
        AddIf(lines, ext.RegistFire, "Fire Resistance: +{0}", TooltipColor.Green);
        AddIf(lines, ext.RegistIce, "Ice Resistance: +{0}", TooltipColor.Green);
        AddIf(lines, ext.RegistElec, "Lightning Resistance: +{0}", TooltipColor.Green);
        AddIf(lines, ext.RegistMagic, "Magic Resistance: +{0}", TooltipColor.Green);
        AddIf(lines, ext.RegistPoison, "Poison Resistance: +{0}", TooltipColor.Green);
        AddIf(lines, ext.RegistCurse, "Curse Resistance: +{0}", TooltipColor.Green);

        // Requirements — red when the player fails to meet them.
        int needLevel = basic.NeedLevel + ext.NeedLevel;
        if (needLevel > 1)
            lines.Add(new TooltipLine(Fmt("Required Level: {0}", needLevel), Req(player?.Level, needLevel)));

        int needRank = basic.NeedRank + ext.NeedRank;
        if (needRank > 0)
            lines.Add(new TooltipLine(Fmt("Required Rank: {0}", needRank), Req(player?.Rank, needRank)));

        int needTitle = basic.NeedTitle + ext.NeedTitle;
        if (needTitle > 0)
            lines.Add(new TooltipLine(Fmt("Required Class Rank: {0}", needTitle), Req(player?.Title, needTitle)));

        AddNeed(lines, basic.NeedStrength, ext.NeedStrength, "Required STR: {0}", player?.Strength);
        AddNeed(lines, basic.NeedStamina, ext.NeedStamina, "Required HP/STA: {0}", player?.Stamina);
        AddNeed(lines, basic.NeedDexterity, ext.NeedDexterity, "Required DEX: {0}", player?.Dexterity);
        AddNeed(lines, basic.NeedInteli, ext.NeedInteli, "Required INT: {0}", player?.Intelligence);
        AddNeed(lines, basic.NeedMagicAttack, ext.NeedMagicAttack, "Required MP: {0}", player?.MagicAttack);

        // Remark — centred, white; split into two lines at/above the word threshold.
        if (!string.IsNullOrEmpty(basic.Remark))
            AddRemark(lines, basic.Remark);

        // Rarity footer.
        if (ext.MagicOrRare == AttribUnique)
            lines.Add(new TooltipLine("Unique", TooltipColor.Green));
        else if (ext.MagicOrRare == AttribUpgrade)
        {
            string? grade = GradeText(basic.Grade);
            if (grade != null)
                lines.Add(new TooltipLine(grade, TooltipColor.Green));
        }

        // Price footer (vendor context).
        if (showPrice)
        {
            if (isBuy)
            {
                int price = ItemTableSet.GetBuyPrice(basic, ext);
                TooltipColor c = player is { } pb && pb.Gold < price ? TooltipColor.Red : TooltipColor.White;
                lines.Add(new TooltipLine(Fmt("Purchase Price: {0} Noah", price), c));
            }
            else
            {
                int price = ItemTableSet.GetSellPrice(basic, ext, hasPremium);
                lines.Add(new TooltipLine(Fmt("Sell Price: {0} Noah", price), TooltipColor.White));
            }
        }

        if (lines.Count > MaxLines)
            lines.RemoveRange(MaxLines, lines.Count - MaxLines);

        return lines;
    }

    /// <summary>
    /// Show the tooltip for an item near <paramref name="x"/>/<paramref name="y"/> (the C++
    /// DisplayTooltipsEnable + SetPosSomething). Writes the composed lines into string_0..N and
    /// blanks the rest; the device renderer draws the visible widgets.
    /// </summary>
    public void Show(
        ItemBasicRow? basic,
        ItemExtRow? ext,
        int durability,
        int count,
        int x,
        int y,
        TooltipPlayer? player = null,
        bool showPrice = false,
        bool isBuy = false,
        bool hasPremium = false)
    {
        IReadOnlyList<TooltipLine> composed = Compose(basic, ext, durability, count, player, showPrice, isBuy, hasPremium);
        if (composed.Count == 0)
        {
            Hide();
            return;
        }

        for (int i = 0; i < MaxLines; i++)
        {
            UiStringControl? str = _lines[i];
            if (str == null)
                continue;

            if (i < composed.Count)
            {
                str.Text = composed[i].Text;
                str.ColorArgb = ColorArgb(composed[i].Color);
                str.SetVisible(true);
            }
            else
            {
                str.Text = string.Empty;
                str.SetVisible(false);
            }
        }

        _root.SetPos(x + 26, y);
        _root.SetVisible(true);
    }

    public void Hide() => _root.SetVisible(false);

    // ---- Compose helpers ---------------------------------------------------

    private static void AddIf(List<TooltipLine> lines, int value, string format, TooltipColor color)
    {
        if (value != 0)
            lines.Add(new TooltipLine(Fmt(format, value), color));
    }

    private static void AddNeed(List<TooltipLine> lines, int basicNeed, int extNeed, string format, int? have)
    {
        // The C++ only folds in the ext requirement when the basic requirement is non-zero.
        int need = basicNeed;
        if (need != 0)
            need += extNeed;
        if (need > 0)
            lines.Add(new TooltipLine(Fmt(format, basicNeed + extNeed), Req(have, basicNeed + extNeed)));
    }

    private static void AddRemark(List<TooltipLine> lines, string remark)
    {
        int totalWords = 1;
        foreach (char c in remark)
        {
            if (c == ' ')
                totalWords++;
        }

        if (totalWords >= MinWordsToSplitDesc)
        {
            int wordsInFirstHalf = (totalWords + 1) / 2;
            int wordsSeen = 1;
            int splitPos = -1;
            for (int i = 0; i < remark.Length; i++)
            {
                if (remark[i] != ' ')
                    continue;
                if (++wordsSeen > wordsInFirstHalf)
                {
                    splitPos = i;
                    break;
                }
            }

            if (splitPos < 0)
                splitPos = remark.Length;

            lines.Add(new TooltipLine("*" + remark[..splitPos], TooltipColor.White));
            lines.Add(new TooltipLine(remark[splitPos..] + "*", TooltipColor.White));
        }
        else
        {
            lines.Add(new TooltipLine("*" + remark + "*", TooltipColor.White));
        }
    }

    private static TooltipColor Req(int? have, int need)
        => have is { } h && h < need ? TooltipColor.Red : TooltipColor.White;

    private static string Fmt(string format, object arg)
        => string.Format(CultureInfo.InvariantCulture, format, arg);

    private static TooltipColor RarityColor(byte magicOrRare) => magicOrRare switch
    {
        0 => TooltipColor.White,   // GENERAL
        1 => TooltipColor.Blue,    // MAGIC
        2 => TooltipColor.Yellow,  // LAIR (rare)
        3 => TooltipColor.Green,   // CRAFT
        4 => TooltipColor.Gold,    // UNIQUE
        5 => TooltipColor.Ivory,   // UPGRADE
        _ => TooltipColor.White,
    };

    private static string AttackSpeedText(float interval) => interval switch
    {
        <= 89 => "Very Fast",
        <= 110 => "Fast",
        <= 130 => "Normal",
        <= 150 => "Slow",
        _ => "Very Slow",
    };

    private static string? GradeText(byte grade) => grade switch
    {
        1 => "Low Class",
        2 => "Middle Class",
        3 => "High Class",
        _ => null,
    };

    private static string ItemClassText(byte itemClass)
        => "Type " + itemClass.ToString(CultureInfo.InvariantCulture);

    private static string RaceText(byte race) => race switch
    {
        1 => "Karus Only",
        2 => "El Morad Only",
        _ => "Race " + race.ToString(CultureInfo.InvariantCulture),
    };

    private static string ClassText(byte needClass)
        => "Class " + needClass.ToString(CultureInfo.InvariantCulture);

    // ARGB colours matching the CUIImageTooltipDlg member D3DCOLORs.
    private static uint ColorArgb(TooltipColor c) => c switch
    {
        TooltipColor.White => 0xFFFFFFFF,
        TooltipColor.Blue => 0xFF8080FF,
        TooltipColor.Yellow => 0xFFFFFF00,
        TooltipColor.Green => 0xFF80FF00,
        TooltipColor.Gold => 0xFFDCC77C,
        TooltipColor.Ivory => 0xFFC87CC7,
        TooltipColor.Red => 0xFFFF3C3C,
        _ => 0xFFFFFFFF,
    };
}
