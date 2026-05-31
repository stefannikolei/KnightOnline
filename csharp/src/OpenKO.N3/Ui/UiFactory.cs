namespace OpenKO.N3;

/// <summary>
/// Creates the right <see cref="N3UIBase"/> subclass for a serialized <see cref="UiType"/>
/// (port of the switch in <c>CN3UIBase::Load</c>).
///
/// Because every control reads a fixed byte run from the stream, a type whose reader is not yet
/// ported cannot be skipped safely — doing so would desynchronise the shared file cursor and corrupt
/// every subsequent control. So unported types throw a clear <see cref="NotSupportedException"/>
/// rather than silently producing garbage; this is by design until each control's Load is ported.
///
/// This list matches the child-type switch in <c>CN3UIBase::Load</c> exactly. Note that
/// <c>UI_TYPE_ICON</c> and <c>UI_TYPE_ICON_MANAGER</c> are never serialised as children of a
/// ".uif" file — the original engine creates those at runtime from game dialogs (item/skill bars),
/// and its loader has no case for them (it would hit <c>__ASSERT(pChild)</c>). <c>UI_TYPE_ICONSLOT</c>
/// is only handled under the <c>_REPENT</c> build. All three therefore belong to the later game-UI
/// layer (Client/WarFare/), not the engine-level loader, so they intentionally throw here.
/// </summary>
public static class UiFactory
{
    public static N3UIBase Create(UiType type) => type switch
    {
        UiType.Base       => new N3UIBase(),
        UiType.Button     => new N3UIButton(),
        UiType.Static     => new N3UIStatic(),
        UiType.Progress   => new N3UIProgress(),
        UiType.Image      => new N3UIImage(),
        UiType.ScrollBar  => new N3UIScrollBar(),
        UiType.String     => new N3UIString(),
        UiType.TrackBar   => new N3UITrackBar(),
        UiType.Edit       => new N3UIEdit(),
        UiType.Area       => new N3UIArea(),
        UiType.Tooltip    => new N3UITooltip(),
        UiType.List       => new N3UIList(),
        _ => throw new NotSupportedException(
            $"UI control type '{type}' is not yet ported. Its Load() must be implemented before " +
            ".uif files containing it can be parsed (skipping it would desync the file stream)."),
    };
}
