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

    public FxPartLifeState State => _state.State;

    public void Start() => _state.Start();

    public void Rearm() => _state.Rearm();

    public void Stop() => _state.Stop();

    public bool Advance(float secPerFrame, FxBundleContext bundle, float? cameraDistance)
        => Tick(secPerFrame);

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
