using System.Runtime.InteropServices;
using OpenKO.Numerics;

namespace OpenKO.N3;

/// <summary>
/// Port of the C++ <c>__VertexXyzNormal</c> (Client/N3Base/My_3DStruct.h) — a position plus a normal.
/// Laid out exactly as on disk (6 contiguous floats, 24 bytes) so it can be blitted from N3 files.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VertexXyzNormal
{
    public Vector3 Position;
    public Vector3 Normal;

    public VertexXyzNormal(Vector3 position, Vector3 normal)
    {
        Position = position;
        Normal = normal;
    }
}
