using System.Runtime.InteropServices;

namespace OpenKO.Client.Assets;

/// <summary>eUI_TYPE (N3UIDef.h).</summary>
public enum N3UiType
{
    Base = 0,
    Button = 1,
    Static = 2,
    Progress = 3,
    Image = 4,
    ScrollBar = 5,
    String = 6,
    TrackBar = 7,
    Edit = 8,
    Area = 9,
    Tooltip = 10,
    Icon = 11,
    IconManager = 12,
    IconSlot = 13,
    List = 14,
}

/// <summary>Win32 RECT — four int32s.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3UiRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

/// <summary>UV rect (__FLOAT_RECT) — four floats.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3UiRectF
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;
}

/// <summary>
/// Port of <c>CN3UIBase::Load</c> (Client/N3Base/N3UIBase.cpp) — the .uif
/// widget tree: recursively typed children first, then the widget's own id,
/// region/movable rects, style, reserved, tooltip and open/close sounds.
/// The &gt;= 1264 format stores the child count as int16 plus an unused int16.
/// </summary>
public class N3UiBase : N3BaseFile
{
    public virtual N3UiType UiType => N3UiType.Base;

    public List<N3UiBase> Children { get; } = [];

    public string Id { get; set; } = string.Empty;

    public N3UiRect Region { get; set; }

    public N3UiRect Movable { get; set; }

    public uint Style { get; set; }

    public uint Reserved { get; set; }

    public string ToolTip { get; set; } = string.Empty;

    public string OpenSoundFileName { get; set; } = string.Empty;

    public string CloseSoundFileName { get; set; } = string.Empty;

    /// <summary>The child factory switch in CN3UIBase::Load (non-_REPENT build).</summary>
    public static N3UiBase CreateByType(N3UiType type) => type switch
    {
        N3UiType.Base => new N3UiBase(),
        N3UiType.Image => new N3UiImage(),
        N3UiType.String => new N3UiString(),
        N3UiType.Button => new N3UiButton(),
        N3UiType.Static => new N3UiStatic(),
        N3UiType.Progress => new N3UiProgress(),
        N3UiType.ScrollBar => new N3UiScrollBar(),
        N3UiType.TrackBar => new N3UiTrackBar(),
        N3UiType.Edit => new N3UiEdit(),
        N3UiType.Area => new N3UiArea(),
        N3UiType.Tooltip => new N3UiTooltip(),
        N3UiType.Icon => new N3UiIcon(),
        N3UiType.IconManager => new N3UiIconMng(),
        N3UiType.IconSlot => new N3UiIconSlot(),
        N3UiType.List => new N3UiList(),
        _ => throw new InvalidDataException($"Unknown UI type {type}"), // C++ asserts and crashes here
    };

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        int childCount;
        if (FileFormatVersion >= N3FormatVersion.V1264)
        {
            childCount = reader.ReadInt16();
            reader.ReadInt16(); // sIdk0 — unused
        }
        else
        {
            childCount = reader.ReadInt32();
        }

        // CN3UIBase::AddChild does m_Children.push_front(pChild) (N3UIBase.h), so the
        // in-memory list ends up in REVERSE file order — the last widget written is
        // Children[0]. Render()/MouseProc() walk this list back-to-front
        // (rbegin→rend / forward respectively), so Children[0] ends up drawn last —
        // i.e. on top. Insert-at-front here mirrors that; a plain Add (append) would
        // leave later-authored widgets (like an account-panel group appended after
        // its background tiles) buried under the earlier ones instead of layered
        // on top of them.
        Children.Clear();
        for (int i = 0; i < childCount; i++)
        {
            var type = (N3UiType)reader.ReadInt32();
            N3UiBase child = CreateByType(type);
            child.FileFormatVersion = FileFormatVersion;
            Children.Insert(0, child);
            child.Load(reader);
        }

        Id = reader.ReadN3FileName();
        Region = reader.ReadStruct<N3UiRect>();
        Movable = reader.ReadStruct<N3UiRect>();
        Style = reader.ReadUInt32();
        Reserved = reader.ReadUInt32();
        ToolTip = reader.ReadN3FileName();
        OpenSoundFileName = reader.ReadN3FileName();
        CloseSoundFileName = reader.ReadN3FileName();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        if (FileFormatVersion >= N3FormatVersion.V1264)
        {
            writer.Write((short)Children.Count);
            writer.Write((short)0);
        }
        else
        {
            writer.Write(Children.Count);
        }

        // Mirror image of the Load()-side push_front: write back-to-front so a
        // Save() → Load() round trip restores the same in-memory Children order.
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            N3UiBase child = Children[i];
            writer.Write((int)child.UiType);
            child.FileFormatVersion = FileFormatVersion;
            child.Save(writer);
        }

        writer.WriteN3FileName(Id);
        writer.WriteStruct(Region);
        writer.WriteStruct(Movable);
        writer.Write(Style);
        writer.Write(Reserved);
        writer.WriteN3FileName(ToolTip);
        writer.WriteN3FileName(OpenSoundFileName);
        writer.WriteN3FileName(CloseSoundFileName);
    }
}

/// <summary>CN3UIImage: texture name, UV rect, animation frame rate.</summary>
public class N3UiImage : N3UiBase
{
    public override N3UiType UiType => N3UiType.Image;

    public string TexFileName { get; set; } = string.Empty;

    public N3UiRectF UvRect { get; set; }

    public float AnimFrame { get; set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);
        TexFileName = reader.ReadN3FileName();
        UvRect = reader.ReadStruct<N3UiRectF>();
        AnimFrame = reader.ReadSingle();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);
        writer.WriteN3FileName(TexFileName);
        writer.WriteStruct(UvRect);
        writer.Write(AnimFrame);
    }
}

/// <summary>CN3UIString: font (name/height/flags), color, text, 1264+ extra int.</summary>
public sealed class N3UiString : N3UiBase
{
    public override N3UiType UiType => N3UiType.String;

    public string FontName { get; set; } = string.Empty;

    public uint FontHeight { get; set; }

    public uint FontFlags { get; set; }

    public uint Color { get; set; }

    public string Text { get; set; } = string.Empty;

    public int Idk0 { get; set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        FontName = reader.ReadN3FileName();
        if (FontName.Length > 0)
        {
            FontHeight = reader.ReadUInt32();
            FontFlags = reader.ReadUInt32();
        }

        Color = reader.ReadUInt32();
        Text = reader.ReadN3FileName();

        if (FileFormatVersion >= N3FormatVersion.V1264)
            Idk0 = reader.ReadInt32();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.WriteN3FileName(FontName);
        if (FontName.Length > 0)
        {
            writer.Write(FontHeight);
            writer.Write(FontFlags);
        }

        writer.Write(Color);
        writer.WriteN3FileName(Text);

        if (FileFormatVersion >= N3FormatVersion.V1264)
            writer.Write(Idk0);
    }
}

/// <summary>CN3UIButton: click rect plus click/over sounds. Button-state images are children.</summary>
public sealed class N3UiButton : N3UiBase
{
    public override N3UiType UiType => N3UiType.Button;

    public N3UiRect ClickRect { get; set; }

    public string ClickSoundFileName { get; set; } = string.Empty;

    public string OverSoundFileName { get; set; } = string.Empty;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);
        ClickRect = reader.ReadStruct<N3UiRect>();
        ClickSoundFileName = reader.ReadN3FileName();
        OverSoundFileName = reader.ReadN3FileName();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);
        writer.WriteStruct(ClickRect);
        writer.WriteN3FileName(ClickSoundFileName);
        writer.WriteN3FileName(OverSoundFileName);
    }
}

/// <summary>CN3UIStatic: one click sound; image/text are children.</summary>
public class N3UiStatic : N3UiBase
{
    public override N3UiType UiType => N3UiType.Static;

    public string ClickSoundFileName { get; set; } = string.Empty;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);
        ClickSoundFileName = reader.ReadN3FileName();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);
        writer.WriteN3FileName(ClickSoundFileName);
    }
}

/// <summary>CN3UIEdit: a static plus the typing sound.</summary>
public sealed class N3UiEdit : N3UiStatic
{
    public override N3UiType UiType => N3UiType.Edit;

    public string TypingSoundFileName { get; set; } = string.Empty;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);
        TypingSoundFileName = reader.ReadN3FileName();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);
        writer.WriteN3FileName(TypingSoundFileName);
    }
}

/// <summary>CN3UIProgress: no own fields — bkgnd/frgnd images are children.</summary>
public sealed class N3UiProgress : N3UiBase
{
    public override N3UiType UiType => N3UiType.Progress;
}

/// <summary>CN3UIScrollBar: no own fields — trackbar/buttons are children.</summary>
public sealed class N3UiScrollBar : N3UiBase
{
    public override N3UiType UiType => N3UiType.ScrollBar;
}

/// <summary>CN3UITrackBar: no own fields — bkgnd/thumb images are children.</summary>
public sealed class N3UiTrackBar : N3UiBase
{
    public override N3UiType UiType => N3UiType.TrackBar;
}

/// <summary>
/// eUI_AREA_TYPE (Client/N3Base/N3UIArea.h) — the semantic role of a
/// <see cref="N3UiArea"/> slot region within an icon-manager window.
/// </summary>
public enum UiAreaType
{
    None = 0,
    Slot = 1,
    Inv = 2,
    TradeNpc = 3,
    PerTradeMy = 4,
    PerTradeOther = 5,
    DropItem = 6,
    SkillTree = 7,
    SkillHotkey = 8,
    RepairInv = 9,
    RepairNpc = 10,
    TradeMy = 11,
    PerTradeInv = 12,
}

/// <summary>CN3UIArea: the area type int (non-_REPENT build).</summary>
public sealed class N3UiArea : N3UiBase
{
    public override N3UiType UiType => N3UiType.Area;

    public int AreaType { get; set; }

    /// <summary>Strongly-typed view of <see cref="AreaType"/> (eUI_AREA_TYPE).</summary>
    public UiAreaType AreaTypeEnum => (UiAreaType)AreaType;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);
        AreaType = reader.ReadInt32();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);
        writer.Write(AreaType);
    }
}

/// <summary>
/// CN3UIIcon (Client/WarFare/N3UIIcon.{h,cpp}) — a draggable item/skill icon.
/// The class derives from <c>CN3UIImage</c> and adds no <c>Load</c> override, so the
/// serialized layout is byte-identical to <see cref="N3UiImage"/> (texture, UV rect,
/// animation frame). The runtime drag/hit-test behaviour lives in the Engine control.
/// </summary>
public sealed class N3UiIcon : N3UiImage
{
    public override N3UiType UiType => N3UiType.Icon;
}

/// <summary>
/// CN3UITooltip (Client/N3Base/N3UITooltip.{h,cpp}) — derives from <c>CN3UIStatic</c>
/// with no <c>Load</c> override, so its serialized layout is byte-identical to
/// <see cref="N3UiStatic"/> (one click-sound name; background/text are children).
/// </summary>
public sealed class N3UiTooltip : N3UiStatic
{
    public override N3UiType UiType => N3UiType.Tooltip;
}

/// <summary>
/// CN3UIIconManager (UI_TYPE_ICON_MANAGER) — the icon-owning window role. No dedicated
/// C++ class survives (the factory branch is commented out); it carries no fields beyond
/// <see cref="N3UiBase"/>, so it parses exactly like the base node.
/// </summary>
public sealed class N3UiIconMng : N3UiBase
{
    public override N3UiType UiType => N3UiType.IconManager;
}

/// <summary>
/// CN3UIIconSlot (UI_TYPE_ICONSLOT) — a _REPENT-only node whose C++ source is absent
/// from this tree. It adds no observed fields beyond <see cref="N3UiBase"/>, so it parses
/// like the base node; revisit if a _REPENT corpus ever surfaces extra bytes.
/// </summary>
public sealed class N3UiIconSlot : N3UiBase
{
    public override N3UiType UiType => N3UiType.IconSlot;
}

/// <summary>CN3UIList: list font block (name, height, color, bold, italic as 4-byte BOOLs).</summary>
public sealed class N3UiList : N3UiBase
{
    public override N3UiType UiType => N3UiType.List;

    public string FontName { get; set; } = string.Empty;

    public uint FontHeight { get; set; }

    public uint FontColor { get; set; }

    public bool FontBold { get; set; }

    public bool FontItalic { get; set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        FontName = reader.ReadN3FileName();
        if (FontName.Length > 0)
        {
            FontHeight = reader.ReadUInt32();
            FontColor = reader.ReadUInt32();
            FontBold = reader.ReadInt32() != 0;
            FontItalic = reader.ReadInt32() != 0;
        }
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.WriteN3FileName(FontName);
        if (FontName.Length > 0)
        {
            writer.Write(FontHeight);
            writer.Write(FontColor);
            writer.Write(FontBold ? 1 : 0);
            writer.Write(FontItalic ? 1 : 0);
        }
    }
}
