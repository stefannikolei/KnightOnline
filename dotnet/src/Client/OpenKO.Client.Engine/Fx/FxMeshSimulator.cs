using System.Numerics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Pure port of the <c>CN3FXPartMesh</c> lifecycle + colour/frame state
/// (N3FXPartMesh.cpp Tick). The animated-mesh part drives a duplicated
/// <c>CN3FXShape</c> (transform keys + a LOD-collapsed FXPMesh instance); that
/// device-side geometry render is deferred to the mesh/shape render wiring, so
/// this pure simulator advances only what is testable without a GraphicsDevice:
/// the READY→LIVE→DYING→DEAD state (base <c>IsDead</c> — a mesh part dies the
/// frame after it stops), the per-part fade colour applied to the shape, and the
/// shape animation frame (<c>currLife * meshFPS</c>). The full shape transform +
/// FXPMesh draw is the device slice's job (it reuses the existing PMesh
/// renderers).
/// </summary>
public sealed class FxMeshSimulator : IFxPart
{
    private readonly N3FXPartMesh _desc;
    private readonly FxPartState _state;

    public FxMeshSimulator(N3FXPartMesh desc)
    {
        _desc = desc;
        // CN3FXPartMesh does not override IsDead → base returns true, so the part
        // transitions to DEAD the first Tick after Stop().
        _state = new FxPartState(desc.Life, desc.FadeIn);
    }

    /// <summary>m_dwCurrColor applied to every shape part.</summary>
    public uint CurrColor { get; private set; } = FxColor.White;

    /// <summary>The shape animation frame this tick (currLife * meshFPS).</summary>
    public float CurrFrame { get; private set; }

    /// <summary>The mesh part descriptor (shape file name, unit scale, render flags).</summary>
    public N3FXPartMesh Descriptor => _desc;

    /// <summary>
    /// m_pShape-&gt;m_mtxParent — the part's world transform this tick, driven from the
    /// bundle position + the part's own position/velocity and the scaled unit size.
    /// The device layer renders the resolved shape's parts under this matrix. This
    /// is the pure Move-mode transform (the Rotate/curve act's parent orientation is
    /// deferred, as is the frame-rate-dependent scale-acceleration integration).
    /// </summary>
    public Matrix4x4 ParentMatrix { get; private set; } = Matrix4x4.Identity;

    public FxPartLifeState State => _state.State;

    public void Start() => _state.Start();

    public void Rearm() => _state.Rearm();

    public void Stop() => _state.Stop();

    public bool Advance(float secPerFrame, FxBundleContext bundle, float? cameraDistance)
    {
        bool alive = Tick(secPerFrame);
        ParentMatrix = ComputeParentMatrix(bundle);
        return alive;
    }

    /// <summary>
    /// CN3FXPartMesh::Tick (Move) — the shape's parent transform: scale by
    /// <c>unitScale + scaleVel * currLife</c> (times the bundle target scale when
    /// <c>DependScale</c>), translated to <c>bundlePos + partPos + partVel*currLife</c>.
    /// </summary>
    private Matrix4x4 ComputeParentMatrix(in FxBundleContext bundle)
    {
        float currLife = _state.CurrLife;

        Vector3 scale = _desc.UnitScale + _desc.ScaleVelocity * currLife;
        if (bundle.DependScale)
            scale *= bundle.TargetScale;
        scale = Vector3.Max(scale, Vector3.Zero);

        Vector3 pos = bundle.Pos + _desc.Pos + _desc.Velocity * currLife;

        Matrix4x4 m = Matrix4x4.CreateScale(scale);
        m.Translation = pos;
        return m;
    }

    /// <summary>CN3FXPartMesh::Tick — fade colour + animation frame (transform/render deferred).</summary>
    public bool Tick(float secPerFrame)
    {
        if (!_state.Tick(secPerFrame))
            return false;

        float currLife = _state.CurrLife;

        if (currLife <= _desc.FadeIn)
        {
            uint alpha = ToAlpha(255.0f * currLife / _desc.FadeIn);
            CurrColor = (alpha << 24) + FxColor.TransparentWhite;
        }
        else if (CurrColor != FxColor.White && currLife < _desc.FadeIn + _desc.Life)
        {
            CurrColor = FxColor.White;
        }

        if (_state.State == FxPartLifeState.Dying)
        {
            float total = _desc.FadeIn + _desc.Life + _desc.FadeOut;
            if (currLife >= total)
            {
                CurrColor = FxColor.TransparentWhite;
            }
            else
            {
                uint alpha = ToAlpha(255.0f * (total - currLife) / _desc.FadeOut);
                CurrColor = (alpha << 24) + FxColor.TransparentWhite;
            }
        }

        CurrFrame = currLife * _desc.MeshFps;
        return true;
    }

    private static uint ToAlpha(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
            return 0u;
        if (value >= 255f)
            return 255u;
        return (uint)value;
    }
}
