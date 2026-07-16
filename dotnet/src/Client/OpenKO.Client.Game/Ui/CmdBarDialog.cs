using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the command bar HUD — port of <c>CUICmd</c> (Client/WarFare/UICmd.cpp).
/// Deliberately a thin event surface: rather than hardwiring each button to a game action
/// (walk/run/attack/inventory/…), it raises <see cref="Command"/> with the pressed button's
/// id so the executable / later slices decide the behavior.
/// </summary>
public sealed class CmdBarDialog
{
    /// <summary>The command button ids resolved from the layout (CUICmd::Load).</summary>
    public static readonly string[] ButtonIds =
    [
        "btn_walk", "btn_run", "btn_attack", "btn_sit", "btn_stand",
        "btn_character", "btn_inventory", "btn_option", "btn_camera",
        "btn_invite", "btn_disband", "btn_skill", "btn_exit", "btn_map",
    ];

    private readonly UiControl _root;
    private readonly Dictionary<UiControl, string> _buttons = new();

    /// <summary>Raised with the pressed button's id (e.g. "btn_inventory").</summary>
    public event Action<string>? Command;

    public UiControl Root => _root;

    public CmdBarDialog(GameContext context, UiControl root)
    {
        _root = root;
        foreach (string id in ButtonIds)
        {
            if (root.GetChildById<UiButton>(id) is { } button)
                _buttons[button] = id;
        }

        root.Message += OnMessage;
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg == UiMsg.ButtonClick && _buttons.TryGetValue(sender, out string? id))
            Command?.Invoke(id);
    }
}
