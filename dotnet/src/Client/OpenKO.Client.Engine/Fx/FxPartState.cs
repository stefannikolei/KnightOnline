using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// The bundle-level context a part needs each frame: where the bundle sits, which
/// way it faces, and its optional target scale. Mirrors the fields
/// <c>CN3FXPartBase</c> reads off <c>m_pRefBundle</c> (m_vPos, m_vDir,
/// m_bDependScale, m_fTargetScale). Bundle-level target following / movement acts
/// live in the game manager (slice 9.10c); this carries only what the part sim
/// reads.
/// </summary>
public struct FxBundleContext
{
    public System.Numerics.Vector3 Pos;

    public System.Numerics.Vector3 Dir;

    public bool DependScale;

    public float TargetScale;

    public static FxBundleContext Default => new()
    {
        Pos = System.Numerics.Vector3.Zero,
        Dir = new System.Numerics.Vector3(0f, 0f, 1f),
        DependScale = false,
        TargetScale = 1f,
    };
}

/// <summary>
/// Port of the <c>CN3FXPartBase</c> lifecycle state machine (N3FXPartBase.cpp
/// Start/Stop/Tick): READY → LIVE → DYING → DEAD, driven by the part's
/// <c>m_fLife</c>/<c>m_fFadeIn</c>/<c>m_fFadeOut</c> and a per-part
/// <see cref="IsDead"/> predicate. Pure and headless-testable; the concrete part
/// simulators own an instance and advance it at the top of their own Tick.
/// </summary>
public sealed class FxPartState
{
    private readonly float _life;
    private readonly float _fadeIn;
    private readonly Func<bool> _isDead;

    /// <param name="life">m_fLife — play time in seconds (0 = infinite).</param>
    /// <param name="fadeIn">m_fFadeIn.</param>
    /// <param name="isDead">
    /// The subclass <c>IsDead()</c> predicate: the base returns true immediately;
    /// particles die when the live pool empties; boards die at total life.
    /// </param>
    public FxPartState(float life, float fadeIn, Func<bool>? isDead = null)
    {
        _life = life;
        _fadeIn = fadeIn;
        _isDead = isDead ?? (static () => true);
    }

    /// <summary>m_dwState.</summary>
    public FxPartLifeState State { get; private set; } = FxPartLifeState.Ready;

    /// <summary>m_fCurrLife.</summary>
    public float CurrLife { get; private set; }

    /// <summary>CN3FXPartBase::Init — reset the running clock (called on DEAD).</summary>
    public void Init() => CurrLife = 0f;

    /// <summary>CN3FXPartBase::Start — enter LIVE.</summary>
    public void Start() => State = FxPartLifeState.Live;

    /// <summary>Reset to READY (bundle Trigger re-arms every part).</summary>
    public void Rearm()
    {
        State = FxPartLifeState.Ready;
        CurrLife = 0f;
    }

    /// <summary>
    /// CN3FXPartBase::Stop — begin dying; the clock is snapped forward to the end
    /// of the play window so the fade-out timing lines up.
    /// </summary>
    public void Stop()
    {
        State = FxPartLifeState.Dying;
        CurrLife = _life + _fadeIn;
    }

    /// <summary>
    /// CN3FXPartBase::Tick — advance the clock, auto-Stop at the end of life, and
    /// transition to DEAD once <see cref="IsDead"/> holds. Returns false when the
    /// part produced no work this frame (DEAD/READY, or just died).
    /// </summary>
    public bool Tick(float secPerFrame)
    {
        if (State is FxPartLifeState.Dead or FxPartLifeState.Ready)
            return false;

        CurrLife += secPerFrame;

        if (_life > 0f && State == FxPartLifeState.Live && CurrLife >= _life + _fadeIn)
            Stop();

        if (State == FxPartLifeState.Dying && _isDead())
        {
            State = FxPartLifeState.Dead;
            Init();
            return false;
        }

        return true;
    }
}

/// <summary>e_FXPartState as the pure layer sees it (mirrors <see cref="FxPartState"/>).</summary>
public enum FxPartLifeState
{
    Dead = 0,
    Dying = 1,
    Live = 2,
    Ready = 3,
}
