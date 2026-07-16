using System.Globalization;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the player state bar HUD — port of <c>CUIStateBar</c>
/// (Client/WarFare/UIStateBar.cpp). Resolves the HP/MP/EXP progress bars and their
/// text labels plus the position readout, and mirrors the original UpdateHP/UpdateMSP/
/// UpdateExp/UpdatePosition formatting.
///
/// The runtime has no progress-bar widget yet (N3UiProgress maps to a plain
/// <see cref="UiControl"/>), so <see cref="SetProgress"/> records the 0..100 percent
/// against the control rather than driving a fill; the percentages are exposed for the
/// renderer/tests. The minimap (Group_MiniMap) and duration buff icons are deferred to
/// a later slice and left hidden.
/// </summary>
public sealed class StateBarDialog
{
    private readonly UiControl _root;
    private readonly UiControl? _progressHp;
    private readonly UiControl? _progressMsp;
    private readonly UiControl? _progressExpC;
    private readonly UiControl? _progressExpP;
    private readonly UiStringControl? _textHp;
    private readonly UiStringControl? _textMsp;
    private readonly UiStringControl? _textExpP;
    private readonly UiStringControl? _textPosition;
    private readonly UiControl? _groupMiniMap;

    // N3UiProgress has no runtime fill API — record the last percent per bar so the
    // renderer/tests can read it. Stubbed pending a real progress widget.
    private readonly Dictionary<UiControl, int> _progress = new();

    public UiControl Root => _root;

    /// <summary>Last HP fill percentage (0..100).</summary>
    public int HpPercent { get; private set; }

    /// <summary>Last MP fill percentage (0..100).</summary>
    public int MpPercent { get; private set; }

    /// <summary>Last "current level segment" EXP fill percentage (Progress_ExpC).</summary>
    public int ExpCPercent { get; private set; }

    /// <summary>Last overall EXP fill percentage (Progress_ExpP).</summary>
    public int ExpPercent { get; private set; }

    public StateBarDialog(GameContext context, UiControl root)
    {
        _root = root;
        _progressHp = root.GetChildById("Progress_HP");
        _progressMsp = root.GetChildById("Progress_MSP");
        _progressExpC = root.GetChildById("Progress_ExpC");
        _progressExpP = root.GetChildById("Progress_ExpP");
        _textHp = root.GetChildById<UiStringControl>("Text_HP");
        _textMsp = root.GetChildById<UiStringControl>("Text_MSP");
        _textExpP = root.GetChildById<UiStringControl>("Text_ExpP");
        _textPosition = root.GetChildById<UiStringControl>("Text_Position");

        // Minimap + buff icons deferred to a later slice; keep the group hidden.
        _groupMiniMap = root.GetChildById("Group_MiniMap");
        _groupMiniMap?.SetVisible(false);
    }

    /// <summary>
    /// Convenience wiring: assign the in-game hooks so the bars refresh from the world
    /// state. The glue task may instead call the public Update* / <see cref="Fill"/>
    /// methods directly (these hooks are single-assignment on <see cref="InGameState"/>).
    /// </summary>
    public void Bind(InGameState inGame)
    {
        inGame.MyInfoReceived = Fill;
        inGame.HpChanged = (max, hp) => UpdateHp(hp, max);
    }

    /// <summary>WIZ_MYINFO — populate every bar/label from the local player block.</summary>
    public void Fill(LocalPlayer player)
    {
        UpdateHp(player.Hp, player.MaxHp);
        UpdateMp(player.Mp, player.MaxMp);
        UpdateExp(player.Exp, player.MaxExp);
        UpdatePosition(player.X, player.Z);
    }

    /// <summary>CUIStateBar::UpdateHP.</summary>
    public void UpdateHp(int hp, int max)
    {
        if (max <= 0)
            return;
        HpPercent = 100 * hp / max;
        SetProgress(_progressHp, HpPercent);
        if (_textHp != null)
            _textHp.Text = $"{hp} / {max}";
    }

    /// <summary>CUIStateBar::UpdateMSP (the label id is Text_MSP).</summary>
    public void UpdateMp(int mp, int max)
    {
        if (max <= 0)
            return;
        MpPercent = 100 * mp / max;
        SetProgress(_progressMsp, MpPercent);
        if (_textMsp != null)
            _textMsp.Text = $"{mp} / {max}";
    }

    /// <summary>CUIStateBar::UpdateExp — the overall (ExpP) and current-segment (ExpC) bars.</summary>
    public void UpdateExp(uint exp, uint max)
    {
        if (max == 0)
            return;

        ExpPercent = (int)(100.0 * exp / max);
        SetProgress(_progressExpP, ExpPercent);

        if (max > 10)
        {
            uint segment = max / 10;
            uint within = exp % segment;
            ExpCPercent = (int)(100 * within / segment);
        }
        else
        {
            ExpCPercent = 0;
        }

        SetProgress(_progressExpC, ExpCPercent);

        if (_textExpP != null)
        {
            double percent = 100.0 * exp / max;
            _textExpP.Text = string.Format(CultureInfo.InvariantCulture, "{0:F2} %", percent);
        }
    }

    /// <summary>CUIStateBar::UpdatePosition — "x, z" (one decimal, invariant).</summary>
    public void UpdatePosition(float x, float z)
    {
        if (_textPosition != null)
            _textPosition.Text = string.Format(CultureInfo.InvariantCulture, "{0:F1}, {1:F1}", x, z);
    }

    /// <summary>
    /// Record a bar's fill percentage. There is no runtime progress widget yet, so this
    /// stores the 0..100 value against the control instead of resizing a fill region.
    /// </summary>
    public void SetProgress(UiControl? bar, int percent)
    {
        if (bar == null)
            return;
        _progress[bar] = Math.Clamp(percent, 0, 100);
    }

    /// <summary>The recorded fill percentage for a bar (0 when never set).</summary>
    public int GetProgress(UiControl? bar) =>
        bar != null && _progress.TryGetValue(bar, out int v) ? v : 0;
}
