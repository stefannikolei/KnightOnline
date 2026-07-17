using System.Globalization;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// The composed repair-tooltip lines, one per widget slot. A null field means the widget is
/// hidden; the "cannot repair" case fills only <see cref="Title"/>.
/// </summary>
public readonly record struct RepairTooltipData(
    TooltipLine? RepairGold,
    TooltipLine? DurMax,
    TooltipLine? DurCurrent,
    TooltipLine? Title);

/// <summary>
/// Controller for the repair image-tooltip — port of <c>CUIRepairTooltipDlg</c>
/// (Client/WarFare/UIRepairTooltipDlg.cpp, MAX_REPAIR_TOOLTIP_COUNT = 4). The shipped
/// <c>*_repairtooltip_*.uif</c> carries <c>string_repairgold</c>, <c>string_dur_max</c>,
/// <c>string_dur_current</c> and <c>string_title</c>. A repairable item shows the required
/// gold (red when the player cannot afford it), max/current durability and a repair label;
/// a countable or non-wearing (max durability == 1) item collapses to a single
/// "cannot repair" title line. The compose pass is static and headless.
/// </summary>
public sealed class RepairTooltipControl
{
    private readonly UiControl _root;
    private readonly UiStringControl? _repairGold;
    private readonly UiStringControl? _durMax;
    private readonly UiStringControl? _durCurrent;
    private readonly UiStringControl? _title;

    public RepairTooltipControl(UiControl root)
    {
        _root = root;
        _repairGold = root.GetChildById<UiStringControl>("string_repairgold");
        _durMax = root.GetChildById<UiStringControl>("string_dur_max");
        _durCurrent = root.GetChildById<UiStringControl>("string_dur_current");
        _title = root.GetChildById<UiStringControl>("string_title");
        _root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>
    /// Compose the repair-tooltip lines — a faithful port of
    /// <c>CUIRepairTooltipDlg::DisplayTooltipsEnable</c>. Pure: returns the four widget-slot
    /// lines (or nulls) so callers/tests see the exact text and colours.
    /// </summary>
    public static RepairTooltipData Compose(
        ItemBasicRow? basic, ItemExtRow? ext, int durability, int requiredGold, bool haveEnough)
    {
        if (basic == null || ext == null)
            return default;

        bool repairable = !basic.Countable && basic.MaxDurability + ext.MaxDurability != 1;
        if (!repairable)
            return new RepairTooltipData(null, null, null, new TooltipLine("Cannot Repair", TooltipColor.White));

        int maxDur = basic.MaxDurability + ext.MaxDurability;
        var gold = new TooltipLine(
            string.Format(CultureInfo.InvariantCulture, "Repair Cost: {0} Noah", requiredGold),
            haveEnough ? TooltipColor.White : TooltipColor.Red);

        return new RepairTooltipData(
            gold,
            new TooltipLine(string.Format(CultureInfo.InvariantCulture, "Max Durability: {0}", maxDur), TooltipColor.White),
            new TooltipLine(string.Format(CultureInfo.InvariantCulture, "Durability: {0}", durability), TooltipColor.White),
            new TooltipLine("Repair", TooltipColor.White));
    }

    /// <summary>Show the repair tooltip near a cursor point (DisplayTooltipsEnable).</summary>
    public void Show(
        ItemBasicRow? basic, ItemExtRow? ext, int durability, int requiredGold, bool haveEnough, int x, int y)
    {
        if (basic == null || ext == null)
        {
            Hide();
            return;
        }

        RepairTooltipData data = Compose(basic, ext, durability, requiredGold, haveEnough);
        Apply(_repairGold, data.RepairGold);
        Apply(_durMax, data.DurMax);
        Apply(_durCurrent, data.DurCurrent);
        Apply(_title, data.Title);

        int height = _root.Height;
        _root.SetPos(x + 26, y - height);
        _root.SetVisible(true);
    }

    public void Hide() => _root.SetVisible(false);

    private static void Apply(UiStringControl? widget, TooltipLine? line)
    {
        if (widget == null)
            return;
        if (line is { } l)
        {
            widget.Text = l.Text;
            widget.ColorArgb = ColorArgb(l.Color);
            widget.SetVisible(true);
        }
        else
        {
            widget.Text = string.Empty;
            widget.SetVisible(false);
        }
    }

    private static uint ColorArgb(TooltipColor c) => c switch
    {
        TooltipColor.Red => 0xFFDD0000,
        _ => 0xFFFFFFFF,
    };
}
