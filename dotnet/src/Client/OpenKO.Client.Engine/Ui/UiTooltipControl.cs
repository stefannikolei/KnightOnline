using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime tooltip (port of <c>CN3UITooltip</c>). During MouseProc the control under
/// the cursor calls <see cref="SetText"/>; the manager clears it each frame before
/// dispatch, so only the hovered control's tooltip survives.
/// </summary>
public sealed class UiTooltipControl : UiControl
{
    public UiTooltipControl(N3UiBase node) : base(node)
    {
    }

    public string Text { get; private set; } = string.Empty;

    public void SetText(string text) => Text = text ?? string.Empty;

    public void Clear() => Text = string.Empty;
}
