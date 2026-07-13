using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3VMesh</c> (Client/N3Base/N3VMesh.cpp) — a bare collision
/// mesh: __Vector3 positions plus an optional 16-bit index list (no indices
/// means every three vertices form a triangle).
/// </summary>
public sealed class N3VMesh : N3BaseFile
{
    public Vector3[] Vertices { get; private set; } = [];

    public ushort[] Indices { get; private set; } = [];

    public Vector3 Min { get; private set; }

    public Vector3 Max { get; private set; }

    public float Radius { get; private set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        int vertexCount = reader.ReadInt32();
        Vertices = vertexCount > 0 ? reader.ReadStructs<Vector3>(vertexCount) : [];

        int indexCount = reader.ReadInt32();
        Indices = indexCount > 0 ? reader.ReadStructs<ushort>(indexCount) : [];

        FindMinMax();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(Vertices.Length);
        writer.WriteStructs<Vector3>(Vertices);
        writer.Write(Indices.Length);
        writer.WriteStructs<ushort>(Indices);
    }

    public void Initialize(Vector3[] vertices, ushort[] indices)
    {
        Vertices = vertices;
        Indices = indices;
        FindMinMax();
    }

    private void FindMinMax()
    {
        Min = Vector3.Zero;
        Max = Vector3.Zero;
        Radius = 0f;
        if (Vertices.Length == 0)
            return;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (ref readonly Vector3 v in Vertices.AsSpan())
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        Min = min;
        Max = max;
        Radius = (max - min).Length() * 0.5f;
    }
}
