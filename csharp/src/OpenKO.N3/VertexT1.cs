using System.Runtime.InteropServices;
using OpenKO.Numerics;

namespace OpenKO.N3;

/// <summary>
/// Port of the C++ <c>__VertexT1</c> (Client/N3Base/My_3DStruct.h) — a fully expanded render vertex:
/// position, normal and a single set of UVs (8 contiguous floats, FVF_VNT1). This is what
/// <see cref="N3IMesh.BuildVertexList"/> produces for upload into a GPU vertex buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VertexT1
{
    public Vector3 Position;
    public Vector3 Normal;
    public float U;
    public float V;

    public VertexT1(Vector3 position, Vector3 normal, float u, float v)
    {
        Position = position;
        Normal = normal;
        U = u;
        V = v;
    }
}
