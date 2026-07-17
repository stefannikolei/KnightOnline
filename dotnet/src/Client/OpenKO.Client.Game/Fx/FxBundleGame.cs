using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Fx;

namespace OpenKO.Client.Game.Fx;

/// <summary>
/// Port of <c>CN3FXBundleGame</c> (Client/WarFare/N3FXBundleGame.cpp): the
/// game-side wrapper around an Engine <see cref="FxBundleSimulator"/> that follows
/// its source/target entity (and joint) every frame and applies the
/// <see cref="FxBundleAct"/> movement act, then advances the pure part sim.
/// <para>
/// Source/target world positions are resolved through the injected
/// <see cref="IFxEntityLocator"/> (the C++ <c>CharacterGetByID</c>/<c>JointPosGet</c>).
/// The bounding-box / <c>m_pShapeExtraRef</c> centre-of-mesh anchoring and the
/// <c>DependScale</c> target sizing need character geometry the locator does not
/// expose; those are approximated by the resolved joint/origin position and left
/// for the executable to refine (see the class remarks in slice 9.10c).
/// </para>
/// </summary>
public sealed class FxBundleGame
{
    private readonly IFxEntityLocator _locator;
    private readonly FxBundleSimulator _sim;

    public FxBundleGame(N3FXBundle bundle, IFxEntityLocator locator, uint seed = 0x1234u)
    {
        _locator = locator;
        _sim = new FxBundleSimulator(bundle, seed);
        Velocity = bundle.Velocity;
        Static = bundle.Static;
    }

    /// <summary>The wrapped pure simulator (parts + clock).</summary>
    public FxBundleSimulator Simulator => _sim;

    /// <summary>m_iID — the game FXID this bundle plays.</summary>
    public int FxId { get; set; } = -1;

    /// <summary>m_iIdx — the trigger index (arrow slot / self-fx sub-index).</summary>
    public int Idx { get; set; }

    /// <summary>m_iSourceID — the emitter entity.</summary>
    public int SourceId { get; set; } = -1;

    /// <summary>m_iTargetID — the target entity (-1 = none / region cast).</summary>
    public int TargetId { get; set; } = -1;

    /// <summary>m_iSourceJoint — the joint the emitter attaches to.</summary>
    public int SourceJoint { get; set; }

    /// <summary>m_iTargetJoint — the joint the target attaches to (-1 = origin).</summary>
    public int TargetJoint { get; set; } = -1;

    /// <summary>m_iMoveType — the e_FXBundleAct movement behaviour.</summary>
    public FxBundleAct MoveType { get; set; } = FxBundleAct.MoveNone;

    /// <summary>m_fVelocity — the flight speed (units/sec) for the moving acts.</summary>
    public float Velocity { get; set; }

    /// <summary>m_bStatic — a static bundle triggers as a region cast at its dest.</summary>
    public bool Static { get; set; }

    /// <summary>m_bRegion — true for a target-position cast (no target entity).</summary>
    public bool Region { get; private set; }

    /// <summary>The lower-cased .fxb filename this bundle's origin is cached under.</summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>m_vPos — the current bundle position (mirrors the sim's Position).</summary>
    public Vector3 Position => _sim.Position;

    /// <summary>m_vDestPos — the target/destination point the movement steers toward.</summary>
    public Vector3 DestPos { get; set; }

    /// <summary>m_vDir — the current bundle facing.</summary>
    public Vector3 Direction => _sim.Direction;

    /// <summary>m_fDistance — source→dest distance captured at trigger (curve height base).</summary>
    public float Distance { get; private set; }

    /// <summary>m_fHeight — the parabola apex height (== Distance/2) for the curve act.</summary>
    public float Height { get; private set; }

    /// <summary>True once the bundle has retired (all parts dead / past lifetime).</summary>
    public bool IsDead => _sim.State == FxBundleState.Dead;

    /// <summary>The bundle lifecycle state.</summary>
    public FxBundleState State => _sim.State;

    /// <summary>
    /// CN3FXBundleGame::Trigger(SourceID, TargetID, TargetJoint) — anchor at the
    /// source, aim at the target entity (its resolved joint/origin), capture the
    /// distance/dir, and go live. A <see cref="Static"/> bundle degrades to the
    /// region overload at its computed dest, matching the C++.
    /// </summary>
    public void Trigger(int sourceId, int targetId, int targetJoint)
    {
        Region = false;
        SourceId = sourceId;
        TargetId = targetId;
        TargetJoint = targetJoint;

        Vector3 sourcePos = ResolveSource();
        Vector3 destPos = sourcePos;
        if (targetId != sourceId && _locator.TryGetPosition(targetId, targetJoint, out Vector3 tp))
            destPos = tp;

        DestPos = destPos;
        Distance = (DestPos - sourcePos).Length();
        Height = Distance / 2.0f;

        Vector3 dir = SafeNormalize(DestPos - sourcePos);

        if (Static)
        {
            TriggerRegion(sourceId, DestPos);
            return;
        }

        _sim.Position = sourcePos;
        _sim.Direction = dir;
        _sim.Trigger();
    }

    /// <summary>
    /// CN3FXBundleGame::Trigger(SourceID, TargetPos) — a region cast: anchor at the
    /// source, fly toward a fixed world point, no target entity to follow.
    /// </summary>
    public void TriggerRegion(int sourceId, Vector3 targetPos)
    {
        Region = true;
        SourceId = sourceId;
        TargetId = -1;
        TargetJoint = -1;

        Vector3 sourcePos = ResolveSource();
        DestPos = targetPos;
        Distance = (DestPos - sourcePos).Length();
        Height = Distance / 2.0f;
        Vector3 dir = SafeNormalize(DestPos - sourcePos);

        _sim.Position = sourcePos;
        _sim.Direction = dir;
        _sim.Trigger();
    }

    /// <summary>
    /// CN3FXBundleGame::Tick — re-read the target (non-region), apply the movement
    /// act to advance <c>m_vPos</c>/<c>m_vDir</c>, then advance the part sim. The
    /// <paramref name="cameraPos"/> feeds the REGION_POISON act (the poison cloud
    /// hugs the camera). Returns false once retired.
    /// </summary>
    public bool Tick(float secPerFrame, Vector3 cameraPos)
    {
        if (_sim.State == FxBundleState.Dead)
            return false;

        if (_sim.State == FxBundleState.Live)
        {
            // Non-region bundles re-read the (possibly moved) target each frame.
            if (!Region && _locator.TryGetPosition(TargetId, TargetJoint, out Vector3 tp))
                DestPos = tp;

            ApplyMovement(secPerFrame, cameraPos);
        }

        // TargetScale is carried from Trigger; DependScale target sizing is deferred
        // (it needs the target's radius/height, not exposed by the locator).
        return _sim.Tick(secPerFrame);
    }

    private void ApplyMovement(float secPerFrame, Vector3 cameraPos)
    {
        Vector3 pos = _sim.Position;
        Vector3 dir = _sim.Direction;
        float step = secPerFrame * Velocity;

        switch (MoveType)
        {
            case FxBundleAct.MoveCurveFixedTarget:
            {
                // Faithful to the shipped bug: x/z are OVERWRITTEN with the per-frame
                // delta (not accumulated), and y traces a sine arc by fraction travelled.
                Vector3 delta = dir * step;
                pos.X = delta.X;
                pos.Z = delta.Z;
                float ang = Distance != 0f
                    ? MathF.PI * (Distance - (DestPos - pos).Length()) / Distance
                    : 0f;
                pos.Y = MathF.Sin(ang) * Height;
                break;
            }

            case FxBundleAct.MoveDirSlow:
            case FxBundleAct.MoveDirFixedTarget:
                pos += dir * step;
                break;

            case FxBundleAct.MoveDirFlexableTargetRatio:
            case FxBundleAct.MoveDirFlexableTarget:
            {
                // RATIO falls through to FLEXABLE in the C++ (its body is commented out).
                if (!_locator.TryGetPosition(TargetId, TargetJoint, out Vector3 tp))
                {
                    pos += dir * step;
                }
                else
                {
                    DestPos = tp;
                    dir = SafeNormalize(DestPos - pos);
                    pos += dir * step;
                }

                break;
            }

            case FxBundleAct.MoveNone:
            {
                dir.Y = 0f;
                dir = SafeNormalize(dir);
                pos = DestPos;
                // A self-cast (source == target) faces the source's own direction;
                // the locator exposes position only, so the flattened dir is kept.
                break;
            }

            case FxBundleAct.RegionPoison:
                // The poison cloud recentres on the camera each frame.
                pos = cameraPos;
                break;
        }

        _sim.Position = pos;
        _sim.Direction = dir;
    }

    private Vector3 ResolveSource() =>
        _locator.TryGetPosition(SourceId, SourceJoint, out Vector3 sp) ? sp : Vector3.Zero;

    private static Vector3 SafeNormalize(Vector3 v)
    {
        float len = v.Length();
        return len > 0f ? v / len : v;
    }
}
