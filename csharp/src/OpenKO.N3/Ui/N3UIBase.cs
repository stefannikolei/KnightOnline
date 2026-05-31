using OpenKO.IO;
using OpenKO.Numerics;

namespace OpenKO.N3;

/// <summary>
/// Port of the C++ <c>CN3UIBase</c> (Client/N3Base/N3UIBase.cpp) — the base of KO's retained-mode
/// UI tree, loaded from ".uif" files. This headless port covers the <b>data model and file format</b>:
/// the control hierarchy, region/movable rectangles, style, id, tooltip and the names of the open/
/// close sounds. Rendering and mouse/keyboard interaction are layered on later (the renderer turns
/// these into <see cref="N3UIImage"/> quads, etc.).
///
/// On-disk format (after the base resource-name header from <see cref="N3BaseFileAccess"/>):
/// <code>
///   childCount      // int32, OR (for format >= 1264) int16 count + int16 padding
///   child[childCount]:
///       int32 uiType        // eUI_TYPE; selects the concrete subclass
///       &lt;that subclass's serialized data, recursively&gt;
///   int32 idLen; byte id[idLen]
///   RECT  rcRegion          // 4x int32: left, top, right, bottom
///   RECT  rcMovable         // 4x int32
///   uint32 style
///   uint32 reserved
///   int32 tooltipLen; byte tooltip[tooltipLen]
///   int32 sndOpenLen;  byte sndOpen[sndOpenLen]
///   int32 sndCloseLen; byte sndClose[sndCloseLen]
/// </code>
/// </summary>
public class N3UIBase : N3BaseFileAccess
{
    public UiType Type { get; protected set; } = UiType.Base;

    public string Id { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;

    /// <summary>Screen-space region (absolute, not relative to parent) — port of <c>m_rcRegion</c>.</summary>
    public Rect Region { get; set; }

    /// <summary>Draggable region — port of <c>m_rcMovable</c>.</summary>
    public Rect Movable { get; set; }

    public uint Style { get; set; }
    public uint Reserved { get; set; }

    public string OpenSound { get; protected set; } = string.Empty;
    public string CloseSound { get; protected set; } = string.Empty;

    public N3UIBase? Parent { get; private set; }

    private readonly List<N3UIBase> _children = new();
    public IReadOnlyList<N3UIBase> Children => _children;

    public int Width => Region.Right - Region.Left;
    public int Height => Region.Bottom - Region.Top;

    public void AddChild(N3UIBase child)
    {
        child.Parent = this;
        // The original push_front()s children; preserve that order so indices/serialization match.
        _children.Insert(0, child);
    }

    /// <summary>Depth-first search for the first descendant with the given id.</summary>
    public N3UIBase? FindById(string id)
    {
        foreach (N3UIBase child in _children)
        {
            if (child.Id == id)
                return child;

            N3UIBase? found = child.FindById(id);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>Typed variant of <see cref="FindById(string)"/>.</summary>
    public T? FindById<T>(string id) where T : N3UIBase => FindById(id) as T;

    public override void Release()
    {
        base.Release();
        _children.Clear();
        Parent = null;
        Id = string.Empty;
        Tooltip = string.Empty;
        OpenSound = string.Empty;
        CloseSound = string.Empty;
        Region = default;
        Movable = default;
        Style = 0;
        Reserved = 0;
    }

    public override bool Load(IFile file)
    {
        var reader = file as FileReader
            ?? throw new ArgumentException("N3UIBase.Load requires a FileReader", nameof(file));

        base.Load(file); // resource name header

        // child count
        int childCount;
        if ((uint)FileFormatVersion >= (uint)N3FormatVersion.V1264)
        {
            short sCC = reader.ReadInt16();
            reader.ReadInt16(); // padding / unused
            childCount = sCC;
        }
        else
        {
            childCount = reader.ReadInt32();
        }

        for (int i = 0; i < childCount; i++)
        {
            var childType = (UiType)reader.ReadInt32();
            N3UIBase child = UiFactory.Create(childType);
            child.FileFormatVersion = FileFormatVersion;
            child.Parent = this;
            // Load before AddChild so we don't reorder mid-parse; preserve push_front order afterwards.
            child.Load(file);
            _children.Insert(0, child);
        }

        // base info
        int idLen = reader.ReadInt32();
        Id = idLen > 0 ? reader.ReadFixedString(idLen) : string.Empty;

        Region = ReadRect(reader);
        Movable = ReadRect(reader);
        Style = reader.ReadUInt32();
        Reserved = reader.ReadUInt32();

        int tooltipLen = reader.ReadInt32();
        if (tooltipLen > 0)
            Tooltip = reader.ReadFixedString(tooltipLen);

        int sndOpenLen = reader.ReadInt32();
        if (sndOpenLen > 0)
            OpenSound = reader.ReadFixedString(sndOpenLen);

        int sndCloseLen = reader.ReadInt32();
        if (sndCloseLen > 0)
            CloseSound = reader.ReadFixedString(sndCloseLen);

        return true;
    }

    private static Rect ReadRect(FileReader reader)
    {
        int left = reader.ReadInt32();
        int top = reader.ReadInt32();
        int right = reader.ReadInt32();
        int bottom = reader.ReadInt32();
        return new Rect(left, top, right, bottom);
    }
}
