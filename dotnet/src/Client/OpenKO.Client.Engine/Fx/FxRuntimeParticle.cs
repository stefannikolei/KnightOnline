using System.Numerics;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// A single live particle — the runtime state of <c>CN3FXParticle</c>
/// (N3FXParticle.h). Never serialized; owned by <see cref="FxParticleSimulator"/>
/// and recycled between the alive/dead pools. The four render-ready fields
/// (<see cref="WorldPos"/>, <see cref="Color"/>, <see cref="Size"/>,
/// <see cref="VertexCurrLife"/>, <see cref="TexIndex"/>) are snapshotted by the
/// sim each frame so the pure <see cref="FxParticleVertexBuilder"/> can rebuild
/// the camera-facing quad without touching sim state — the same values the C++
/// wrote straight into <c>m_pVB</c> during Tick.
/// </summary>
public sealed class FxRuntimeParticle
{
    // --- simulation state ---
    public float Life;

    public float CurrLife;

    public int NumTex;

    public Vector3 CreatePoint;

    public Vector3 Axis = new(0f, 0f, 1f);

    public Vector3 Velocity;

    public Vector3 Accel;

    public float DropVelocity;

    public float DropY;

    public float Rotation;

    public Vector3 LocalPos;

    public float Size;

    // --- render snapshot (set by the sim each frame) ---
    public Vector3 WorldPos;

    public uint Color = FxColor.White;

    /// <summary>The currLife the vertex scale/tex-rotation used (pre-increment).</summary>
    public float VertexCurrLife;

    public int TexIndex;
}
