using System.Runtime.InteropServices;

namespace OpenKO.Client.Assets;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3ColorValue
{
    public float R;
    public float G;
    public float B;
    public float A;
}

/// <summary>
/// __Material (My_3DStruct.h): a raw D3DMATERIAL9 followed by the texture
/// stage/blend settings. Blitted to disk as 92 bytes by the C++ loaders.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3Material
{
    // _D3DMATERIAL9
    public N3ColorValue Diffuse;
    public N3ColorValue Ambient;
    public N3ColorValue Specular;
    public N3ColorValue Emissive;
    public float Power;

    // __Material extras
    public uint ColorOp;
    public uint ColorArg1;
    public uint ColorArg2;
    public uint RenderFlags; // RF_* bits
    public uint SrcBlend;
    public uint DestBlend;
}
