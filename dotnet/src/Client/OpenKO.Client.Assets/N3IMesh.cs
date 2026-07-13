using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3IMesh</c> (Client/N3Base/N3IMesh.cpp) — the indexed mesh
/// used by character parts: positions+normals with separate vertex and UV
/// index lists per face corner.
/// </summary>
public class N3IMesh : N3BaseFile
{
    public int FaceCount { get; private set; }

    public int VertexCount { get; private set; }

    public int UvCount { get; private set; }

    /// <summary>__VertexXyzNormal array, length VertexCount.</summary>
    public N3VertexXyzNormal[] Vertices { get; private set; } = [];

    /// <summary>Vertex index per face corner, length FaceCount * 3.</summary>
    public ushort[] VertexIndices { get; private set; } = [];

    /// <summary>Interleaved u,v pairs, length UvCount * 2.</summary>
    public float[] Uvs { get; private set; } = [];

    /// <summary>UV index per face corner, length FaceCount * 3 (empty if UvCount == 0).</summary>
    public ushort[] UvIndices { get; private set; } = [];

    public Vector3 Min { get; private set; }

    public Vector3 Max { get; private set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        int faceCount = reader.ReadInt32();
        int vertexCount = reader.ReadInt32();
        int uvCount = reader.ReadInt32();

        if (faceCount > 0 && vertexCount > 0)
        {
            FaceCount = faceCount;
            VertexCount = vertexCount;
            UvCount = uvCount > 0 ? uvCount : 0;
            Vertices = reader.ReadStructs<N3VertexXyzNormal>(vertexCount);
            VertexIndices = reader.ReadStructs<ushort>(faceCount * 3);
        }
        else
        {
            // C++ Release(): counts reset — and because m_nUVC is then 0 the
            // UV block below is NOT read even if the header said uvCount > 0.
            FaceCount = 0;
            VertexCount = 0;
            UvCount = 0;
            Vertices = [];
            VertexIndices = [];
        }

        if (UvCount > 0)
        {
            Uvs = reader.ReadStructs<float>(UvCount * 2);
            UvIndices = reader.ReadStructs<ushort>(FaceCount * 3);
        }
        else
        {
            Uvs = [];
            UvIndices = [];
        }

        FindMinMax();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(FaceCount);
        writer.Write(VertexCount);
        writer.Write(UvCount);

        if (FaceCount > 0 && VertexCount > 0)
        {
            writer.WriteStructs<N3VertexXyzNormal>(Vertices);
            writer.WriteStructs<ushort>(VertexIndices);
        }

        if (UvCount > 0)
        {
            writer.WriteStructs<float>(Uvs);
            writer.WriteStructs<ushort>(UvIndices);
        }
    }

    public void Initialize(int faceCount, N3VertexXyzNormal[] vertices, ushort[] vertexIndices, float[] uvs, ushort[] uvIndices)
    {
        FaceCount = faceCount;
        VertexCount = vertices.Length;
        Vertices = vertices;
        VertexIndices = vertexIndices;
        UvCount = uvs.Length / 2;
        Uvs = uvs;
        UvIndices = uvIndices;
        FindMinMax();
    }

    /// <summary>
    /// CN3IMesh::BuildVertexList — flattens the two index lists into a
    /// triangle-list __VertexT1 array (FaceCount * 3 entries).
    /// </summary>
    public N3VertexT1[] BuildVertexList()
    {
        if (FaceCount <= 0)
            return [];

        var result = new N3VertexT1[FaceCount * 3];
        for (int n = 0; n < result.Length; n++)
        {
            N3VertexXyzNormal src = Vertices[VertexIndices[n]];
            float tu = 0f, tv = 0f;
            if (UvCount > 0)
            {
                int uvIndex = UvIndices[n];
                tu = Uvs[uvIndex * 2];
                tv = Uvs[uvIndex * 2 + 1];
            }

            result[n] = new N3VertexT1
            {
                Position = src.Position,
                Normal = src.Normal,
                Tu = tu,
                Tv = tv,
            };
        }

        return result;
    }

    private void FindMinMax()
    {
        Min = Vector3.Zero;
        Max = Vector3.Zero;
        if (Vertices.Length == 0)
            return;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (ref readonly N3VertexXyzNormal v in Vertices.AsSpan())
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        Min = min;
        Max = max;
    }
}
