namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// The uniform lifecycle surface every effect part simulator exposes so the
/// bundle can orchestrate them (mirrors the <c>CN3FXPartBase</c> virtuals the
/// bundle calls: Start/Stop/Tick and the m_dwState it reads). Vertex building
/// stays on the concrete simulators, which the device renderer downcasts to.
/// </summary>
public interface IFxPart
{
    FxPartLifeState State { get; }

    /// <summary>CN3FXPartBase::Start.</summary>
    void Start();

    /// <summary>Re-arm to READY (bundle Trigger).</summary>
    void Rearm();

    /// <summary>CN3FXPartBase::Stop.</summary>
    void Stop();

    /// <summary>
    /// One frame of simulation. <paramref name="cameraDistance"/> feeds the
    /// particle distance-LOD; parts that ignore it may pass null.
    /// </summary>
    bool Advance(float secPerFrame, FxBundleContext bundle, float? cameraDistance);
}
