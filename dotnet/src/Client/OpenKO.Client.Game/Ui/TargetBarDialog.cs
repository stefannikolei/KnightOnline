using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the target health bar — port of <c>CUITargetBar</c>
/// (Client/WarFare/UITargetBar.cpp). Resolves the target HP progress bar
/// (<c>pro_target</c>) and the name label (<c>text_target</c>). Hidden until a target
/// is set; the HP percentage is recorded (no runtime progress widget yet — see
/// <see cref="StateBarDialog.SetProgress"/>).
/// </summary>
public sealed class TargetBarDialog
{
    private readonly UiControl _root;
    private readonly UiControl? _progress;
    private readonly UiStringControl? _text;

    public UiControl Root => _root;

    /// <summary>Last target HP fill percentage (0..100).</summary>
    public int HpPercent { get; private set; }

    public TargetBarDialog(GameContext context, UiControl root)
    {
        _root = root;
        _progress = root.GetChildById("pro_target");
        _text = root.GetChildById<UiStringControl>("text_target");
        _root.SetVisible(false);
    }

    /// <summary>
    /// Convenience wiring: route WIZ_TARGET_HP into <see cref="UpdateHp"/>. The glue task
    /// may instead call the public methods directly (TargetHpReceived is single-assignment
    /// on <see cref="InGameState"/> and may be owned by the executable).
    /// </summary>
    public void Bind(InGameState inGame)
    {
        inGame.TargetHpReceived = update => UpdateHp(update.Hp, update.MaxHp);
    }

    /// <summary>Show the bar for a named target (CUITargetBar::SetIDString + SetVisible).</summary>
    public void SetTarget(string name)
    {
        if (_text != null)
            _text.Text = name;
        _root.SetVisible(true);
    }

    /// <summary>CUITargetBar::UpdateHP — record the fill percent (0 when HP invalid).</summary>
    public void UpdateHp(int hp, int max)
    {
        if (hp < 0 || max <= 0)
            return;
        HpPercent = hp * 100 / max;
    }

    /// <summary>Hide the bar (no current target).</summary>
    public void Clear() => _root.SetVisible(false);
}
