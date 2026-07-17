using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXParticle</c> (Client/N3Base/N3FXParticle.h) — a single live
/// particle. This is a runtime simulation object: it is never serialized (no
/// Load/Save), so only its data layout is modelled here. The Tick/movement logic
/// belongs to slice 9.10b.
/// </summary>
public sealed class N3FXParticle
{
    /// <summary>m_iID — the particle's base vertex index in its part's buffer.</summary>
    public int Id { get; set; }

    public float Life { get; set; }

    public float CurrLife { get; set; }

    public int NumTex { get; set; }

    public int TexIndex { get; set; }

    /// <summary>m_vCreatePoint — the spawn reference position.</summary>
    public Vector3 CreatePoint { get; set; }

    /// <summary>m_vAxis — the reference (rotation) axis.</summary>
    public Vector3 Axis { get; set; }

    /// <summary>m_vVelocity — the travel direction/speed.</summary>
    public Vector3 Velocity { get; set; }

    public Vector3 Acceleration { get; set; }

    public float DropVelocity { get; set; }

    public float DropY { get; set; }

    public float Rotation { get; set; }

    /// <summary>m_vLcPos — local position.</summary>
    public Vector3 LocalPos { get; set; }

    /// <summary>m_vWdPos — world position.</summary>
    public Vector3 WorldPos { get; set; }

    /// <summary>m_dwColor — D3DCOLOR (ARGB).</summary>
    public uint Color { get; set; } = 0xffffffff;

    /// <summary>m_fSize — the original (unscaled) size.</summary>
    public float Size { get; set; }
}
