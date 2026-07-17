using System.Numerics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Pure port of the <c>CN3FXPartParticles</c> particle pool and emitter
/// (N3FXPartParticles.cpp: Init / Tick / CreateParticles_Spread /
/// CreateParticles_Gather and the per-particle CN3FXParticle::Tick sim). No
/// GraphicsDevice: this advances positions, velocities, gravity, colour and life;
/// the camera-facing quad build is the separate pure
/// <see cref="FxParticleVertexBuilder"/>, and the device draw is
/// <c>FxRenderer</c>.
/// <para>
/// Emit maths reproduced verbatim: the spread cone
/// (<c>rand()%emitAngle - emitAngle/2</c> in the XZ plane, then a
/// <c>rand()%360</c> roll about Z), the bundle → emitter → spray direction
/// alignment chain via <see cref="FxMath.RotateBetween"/>, the create-range jitter
/// (<c>min + (max-min)*rand()%100/100</c> per axis), per-particle velocity
/// integration (<c>localPos += velocity*dt</c>; velocity += accel*dt, accel being
/// the always-zero per-particle accel the C++ leaves untouched), separate gravity
/// drop (<c>dropVel += gravity*dt; dropY += dropVel*dt; worldPos.y -= dropY</c>),
/// spin about the emit axis (<c>rot += dt*ptRotVelocity</c>) and colour via
/// <see cref="FxColor"/>. The shape-driven (<c>m_bAnimKey</c>) emitter path is
/// deferred to the mesh slice; a plain (non-anim) emitter is simulated here.
/// </para>
/// </summary>
public sealed class FxParticleSimulator : IFxPart
{
    bool IFxPart.Advance(float secPerFrame, FxBundleContext bundle, float? cameraDistance)
        => Tick(secPerFrame, bundle, cameraDistance);

    private readonly N3FXPartParticles _desc;
    private readonly FxPartState _state;
    private readonly List<FxRuntimeParticle> _alive = [];
    private readonly List<FxRuntimeParticle> _dead = [];

    private FxRandom _rng;
    private Vector3 _currVelocity;
    private Vector3 _currPos;
    private Vector3 _emitterDir = new(0f, 0f, 1f);
    private float _currCreateDelay;
    private int _numLodParticle;

    public FxParticleSimulator(N3FXPartParticles desc, uint seed = 0x1234u)
    {
        _desc = desc;
        _rng = new FxRandom(seed);
        _state = new FxPartState(desc.Life, desc.FadeIn, () => _alive.Count == 0);
        _numLodParticle = desc.NumParticle;

        // InitVB: allocate the dead pool.
        for (int i = 0; i < desc.NumParticle; i++)
            _dead.Add(new FxRuntimeParticle());

        Init();
    }

    /// <summary>The loaded emitter description this simulator runs.</summary>
    public N3FXPartParticles Descriptor => _desc;

    /// <summary>The live particles (read-only) — input to the vertex builder.</summary>
    public IReadOnlyList<FxRuntimeParticle> AliveParticles => _alive;

    public FxPartLifeState State => _state.State;

    public int AliveCount => _alive.Count;

    /// <summary>CN3FXPartBase::Start / CN3FXPartParticles::Start.</summary>
    public void Start() => _state.Start();

    /// <summary>Bundle Trigger re-arm (READY).</summary>
    public void Rearm() => _state.Rearm();

    /// <summary>CN3FXPartBase::Stop.</summary>
    public void Stop() => _state.Stop();

    /// <summary>
    /// CN3FXPartParticles::Init — recycle every live particle back to the dead pool
    /// and (re)roll each dead particle's life/size from the configured ranges.
    /// </summary>
    public void Init()
    {
        _currCreateDelay = _desc.CreateDelay;
        _currVelocity = _desc.Velocity;
        _currPos = _desc.Pos;

        foreach (FxRuntimeParticle p in _alive)
            _dead.Add(p);
        _alive.Clear();

        foreach (FxRuntimeParticle p in _dead)
            ResetParticle(p);
    }

    /// <summary>
    /// CN3FXPartParticles::Tick. Advances the emitter, spawns on the create-delay
    /// cadence while LIVE, and ages the live pool. <paramref name="cameraDistance"/>
    /// drives the C++ distance LOD (fewer particles far away); pass null to keep
    /// the full count (the tool/headless path).
    /// </summary>
    public bool Tick(float secPerFrame, FxBundleContext bundle, float? cameraDistance = null)
    {
        if (!_state.Tick(secPerFrame))
            return false;

        _numLodParticle = ComputeLod(cameraDistance);

        _currCreateDelay += secPerFrame;

        // Non-anim emitter movement (the m_bAnimKey shape path is deferred).
        _currVelocity += _desc.Acceleration * secPerFrame;
        _currPos += _currVelocity * secPerFrame;
        _emitterDir = _currVelocity.Length() != 0f
            ? Vector3.Normalize(_currVelocity)
            : new Vector3(0f, 0f, 1f);

        if (_currCreateDelay >= _desc.CreateDelay && _state.State == FxPartLifeState.Live)
        {
            _currCreateDelay = 0f;
            CreateParticles(bundle);
        }

        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            if (!AdvanceParticle(_alive[i], secPerFrame))
            {
                FxRuntimeParticle dead = _alive[i];
                _alive.RemoveAt(i);
                _dead.Add(dead);
                ResetParticle(dead);
            }
        }

        return true;
    }

    private int ComputeLod(float? cameraDistance)
    {
        if (cameraDistance is not { } dist)
            return _desc.NumParticle;

        if (dist > 30f)
            return (int)(_desc.NumParticle / 3.0f);

        return (int)((_desc.NumParticle * 1 / 3.0f) + ((_desc.NumParticle * 2 / 3.0f) * ((30.0f - dist) / 30.0f)));
    }

    private void ResetParticle(FxRuntimeParticle p)
    {
        p.Color = FxColor.White;
        p.CurrLife = 0f;
        p.DropVelocity = 0f;
        p.DropY = 0f;
        p.Life = _desc.ParticleLifeMin + ((_desc.ParticleLifeMax - _desc.ParticleLifeMin) * _rng.NextUnit());
        p.Size = _desc.ParticleSizeMin + ((_desc.ParticleSizeMax - _desc.ParticleSizeMin) * _rng.NextUnit());
        p.NumTex = _desc.NumTex;
        p.TexIndex = 0;
        p.LocalPos = Vector3.Zero;
        p.Velocity = Vector3.Zero;
        p.Accel = Vector3.Zero;
        p.Rotation = 0f;
    }

    private void CreateParticles(FxBundleContext bundle)
    {
        if (_alive.Count > _numLodParticle)
            return;

        if (_desc.EmitType == FxPartParticleEmitType.Spread)
            CreateParticlesSpread(bundle);
        else if (_desc.EmitType == FxPartParticleEmitType.Gather)
            CreateParticlesGather(bundle);

        // EmitType.Normal creates nothing (the C++ switch has no Normal case).
    }

    private void CreateParticlesSpread(FxBundleContext bundle)
    {
        for (int i = 0; i < _desc.NumCreate; i++)
        {
            if (_dead.Count == 0)
                break;

            FxRuntimeParticle p = _dead[0];

            float emitAngle = _desc.EmitCondition.EmitAngle;
            float unitAngleXz = emitAngle != 0f
                ? _rng.NextMod((int)emitAngle) - (emitAngle / 2.0f)
                : 0f;
            float unitAxisZ = _rng.NextMod(360);

            var vDir = new Vector3(
                MathF.Sin(DegToRad(unitAngleXz)), 0f, MathF.Cos(DegToRad(unitAngleXz)));
            vDir = Vector3.Transform(vDir, FxMath.RotationZ(unitAxisZ));
            vDir = FxMath.SafeNormalize(vDir);

            Vector3 vDirEmit = ResolveEmitDir(bundle);
            vDir = Vector3.Transform(vDir, FxMath.RotateBetween(new Vector3(0f, 0f, 1f), vDirEmit));

            p.Axis = vDirEmit;
            p.Velocity = vDir * _desc.PtVelocity;

            ApplyCreateRangeAndScale(p, bundle);

            _dead.RemoveAt(0);
            _alive.Add(p);
        }
    }

    private void CreateParticlesGather(FxBundleContext bundle)
    {
        for (int i = 0; i < _desc.NumCreate; i++)
        {
            if (_dead.Count == 0)
                break;

            FxRuntimeParticle p = _dead[0];

            var vDir = _desc.EmitCondition.GatherPoint;

            Vector3 vDirEmit = ResolveEmitDir(bundle);
            vDir = Vector3.Transform(vDir, FxMath.RotateBetween(new Vector3(0f, 0f, 1f), vDirEmit));

            p.Axis = vDirEmit;

            Vector3 createPos = ApplyCreateRangeAndScale(p, bundle);

            // Gather aims each particle from its spawn point toward the gather point.
            vDir -= createPos;
            vDir = FxMath.SafeNormalize(vDir);
            p.Velocity = vDir * _desc.PtVelocity;

            _dead.RemoveAt(0);
            _alive.Add(p);
        }
    }

    /// <summary>
    /// The bundle → emitter direction alignment shared by both emit shapes: rotate
    /// the emitter dir by the bundle facing, then rotate the emit dir by that.
    /// </summary>
    private Vector3 ResolveEmitDir(FxBundleContext bundle)
    {
        var v = new Vector3(0f, 0f, 1f);
        Vector3 vDirPart = _emitterDir;
        Vector3 vDirEmit = _desc.PtEmitDir;

        vDirPart = Vector3.Transform(vDirPart, FxMath.RotateBetween(v, bundle.Dir));

        if (vDirPart.Length() != 0f)
            vDirEmit = Vector3.Transform(vDirEmit, FxMath.RotateBetween(v, vDirPart));

        return vDirEmit;
    }

    /// <summary>
    /// The create-range jitter + optional bundle scale, shared by spread/gather.
    /// Returns the rotated local spawn offset (<c>m_vLcPos</c>).
    /// </summary>
    private Vector3 ApplyCreateRangeAndScale(FxRuntimeParticle p, FxBundleContext bundle)
    {
        Vector3 maxCreate = _desc.MaxCreateRange;
        Vector3 minCreate = _desc.MinCreateRange;

        if (bundle.DependScale)
        {
            p.Size *= bundle.TargetScale;
            p.Velocity *= bundle.TargetScale;
            maxCreate *= bundle.TargetScale;
            minCreate *= bundle.TargetScale;
        }

        var createPos = new Vector3(
            minCreate.X + ((maxCreate.X - minCreate.X) * _rng.NextUnit()),
            minCreate.Y + ((maxCreate.Y - minCreate.Y) * _rng.NextUnit()),
            minCreate.Z + ((maxCreate.Z - minCreate.Z) * _rng.NextUnit()));

        createPos = Vector3.Transform(createPos, FxMath.RotateBetween(new Vector3(0f, 0f, 1f), p.Axis));

        p.CreatePoint = bundle.Pos + _currPos;
        p.LocalPos = createPos;
        return createPos;
    }

    /// <summary>CN3FXParticle::Tick — the per-particle sim minus the vertex write.</summary>
    private bool AdvanceParticle(FxRuntimeParticle p, float secPerFrame)
    {
        if (_desc.ChangeColor && p.CurrLife >= p.Life)
            return false;
        if (!_desc.ChangeColor && p.CurrLife >= _desc.FadeIn + p.Life + _desc.FadeOut)
            return false;

        // World position (uses pre-increment rotation / dropY).
        Matrix4x4 mtxRot = FxMath.RotationAxis(p.Axis, p.Rotation);
        p.WorldPos = p.CreatePoint + Vector3.Transform(p.LocalPos, mtxRot);
        p.WorldPos.Y -= p.DropY;

        // Snapshot the currLife the vertex scale/tex-rotation should use.
        p.VertexCurrLife = p.CurrLife;

        // Advance local position by velocity.
        p.LocalPos += p.Velocity * secPerFrame;

        // Colour.
        p.Color = _desc.ChangeColor
            ? FxColor.ColorKeyAt(_desc.ChangeColors, p.CurrLife, p.Life, N3FxDef.NumKeyColor)
            : FxColor.ParticleFade(p.CurrLife, _desc.FadeIn, p.Life, _desc.FadeOut);

        // Advance velocity / spin / gravity / life / texture frame.
        p.Velocity += p.Accel * secPerFrame;
        p.Rotation += secPerFrame * _desc.PtRotVelocity;
        p.DropVelocity += _desc.PtGravity * secPerFrame;
        p.DropY += p.DropVelocity * secPerFrame;
        p.CurrLife += secPerFrame;
        p.TexIndex = p.NumTex > 0 ? (int)(p.CurrLife * _desc.TexFps) % p.NumTex : 0;

        return true;
    }

    private static float DegToRad(float degrees) => degrees * MathF.PI / 180.0f;
}
