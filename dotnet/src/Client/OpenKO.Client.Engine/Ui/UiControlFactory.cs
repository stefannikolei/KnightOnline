using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Builds a runtime <see cref="UiControl"/> tree from a parsed <see cref="N3UiBase"/>
/// layout tree, mapping each widget type to its interactive control (buttons/lists/
/// scrollbars get their state machines; the rest use the generic control).
/// </summary>
public static class UiControlFactory
{
    public static UiControl Build(N3UiBase node)
    {
        UiControl control = CreateFor(node);
        foreach (N3UiBase childNode in node.Children)
            control.AddChild(Build(childNode));
        return control;
    }

    private static UiControl CreateFor(N3UiBase node) => node switch
    {
        N3UiButton button => new UiButton(button),
        N3UiList list => new UiListControl(list),
        N3UiScrollBar scroll => new UiScrollBarControl(scroll),
        N3UiEdit edit => new UiEditControl(edit),
        N3UiString str => new UiStringControl(str),
        // N3UiIcon derives from N3UiImage, so it must be matched before any N3UiImage arm.
        N3UiIcon icon => new UiIconControl(icon),
        N3UiArea area => new UiAreaControl(area),
        N3UiTooltip tip => new UiTooltipControl(tip),
        // IconManager/IconSlot carry no behaviour of their own yet — the generic control
        // (their layout data lives on the node) is sufficient until a dialog needs more.
        _ => new UiControl(node),
    };
}
