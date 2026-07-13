using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3PMesh</c> (Client/N3Base/N3PMesh.cpp) — the .n3pmesh
/// progressive mesh: the full-detail vertex/index arrays plus the edge
/// collapse list that <see cref="N3PMeshInstance"/> walks to change LOD.
/// </summary>
public sealed class N3PMesh : N3BaseFile
{
    /// <summary>
    /// One edge collapse / vertex split. On disk this is the raw
    /// __EdgeCollapse struct: five int32 fields plus a bool padded to 24
    /// bytes by MSVC (the three padding bytes are written as-is).
    /// </summary>
    public struct EdgeCollapse
    {
        public int NumIndicesToLose;
        public int NumIndicesToChange;
        public int NumVerticesToLose;

        /// <summary>Start offset into <see cref="AllIndexChanges"/> (iIndexChanges).</summary>
        public int IndexChangesOffset;

        public int CollapseTo;

        /// <summary>
        /// True while stopping here would leave holes — the instance keeps
        /// splitting until this is false.
        /// </summary>
        public bool ShouldCollapse;
    }

    public readonly record struct LodCtrlValue(float Distance, int NumVertices);

    public int NumCollapses { get; private set; }

    public int TotalIndexChanges { get; private set; }

    public int MaxNumVertices { get; private set; }

    public int MaxNumIndices { get; private set; }

    public int MinNumVertices { get; private set; }

    public int MinNumIndices { get; private set; }

    /// <summary>Full-detail vertices (__VertexT1), length MaxNumVertices.</summary>
    public N3VertexT1[] Vertices { get; private set; } = [];

    /// <summary>The index buffer at minimum LOD, length MaxNumIndices.</summary>
    public ushort[] Indices { get; private set; } = [];

    /// <summary>
    /// The collapse records plus the zeroed sentinel the C++ appends
    /// (length NumCollapses + 1 when NumCollapses &gt; 0, else empty) —
    /// SplitOne deliberately lets the walk pointer rest on the sentinel.
    /// </summary>
    public EdgeCollapse[] Collapses { get; private set; } = [];

    public int[] AllIndexChanges { get; private set; } = [];

    public LodCtrlValue[] LodCtrlValues { get; private set; } = [];

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
            Collapses = new EdgeCollapse[NumCollapses + 1]; // +1: zeroed sentinel, see SplitOne
            for (int i = 0; i < NumCollapses; i++)
            {
                Collapses[i] = ReadCollapse(reader);

                // The C++ load repairs broken meshes: negative offsets -> 0.
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
        var lods = new LodCtrlValue[System.Math.Max(0, lodCount)];
        for (int i = 0; i < lodCount; i++)
            lods[i] = new LodCtrlValue(reader.ReadSingle(), reader.ReadInt32());
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
        foreach (LodCtrlValue lod in LodCtrlValues)
        {
            writer.Write(lod.Distance);
            writer.Write(lod.NumVertices);
        }
    }

    /// <summary>Test/tool helper mirroring the C++ member setup.</summary>
    public void Initialize(
        N3VertexT1[] vertices, ushort[] indices, int minNumVertices, int minNumIndices,
        EdgeCollapse[] collapses, int[] allIndexChanges, LodCtrlValue[] lodCtrlValues)
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

    private static EdgeCollapse ReadCollapse(BinaryReader reader)
    {
        var c = new EdgeCollapse
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

    private static void WriteCollapse(BinaryWriter writer, in EdgeCollapse c)
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
