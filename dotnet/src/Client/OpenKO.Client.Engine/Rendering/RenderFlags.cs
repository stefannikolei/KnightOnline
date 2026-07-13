namespace OpenKO.Client.Engine.Rendering;

/// <summary>__Material.nRenderFlags bits (My_3DStruct.h RF_*).</summary>
[Flags]
public enum RenderFlags : uint
{
    None = 0,
    AlphaBlending = 0x1,   // RF_ALPHABLENDING — defer to the alpha manager
    NotUseFog = 0x2,       // RF_NOTUSEFOG
    DoubleSided = 0x4,     // RF_DOUBLESIDED — cull off
    BoardY = 0x8,          // RF_BOARD_Y — billboard about Y
    PointSampling = 0x10,  // RF_POINTSAMPLING
    Windy = 0x20,          // RF_WINDY
    NotUseLight = 0x40,    // RF_NOTUSELIGHT
    DiffuseAlpha = 0x80,   // RF_DIFFUSEALPHA
    NotZWrite = 0x100,     // RF_NOTZWRITE
    UvClamp = 0x200,       // RF_UV_CLAMP
    NotZBuffer = 0x400,    // RF_NOTZBUFFER
}
