using System.Numerics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>One orchestrated part: its start time, its simulator, and its type.</summary>
public sealed class FxBundlePartRuntime
{
    public required float StartTime { get; init; }

    public required IFxPart Part { get; init; }

    public required FxPartType Type { get; init; }
}

/// <summary>
/// Pure port of the <c>CN3FXBundle</c> orchestration (N3FXBundle.cpp:
/// Init/Trigger/Tick/Stop/CheckAllPartsDead). Advances every part by its start
/// time, starting a READY part once the bundle clock reaches it, and retires the
/// bundle once all parts are dead or the bundle lifetime elapses.
/// <para>
/// Bundle-level <em>movement</em> — target following, the <c>e_FXBundleAct</c>
/// acts, curve/dir interpolation toward a target joint — belongs to the game FX
/// manager (slice 9.10c). This carries the runtime <see cref="Position"/> /
/// <see cref="Direction"/> / <see cref="TargetScale"/> the parts read, and leaves
/// the movement acts to the caller that drives those each frame.
/// </para>
/// </summary>
public sealed class FxBundleSimulator
{
    private readonly float _life0;
    private readonly List<FxBundlePartRuntime> _parts = [];

    private float _life;

    public FxBundleSimulator(N3FXBundle bundle, uint seed = 0x1234u)
    {
        _life0 = bundle.Life0;
        DependScale = bundle.DependScale;

        uint partSeed = seed;
        foreach (N3FXBundlePart? slot in bundle.Parts)
        {
            if (slot?.Part == null)
                continue;

            IFxPart? sim = CreatePart(slot.Part, partSeed);
            if (sim == null)
                continue;

            _parts.Add(new FxBundlePartRuntime
            {
                StartTime = slot.StartTime,
                Part = sim,
                Type = slot.Part.Type,
            });

            partSeed += 0x9E3779B9u; // decorrelate each particle part's RNG stream
        }
    }

    /// <summary>m_dwState — the bundle lifecycle.</summary>
    public FxBundleState State { get; private set; } = FxBundleState.Dead;

    /// <summary>m_vPos — set by the game manager each frame (9.10c movement).</summary>
    public Vector3 Position { get; set; }

    /// <summary>m_vDir — the bundle facing.</summary>
    public Vector3 Direction { get; set; } = new(0f, 0f, 1f);

    /// <summary>m_bDependScale.</summary>
    public bool DependScale { get; set; }

    /// <summary>m_fTargetScale.</summary>
    public float TargetScale { get; set; } = 1f;

    /// <summary>m_fLife — the running bundle clock.</summary>
    public float Life => _life;

    public IReadOnlyList<FxBundlePartRuntime> Parts => _parts;

    /// <summary>The bundle context the parts read this frame.</summary>
    public FxBundleContext Context => new()
    {
        Pos = Position,
        Dir = Direction,
        DependScale = DependScale,
        TargetScale = TargetScale,
    };

    /// <summary>CN3FXBundle::Init — reset the clock and every part.</summary>
    public void Init()
    {
        _life = 0f;
        State = FxBundleState.Dead;
        Direction = new Vector3(0f, 0f, 1f);
        foreach (FxBundlePartRuntime p in _parts)
            p.Part.Rearm();
    }

    /// <summary>
    /// CN3FXBundle::Trigger — go LIVE and re-arm every part to READY. Source/target
    /// ids and the one-shot sound are game-manager concerns (9.10c).
    /// </summary>
    public void Trigger()
    {
        State = FxBundleState.Live;
        _life = 0f;
        foreach (FxBundlePartRuntime p in _parts)
            p.Part.Rearm();
    }

    /// <summary>CN3FXBundle::Stop — begin dying (or retire immediately).</summary>
    public void Stop(bool immediately = false)
    {
        if (State == FxBundleState.Dead)
            return;

        State = FxBundleState.Dying;
        if (immediately)
        {
            Init();
            return;
        }

        foreach (FxBundlePartRuntime p in _parts)
            p.Part.Stop();
    }

    /// <summary>CN3FXBundle::CheckAllPartsDead.</summary>
    public bool CheckAllPartsDead()
    {
        foreach (FxBundlePartRuntime p in _parts)
        {
            if (p.Part.State != FxPartLifeState.Dead)
                return false;
        }

        return true;
    }

    /// <summary>
    /// CN3FXBundle::Tick — advance the clock, retire when done, start due parts and
    /// tick every part. <paramref name="cameraDistance"/> feeds particle LOD.
    /// </summary>
    public bool Tick(float secPerFrame, float? cameraDistance = null)
    {
        if (State == FxBundleState.Dead)
            return false;

        _life += secPerFrame;

        if (State is FxBundleState.Dying or FxBundleState.Live)
        {
            if (CheckAllPartsDead() || (_life0 != 0f && _life > _life0))
            {
                State = FxBundleState.Dead;
                Init();
                return false;
            }
        }

        FxBundleContext context = Context;
        foreach (FxBundlePartRuntime p in _parts)
        {
            if (p.StartTime <= _life && p.Part.State == FxPartLifeState.Ready)
                p.Part.Start();

            p.Part.Advance(secPerFrame, context, cameraDistance);
        }

        return true;
    }

    private static IFxPart? CreatePart(N3FXPartBase part, uint seed) => part switch
    {
        N3FXPartParticles particles => new FxParticleSimulator(particles, seed),
        N3FXPartBillBoard billboard => new FxBillboardSimulator(billboard),
        N3FXPartBottomBoard bottom => new FxBottomBoardSimulator(bottom),
        N3FXPartMesh mesh => new FxMeshSimulator(mesh),
        _ => null,
    };
}
