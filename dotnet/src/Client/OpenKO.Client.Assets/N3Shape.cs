using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// One shape part (<c>CN3SPart</c> in N3Shape.cpp). No name header — the
/// parts are embedded back to back inside the .n3shape file. Texture name
/// slots may be empty (the C++ leaves the reference null then).
/// </summary>
public sealed class N3SPart
{
    public Vector3 Pivot { get; set; }

    public string MeshFileName { get; set; } = string.Empty;

    public N3Material Material { get; set; }

    public float TexFps { get; set; } = 30f;

    public List<string> TexFileNames { get; } = [];

    public void Load(BinaryReader reader)
    {
        Pivot = reader.ReadVector3();
        MeshFileName = reader.ReadN3FileName();
        Material = reader.ReadStruct<N3Material>();

        int texCount = reader.ReadInt32();
        TexFps = reader.ReadSingle();
        TexFileNames.Clear();
        for (int i = 0; i < texCount; i++)
            TexFileNames.Add(reader.ReadN3FileName());
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(Pivot);
        writer.WriteN3FileName(MeshFileName);
        writer.WriteStruct(Material);

        writer.Write(TexFileNames.Count);
        writer.Write(TexFps);
        foreach (string name in TexFileNames)
            writer.WriteN3FileName(name);
    }
}

/// <summary>
/// Port of <c>CN3Shape</c> loading (N3Shape.cpp) — the .n3shape file: the
/// transform+collision header, the embedded parts and the game metadata
/// (belong/event/NPC fields the server also reads via N3ShapeMgr).
/// </summary>
public class N3Shape : N3TransformCollision
{
    public List<N3SPart> Parts { get; } = [];

    public int Belong { get; set; }

    public int EventId { get; set; }

    public int EventType { get; set; }

    public int NpcId { get; set; }

    public int NpcStatus { get; set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        Parts.Clear();
        int partCount = reader.ReadInt32();
        for (int i = 0; i < partCount; i++)
        {
            var part = new N3SPart();
            part.Load(reader);
            Parts.Add(part);
        }

        // Old shapes (Misc/grasses*, catapult*) end before some of the game
        // metadata ints. The C++ reads past EOF are no-ops keeping the zero
        // defaults — mirrored with EOF guards.
        if (reader.BaseStream.Position < reader.BaseStream.Length)
            Belong = reader.ReadInt32();
        if (reader.BaseStream.Position < reader.BaseStream.Length)
            EventId = reader.ReadInt32();
        if (reader.BaseStream.Position < reader.BaseStream.Length)
            EventType = reader.ReadInt32();
        if (reader.BaseStream.Position < reader.BaseStream.Length)
            NpcId = reader.ReadInt32();
        if (reader.BaseStream.Position < reader.BaseStream.Length)
            NpcStatus = reader.ReadInt32();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(Parts.Count);
        foreach (N3SPart part in Parts)
            part.Save(writer);

        writer.Write(Belong);
        writer.Write(EventId);
        writer.Write(EventType);
        writer.Write(NpcId);
        writer.Write(NpcStatus);
    }
}
