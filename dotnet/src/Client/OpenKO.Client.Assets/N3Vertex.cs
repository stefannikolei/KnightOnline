using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenKO.Client.Assets;

/// <summary>
/// The FVF vertex layouts from Client/N3Base/My_3DStruct.h, kept blittable and
/// byte-identical to the C++ structs so the mesh readers can load vertex blocks
/// verbatim. Colors are D3DCOLOR (ARGB) uints.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexColor // __VertexColor (FVF_CV)
{
    public Vector3 Position;
    public uint Color;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexParticle // __VertexParticle (FVF_PARTICLE)
{
    public Vector3 Position;
    public float PointSize;
    public uint Color;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexTransformedColor // __VertexTransformedColor (FVF_TRANSFORMEDCOLOR)
{
    public Vector3 Position;
    public float Rhw;
    public uint Color;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexT1 // __VertexT1 (FVF_VNT1)
{
    public Vector3 Position;
    public Vector3 Normal;
    public float Tu;
    public float Tv;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexT2 // __VertexT2 (FVF_VNT2)
{
    public Vector3 Position;
    public Vector3 Normal;
    public float Tu;
    public float Tv;
    public float Tu2;
    public float Tv2;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexTransformed // __VertexTransformed (FVF_TRANSFORMED)
{
    public Vector3 Position;
    public float Rhw;
    public uint Color;
    public float Tu;
    public float Tv;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexTransformedT2 // __VertexTransformedT2 (FVF_TRANSFORMEDT2)
{
    public Vector3 Position;
    public float Rhw;
    public uint Color;
    public float Tu;
    public float Tv;
    public float Tu2;
    public float Tv2;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzT1 // __VertexXyzT1
{
    public Vector3 Position;
    public float Tu;
    public float Tv;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzT2 // __VertexXyzT2
{
    public Vector3 Position;
    public float Tu;
    public float Tv;
    public float Tu2;
    public float Tv2;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzNormal // __VertexXyzNormal
{
    public Vector3 Position;
    public Vector3 Normal;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzColor // __VertexXyzColor
{
    public Vector3 Position;
    public uint Color;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzColorT1 // __VertexXyzColorT1
{
    public Vector3 Position;
    public uint Color;
    public float Tu;
    public float Tv;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzColorT2 // __VertexXyzColorT2
{
    public Vector3 Position;
    public uint Color;
    public float Tu;
    public float Tv;
    public float Tu2;
    public float Tv2;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzColorSpecularT1 // __VertexXyzColorSpecularT1
{
    public Vector3 Position;
    public uint Color;
    public uint Specular;
    public float Tu;
    public float Tv;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexXyzNormalColor // __VertexXyzNormalColor
{
    public Vector3 Position;
    public Vector3 Normal;
    public uint Color;
}
