using OpenKO.IO;
using OpenKO.Numerics;

namespace OpenKO.N3;

/// <summary>
/// Port of the C++ <c>CN3IMesh</c> (Client/N3Base/N3IMesh.cpp) — an indexed triangle mesh with
/// per-vertex position+normal and (optionally) a separate UV array with its own index list.
///
/// This ports the headless data model and file <see cref="Load"/>/<see cref="Save"/> only; the
/// DirectX-specific <c>BuildVertexList</c>/<c>Render</c> paths will be reintroduced on top of the
/// OpenGL renderer later. The on-disk layout matches the original byte-for-byte.
/// </summary>
public class N3IMesh : N3BaseFileAccess
{
    /// <summary>Face (triangle) count.</summary>
    public int FaceCount { get; protected set; }

    /// <summary>Vertex (position+normal) array.</summary>
    public VertexXyzNormal[] Vertices { get; protected set; } = Array.Empty<VertexXyzNormal>();

    /// <summary>Vertex indices, 3 per face.</summary>
    public ushort[] VertexIndices { get; protected set; } = Array.Empty<ushort>();

    /// <summary>UV coordinate count (0 if the mesh has no UVs).</summary>
    public int UvCount { get; protected set; }

    /// <summary>UV data, 2 floats (u, v) per UV.</summary>
    public float[] Uvs { get; protected set; } = Array.Empty<float>();

    /// <summary>UV indices, 3 per face (only present when <see cref="UvCount"/> &gt; 0).</summary>
    public ushort[] UvIndices { get; protected set; } = Array.Empty<ushort>();

    public int VertexCount => Vertices.Length;

    public Vector3 Min { get; protected set; }
    public Vector3 Max { get; protected set; }

    public override void Release()
    {
        base.Release();
        FaceCount = 0;
        UvCount = 0;
        Vertices = Array.Empty<VertexXyzNormal>();
        VertexIndices = Array.Empty<ushort>();
        Uvs = Array.Empty<float>();
        UvIndices = Array.Empty<ushort>();
        Min = default;
        Max = default;
    }

    public override bool Load(IFile file)
    {
        base.Load(file); // resource name header

        var reader = file as FileReader
            ?? throw new ArgumentException("N3IMesh.Load requires a FileReader", nameof(file));

        int nFC = reader.ReadInt32();
        int nVC = reader.ReadInt32();
        int nUVC = reader.ReadInt32();

        if (nFC > 0 && nVC > 0)
        {
            FaceCount = nFC;
            Vertices = reader.ReadArray<VertexXyzNormal>(nVC);
            VertexIndices = reader.ReadArray<ushort>(nFC * 3);
        }
        else
        {
            Release();
            return true;
        }

        if (nUVC > 0)
        {
            UvCount = nUVC;
            Uvs = reader.ReadArray<float>(nUVC * 2);
            UvIndices = reader.ReadArray<ushort>(nFC * 3);
        }

        FindMinMax();
        return true;
    }

    public override bool Save(IFile file)
    {
        base.Save(file);

        var writer = file as FileWriter
            ?? throw new ArgumentException("N3IMesh.Save requires a FileWriter", nameof(file));

        writer.Write(FaceCount);
        writer.Write(VertexCount);
        writer.Write(UvCount);

        if (FaceCount > 0 && VertexCount > 0)
        {
            foreach (VertexXyzNormal v in Vertices)
                writer.Write(v);
            foreach (ushort idx in VertexIndices)
                writer.Write(idx);
        }

        if (UvCount > 0)
        {
            foreach (float f in Uvs)
                writer.Write(f);
            foreach (ushort idx in UvIndices)
                writer.Write(idx);
        }

        return true;
    }

    /// <summary>Computes the axis-aligned bounds (port of <c>CN3IMesh::FindMinMax</c>).</summary>
    public void FindMinMax()
    {
        if (Vertices.Length == 0)
        {
            Min = default;
            Max = default;
            return;
        }

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(-float.MaxValue, -float.MaxValue, -float.MaxValue);

        foreach (VertexXyzNormal v in Vertices)
        {
            Vector3 p = v.Position;
            if (p.X < min.X) min.X = p.X;
            if (p.Y < min.Y) min.Y = p.Y;
            if (p.Z < min.Z) min.Z = p.Z;
            if (p.X > max.X) max.X = p.X;
            if (p.Y > max.Y) max.Y = p.Y;
            if (p.Z > max.Z) max.Z = p.Z;
        }

        Min = min;
        Max = max;
    }

    /// <summary>
    /// Expands the indexed mesh into a flat, non-indexed triangle list of <see cref="VertexT1"/>
    /// (port of <c>CN3IMesh::BuildVertexList</c>). The original used parallel vertex- and UV-index
    /// lists (the UVs have their own indices), so this de-references both per triangle corner,
    /// producing 3 * <see cref="FaceCount"/> vertices ready for a GPU vertex buffer.
    ///
    /// When the mesh has no UVs, U/V are emitted as 0 (matching the original's <c>m_nUVC &lt;= 0</c> path).
    /// </summary>
    public VertexT1[] BuildVertexList()
    {
        if (FaceCount <= 0)
            return Array.Empty<VertexT1>();

        var result = new VertexT1[FaceCount * 3];
        bool hasUv = UvCount > 0;

        for (int i = 0; i < FaceCount * 3; i++)
        {
            ushort vi = VertexIndices[i];
            VertexXyzNormal v = Vertices[vi];

            float u = 0f, vCoord = 0f;
            if (hasUv)
            {
                ushort uvi = UvIndices[i];
                u = Uvs[uvi * 2];
                vCoord = Uvs[uvi * 2 + 1];
            }

            result[i] = new VertexT1(v.Position, v.Normal, u, vCoord);
        }

        return result;
    }
}
