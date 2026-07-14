using OpenKO.GameData.Maps;

namespace OpenKO.Client.Assets.Zones;

/// <summary>One placed world object — an <see cref="N3Shape"/> plus its raw type flags.</summary>
public sealed class ZoneObject
{
    public uint Type { get; init; }

    public required N3Shape Shape { get; init; }

    /// <summary>OBJ_SHAPE_EXTRA (0x1000) — castle gates/levers; loaded as a plain shape.</summary>
    public bool IsExtra => (Type & ZoneObjectSet.ObjShapeExtra) != 0;
}

/// <summary>
/// Port of <c>CN3ShapeMgr::Load</c> for the client <c>.opd</c> object-post-data:
/// the version-gated name header, the collision block (reused from the server's
/// <see cref="N3ShapeManager"/>), then the shape count and each placed
/// <see cref="N3Shape"/> (<c>[uint type][CN3Shape::Load]</c>). OBJ_SHAPE_EXTRA
/// shapes read the same bytes as a normal shape (the extra rotation state is
/// runtime-only), so they load as plain shapes without desyncing the stream.
/// </summary>
public sealed class ZoneObjectSet
{
    /// <summary>OBJ_SHAPE_EXTRA (My_3DStruct.h).</summary>
    public const uint ObjShapeExtra = 0x1000;

    public string Name { get; private set; } = string.Empty;

    /// <summary>The shared collision mesh/grid (from LoadCollisionData).</summary>
    public N3ShapeManager Collision { get; } = new();

    public List<ZoneObject> Objects { get; } = [];

    public static ZoneObjectSet LoadFromFile(string path, uint version = N3FormatVersion.Default)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var set = new ZoneObjectSet();
        set.Load(reader, version);
        return set;
    }

    public void Load(BinaryReader reader, uint version = N3FormatVersion.Default)
    {
        // CN3ShapeMgr::Load version-gated header (iIdk0 + name) for >= 1264.
        if (version >= N3FormatVersion.V1264)
        {
            reader.ReadInt32(); // iIdk0
            int nameLen = reader.ReadInt32();
            if (nameLen > 0)
                Name = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(nameLen));
        }

        Collision.LoadCollisionData(reader);

        int shapeCount = reader.ReadInt32();
        Objects.Clear();
        for (int i = 0; i < shapeCount; i++)
        {
            uint type = reader.ReadUInt32();
            var shape = new N3Shape { FileFormatVersion = version };
            shape.Load(reader);
            Objects.Add(new ZoneObject { Type = type, Shape = shape });
        }
    }
}
