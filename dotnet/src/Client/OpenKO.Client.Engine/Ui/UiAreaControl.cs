using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Runtime slot region — port of <c>CN3UIArea</c> (Client/N3Base/N3UIArea.cpp). An area is a
/// non-drawing hit region tagged with a <see cref="UiAreaType"/> (its role: inventory slot,
/// trade slot, skill tree, …) and an integer <see cref="Order"/> (parsed from the node id, the
/// slot's decimal position). Icon windows resolve a drop target with
/// <see cref="UiControl.GetChildAreaByOrder"/>. Hit-testing uses the inherited
/// <see cref="UiControl.IsIn"/> (CN3UIBase::IsIn — inclusive edges).
///
/// Pure/headless.
/// </summary>
public sealed class UiAreaControl : UiControl
{
    public UiAreaControl(N3UiArea node) : base(node)
    {
        AreaType = node.AreaTypeEnum;
        Order = int.TryParse(node.Id, out int order) ? order : -1;
    }

    /// <summary>eUI_AREA_TYPE — the semantic role of this slot region.</summary>
    public UiAreaType AreaType { get; }

    /// <summary>
    /// The slot's decimal order (the node's m_szID), or -1 when the id is not numeric.
    /// </summary>
    public int Order { get; }
}
