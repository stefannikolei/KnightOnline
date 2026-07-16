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
        _ => new UiControl(node),
    };
}
