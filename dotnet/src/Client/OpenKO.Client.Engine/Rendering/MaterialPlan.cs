using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Rendering;

public enum EffectKind
{
    /// <summary>BasicEffect — single texture MODULATE diffuse (or untextured).</summary>
    Basic,

    /// <summary>DualTextureEffect — two-stage MODULATE (needs the 0.5 diffuse compensation).</summary>
    DualTexture,

    /// <summary>AlphaTestEffect — cutout parts (unlit; light baked into vertex colors).</summary>
    AlphaTest,
}

/// <summary>
/// The pure device-state decision for one __Material — everything the C++
/// derives from RF_* flags and the material blend fields at draw time
/// (CN3SPart::Render / CN3CPart::Render). The device layer applies it.
/// </summary>
public readonly record struct MaterialPlan(
    EffectKind Effect,
    bool DeferToAlphaManager,
    uint SrcBlend,
    uint DestBlend,
    bool DisableFog,
    bool CullNone,
    bool DisableZWrite,
    bool DisableZBuffer,
    bool PointSampling,
    bool UvClamp,
    bool NoLighting,
    bool Windy,
    bool BoardY);

public static class MaterialBinder
{
    /// <summary>
    /// Note on alpha test: the C++ enables D3D alpha test GLOBALLY
    /// (ALPHAFUNC=GREATER, ref 0), discarding fully transparent texels even
    /// on opaque draws. BasicEffect cannot discard — alpha-0 texels of
    /// opaque geometry write depth here (documented deviation; cutout
    /// character parts get AlphaTestEffect in the character slice).
    /// </summary>
    public static MaterialPlan Plan(in N3Material material, bool hasOverlayTexture)
    {
        var flags = (RenderFlags)material.RenderFlags;

        return new MaterialPlan(
            Effect: hasOverlayTexture ? EffectKind.DualTexture : EffectKind.Basic,
            DeferToAlphaManager: flags.HasFlag(RenderFlags.AlphaBlending),
            SrcBlend: material.SrcBlend != 0 ? material.SrcBlend : 5,   // default SRCALPHA
            DestBlend: material.DestBlend != 0 ? material.DestBlend : 6, // default INVSRCALPHA
            DisableFog: flags.HasFlag(RenderFlags.NotUseFog),
            CullNone: flags.HasFlag(RenderFlags.DoubleSided),
            DisableZWrite: flags.HasFlag(RenderFlags.NotZWrite),
            DisableZBuffer: flags.HasFlag(RenderFlags.NotZBuffer),
            PointSampling: flags.HasFlag(RenderFlags.PointSampling),
            UvClamp: flags.HasFlag(RenderFlags.UvClamp),
            NoLighting: flags.HasFlag(RenderFlags.NotUseLight),
            Windy: flags.HasFlag(RenderFlags.Windy),
            BoardY: flags.HasFlag(RenderFlags.BoardY));
    }
}
