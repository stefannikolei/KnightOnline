using System.Collections.Generic;
using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXSPart</c> (Client/N3Base/N3FXShape.cpp) — one part of an FX
/// shape: a pivot, the referenced progressive-mesh file name, a material and the
/// animation-frame texture names.
/// <para>
/// Although CN3FXSPart derives from CN3BaseFileAccess, its <c>Load</c> does NOT
/// read a [len][name] header (it reads the pivot first), so this is a plain class.
/// The C++ munges the stored names against the shape's directory before resolving
/// them through the resource managers; the asset layer keeps the raw names.
/// </para>
/// </summary>
public sealed class N3FXShapePart
{
    public Vector3 Pivot { get; set; }

    /// <summary>The referenced .n3fxpmesh file name (raw, as stored).</summary>
    public string MeshFileName { get; set; } = string.Empty;

    public N3Material Material { get; set; }

    /// <summary>m_fTexFPS — texture animation interval.</summary>
    public float TexFps { get; set; } = 10f;

    /// <summary>The animation-frame texture file names (raw; empty entries were length-0 on disk).</summary>
    public List<string> TexNames { get; } = [];

    public void Load(BinaryReader reader)
    {
        Pivot = reader.ReadVector3();
        MeshFileName = reader.ReadN3FileName();

        Material = reader.ReadStruct<N3Material>();

        int texCount = reader.ReadInt32();
        TexFps = reader.ReadSingle();

        TexNames.Clear();
        for (int i = 0; i < texCount; i++)
            TexNames.Add(reader.ReadN3FileName());
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(Pivot);
        writer.WriteN3FileName(MeshFileName);

        writer.WriteStruct(Material);

        writer.Write(TexNames.Count);
        writer.Write(TexFps);

        foreach (string tex in TexNames)
            writer.WriteN3FileName(tex);
    }
}

/// <summary>
/// Port of <c>CN3FXShape</c> (Client/N3Base/N3FXShape.cpp) — the <c>.n3fxshape</c>
/// file: a transform-with-collision header, a list of <see cref="N3FXShapePart"/>,
/// and five trailing attribute dwords (belong + attr0..3).
/// </summary>
public sealed class N3FXShape : N3TransformCollision
{
    /// <summary>The number of trailing attribute dwords (belong + attr0..3).</summary>
    public const int AttributeCount = 5;

    public List<N3FXShapePart> Parts { get; } = [];

    /// <summary>The 5 trailing dwords: [0] belong, [1..4] attr0..3.</summary>
    public uint[] Attributes { get; } = new uint[AttributeCount];

    public override void Load(BinaryReader reader)
    {
        base.Load(reader); // name header + transform + collision/climb names

        Parts.Clear();
        int partCount = reader.ReadInt32();
        for (int i = 0; i < partCount; i++)
        {
            var part = new N3FXShapePart();
            part.Load(reader);
            Parts.Add(part);
        }

        for (int i = 0; i < AttributeCount; i++)
            Attributes[i] = reader.ReadUInt32();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(Parts.Count);
        foreach (N3FXShapePart part in Parts)
            part.Save(writer);

        for (int i = 0; i < AttributeCount; i++)
            writer.Write(Attributes[i]);
    }
}
