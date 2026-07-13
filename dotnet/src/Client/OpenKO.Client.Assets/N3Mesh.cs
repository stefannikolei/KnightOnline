using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3Mesh</c> (Client/N3Base/N3Mesh.cpp) — a plain __VertexT1
/// mesh with an optional 16-bit index list. Quirk kept verbatim: unlike the
/// other mesh classes, CN3Mesh::Load does NOT call the base loader, so there
/// is no name header in this format (it only ever appears embedded).
/// </summary>
public sealed class N3Mesh : N3BaseFile
{
    public N3VertexT1[] Vertices { get; private set; } = [];

    public ushort[] Indices { get; private set; } = [];

    public Vector3 Min { get; private set; }

    public Vector3 Max { get; private set; }

    public override void Load(BinaryReader reader)
    {
        // No base.Load — CN3Mesh::Load skips the name header.
        int vertexCount = reader.ReadInt32();
        if (vertexCount > 0)
        {
            Vertices = reader.ReadStructs<N3VertexT1>(vertexCount);
            FindMinMax();
        }
        else
        {
            Vertices = [];
            Min = Vector3.Zero;
            Max = Vector3.Zero;
        }

        int indexCount = reader.ReadInt32();
        Indices = indexCount > 0 ? reader.ReadStructs<ushort>(indexCount) : [];
    }

    public override void Save(BinaryWriter writer)
    {
        // No base.Save — mirrors the loader.
        writer.Write(Vertices.Length);
        writer.WriteStructs<N3VertexT1>(Vertices);
        writer.Write(Indices.Length);
        writer.WriteStructs<ushort>(Indices);
    }

    public void Initialize(N3VertexT1[] vertices, ushort[] indices)
    {
        Vertices = vertices;
        Indices = indices;
        FindMinMax();
    }

    private void FindMinMax()
    {
        Min = Vector3.Zero;
        Max = Vector3.Zero;
        if (Vertices.Length == 0)
            return;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (ref readonly N3VertexT1 v in Vertices.AsSpan())
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        Min = min;
        Max = max;
    }
}
