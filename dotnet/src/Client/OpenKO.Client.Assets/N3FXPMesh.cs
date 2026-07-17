using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPMesh</c> (Client/N3Base/N3FXPMesh.cpp) — an FX progressive
/// mesh. Its on-disk format is byte-identical to <see cref="N3PMesh"/>: after the
/// name header come the collapse/index-change counts, the vertex/index arrays,
/// the edge-collapse list, the all-index-change list and the LOD control table.
/// The only difference is that at runtime the __VertexT1 positions/UVs are copied
/// into a colored vertex buffer (__VertexXyzColorT1, color = 0xffffffff) — exposed
/// here via <see cref="ColorVertices"/>.
/// </summary>
public sealed class N3FXPMesh : N3BaseFile
{
    public int NumCollapses { get; private set; }

    public int TotalIndexChanges { get; private set; }

    public int MaxNumVertices { get; private set; }

    public int MaxNumIndices { get; private set; }

    public int MinNumVertices { get; private set; }

    public int MinNumIndices { get; private set; }

    /// <summary>Full-detail source vertices (__VertexT1), length MaxNumVertices.</summary>
    public N3VertexT1[] Vertices { get; private set; } = [];

    /// <summary>The index buffer, length MaxNumIndices.</summary>
    public ushort[] Indices { get; private set; } = [];

    /// <summary>
    /// Collapse records plus the zeroed sentinel the C++ appends (length
    /// NumCollapses + 1 when NumCollapses &gt; 0, else empty).
    /// </summary>
    public N3PMesh.EdgeCollapse[] Collapses { get; private set; } = [];

    public int[] AllIndexChanges { get; private set; } = [];

    public N3PMesh.LodCtrlValue[] LodCtrlValues { get; private set; } = [];

    public Vector3 Min { get; private set; }

    public Vector3 Max { get; private set; }

    public float Radius { get; private set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        NumCollapses = reader.ReadInt32();
        TotalIndexChanges = reader.ReadInt32();
        MaxNumVertices = reader.ReadInt32();
        MaxNumIndices = reader.ReadInt32();
        MinNumVertices = reader.ReadInt32();
        MinNumIndices = reader.ReadInt32();

        Vertices = reader.ReadStructs<N3VertexT1>(MaxNumVertices);
        Indices = reader.ReadStructs<ushort>(MaxNumIndices);

        if (NumCollapses > 0)
        {
            Collapses = new N3PMesh.EdgeCollapse[NumCollapses + 1]; // +1: zeroed sentinel
            for (int i = 0; i < NumCollapses; i++)
            {
                Collapses[i] = ReadCollapse(reader);

                // The C++ repairs broken meshes: negative offsets -> 0.
                if (Collapses[i].IndexChangesOffset < 0)
                    Collapses[i].IndexChangesOffset = 0;
            }
        }
        else
        {
            Collapses = [];
        }

        AllIndexChanges = reader.ReadStructs<int>(TotalIndexChanges);

        int lodCount = reader.ReadInt32();
        var lods = new N3PMesh.LodCtrlValue[System.Math.Max(0, lodCount)];
        for (int i = 0; i < lodCount; i++)
            lods[i] = new N3PMesh.LodCtrlValue(reader.ReadSingle(), reader.ReadInt32());
        LodCtrlValues = lods;

        FindMinMax();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(NumCollapses);
        writer.Write(TotalIndexChanges);
        writer.Write(MaxNumVertices);
        writer.Write(MaxNumIndices);
        writer.Write(MinNumVertices);
        writer.Write(MinNumIndices);

        writer.WriteStructs<N3VertexT1>(Vertices);
        writer.WriteStructs<ushort>(Indices);

        for (int i = 0; i < NumCollapses; i++)
            WriteCollapse(writer, Collapses[i]);

        writer.WriteStructs<int>(AllIndexChanges);

        writer.Write(LodCtrlValues.Length);
        foreach (N3PMesh.LodCtrlValue lod in LodCtrlValues)
        {
            writer.Write(lod.Distance);
            writer.Write(lod.NumVertices);
        }
    }

    /// <summary>Test/tool helper mirroring the C++ member setup.</summary>
    public void Initialize(
        N3VertexT1[] vertices, ushort[] indices, int minNumVertices, int minNumIndices,
        N3PMesh.EdgeCollapse[] collapses, int[] allIndexChanges, N3PMesh.LodCtrlValue[] lodCtrlValues)
    {
        Vertices = vertices;
        Indices = indices;
        MaxNumVertices = vertices.Length;
        MaxNumIndices = indices.Length;
        MinNumVertices = minNumVertices;
        MinNumIndices = minNumIndices;
        NumCollapses = collapses.Length;
        Collapses = collapses.Length > 0 ? [.. collapses, default] : [];
        AllIndexChanges = allIndexChanges;
        TotalIndexChanges = allIndexChanges.Length;
        LodCtrlValues = lodCtrlValues;
        FindMinMax();
    }

    /// <summary>
    /// Builds the runtime colored vertex buffer (__VertexXyzColorT1) the C++ keeps
    /// after Load: position/UV copied from <see cref="Vertices"/>, color = white.
    /// </summary>
    public N3VertexXyzColorT1[] ColorVertices()
    {
        var result = new N3VertexXyzColorT1[MaxNumVertices];
        for (int i = 0; i < MaxNumVertices; i++)
        {
            result[i] = new N3VertexXyzColorT1
            {
                Position = Vertices[i].Position,
                Color = 0xffffffff,
                Tu = Vertices[i].Tu,
                Tv = Vertices[i].Tv,
            };
        }

        return result;
    }

    private static N3PMesh.EdgeCollapse ReadCollapse(BinaryReader reader)
    {
        var c = new N3PMesh.EdgeCollapse
        {
            NumIndicesToLose = reader.ReadInt32(),
            NumIndicesToChange = reader.ReadInt32(),
            NumVerticesToLose = reader.ReadInt32(),
            IndexChangesOffset = reader.ReadInt32(),
            CollapseTo = reader.ReadInt32(),
            ShouldCollapse = reader.ReadByte() != 0,
        };
        reader.BaseStream.Seek(3, SeekOrigin.Current); // MSVC struct padding
        return c;
    }

    private static void WriteCollapse(BinaryWriter writer, in N3PMesh.EdgeCollapse c)
    {
        writer.Write(c.NumIndicesToLose);
        writer.Write(c.NumIndicesToChange);
        writer.Write(c.NumVerticesToLose);
        writer.Write(c.IndexChangesOffset);
        writer.Write(c.CollapseTo);
        writer.Write(c.ShouldCollapse ? (byte)1 : (byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
    }

    private void FindMinMax()
    {
        if (MaxNumVertices <= 0)
        {
            Min = Vector3.Zero;
            Max = Vector3.Zero;
            Radius = 0f;
            return;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (ref readonly N3VertexT1 v in Vertices.AsSpan())
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        Min = min;
        Max = max;
        Radius = (max - min).Length() * 0.5f;
    }
}
