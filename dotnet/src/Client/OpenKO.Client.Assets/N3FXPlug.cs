using System.Collections.Generic;
using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPlugPart</c> (Client/N3Base/N3FXPlug.cpp) — one attachment in
/// an FX plug: the referenced <c>.fxb</c> file name, the joint/reference index it
/// rides on, and the position/direction offsets from that joint.
/// <para>
/// The C++ resolves and loads the referenced bundle from its own file during
/// Load; the asset layer keeps just the file name (bundles are separate files).
/// </para>
/// </summary>
public sealed class N3FXPlugPart : N3BaseFile
{
    /// <summary>The referenced .fxb file name (empty when the on-disk length was &lt;= 0).</summary>
    public string FxbFileName { get; set; } = string.Empty;

    /// <summary>m_nRefIndex — the joint / reference index (-1 = none).</summary>
    public int RefIndex { get; set; } = -1;

    /// <summary>m_vOffsetPos — offset from the joint.</summary>
    public Vector3 OffsetPos { get; set; }

    /// <summary>m_vOffsetDir — offset direction from the joint.</summary>
    public Vector3 OffsetDir { get; set; } = new(0f, 0f, 1f);

    /// <summary>
    /// A per-part scale (always 1.0 in the corpus) present in real
    /// <c>.n3fxplug</c> files right after <see cref="OffsetDir"/>.
    /// <para>
    /// NOTE: the repo's C++ <c>CN3FXPlugPart::Load</c> does NOT read this field
    /// (or <see cref="Reserved"/>); it stops after m_vOffsetDir. Every shipped
    /// .n3fxplug nevertheless carries these 8 bytes per part, so the port reads
    /// them — otherwise a multi-part plug would misinterpret the next part's
    /// name header. Confirmed against all 17 corpus plugs.
    /// </para>
    /// </summary>
    public float Scale { get; set; } = 1f;

    /// <summary>The int that follows <see cref="Scale"/> (1 or -1 in the corpus). See <see cref="Scale"/>.</summary>
    public int Reserved { get; set; } = -1;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader); // name header

        FxbFileName = reader.ReadN3FileName();

        RefIndex = reader.ReadInt32();
        OffsetPos = reader.ReadVector3();
        OffsetDir = reader.ReadVector3();

        Scale = reader.ReadSingle();
        Reserved = reader.ReadInt32();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.WriteN3FileName(FxbFileName);

        writer.Write(RefIndex);
        writer.Write(OffsetPos);
        writer.Write(OffsetDir);

        writer.Write(Scale);
        writer.Write(Reserved);
    }
}

/// <summary>
/// Port of <c>CN3FXPlug</c> (Client/N3Base/N3FXPlug.cpp) — the <c>.n3fxplug</c>
/// file: a name header and a list of <see cref="N3FXPlugPart"/>.
/// </summary>
public sealed class N3FXPlug : N3BaseFile
{
    public List<N3FXPlugPart> Parts { get; } = [];

    public override void Load(BinaryReader reader)
    {
        base.Load(reader); // name header

        Parts.Clear();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var part = new N3FXPlugPart { FileFormatVersion = FileFormatVersion };
            part.Load(reader);
            Parts.Add(part);
        }
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(Parts.Count);
        foreach (N3FXPlugPart part in Parts)
            part.Save(writer);
    }
}
