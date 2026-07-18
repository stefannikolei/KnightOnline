using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Fx;

namespace OpenKO.Client.Game.Fx;

/// <summary>
/// Port of <c>CN3FXMgr</c> (Client/WarFare/N3FXMgr.cpp): the game-side owner of
/// every live effect bundle plus the global weather field. It triggers bundles by
/// FXID (resolving + caching the loaded origin <see cref="N3FXBundle"/> through
/// <see cref="IFxBundleLoader"/>), advances and culls them each
/// <see cref="Tick"/>, and drives the <see cref="Weather"/> sub-object fed by the
/// WIZ_WEATHER handler.
/// <para>
/// The <c>m_OriginBundle</c> cache + reference counting are reproduced: an origin
/// is kept for <see cref="OriginLimitedTime"/> seconds after its last live copy
/// dies, then evicted. The collision → WIZ_MAGIC_PROCESS "kill flying" packets the
/// C++ <c>Tick</c> emits for the local player's projectiles need the world
/// collision meshes + socket and are deferred to the executable wiring; this layer
/// reproduces the spawn/advance/cull/movement that is pure + testable.
/// </para>
/// </summary>
public sealed class FxManager
{
    private sealed class BundleOrigin
    {
        public required string Key { get; init; }

        public required N3FXBundle Bundle { get; init; }

        /// <summary>__TABLE_FX::dwSoundID — the sound played when a copy triggers (0 = none).</summary>
        public uint SoundId { get; init; }

        public int Num { get; set; }

        public float LimitedTime { get; set; }
    }

    private readonly IFxEntityLocator _locator;
    private readonly IFxBundleLoader _loader;
    private readonly List<FxBundleGame> _bundles = [];
    private readonly Dictionary<string, BundleOrigin> _origins = [];
    private uint _seed;

    public FxManager(IFxEntityLocator locator, IFxBundleLoader loader)
    {
        _locator = locator;
        _loader = loader;
    }

    /// <summary>m_fOriginLimitedTime — seconds an unused origin lingers before eviction.</summary>
    public float OriginLimitedTime { get; set; } = 60.0f;

    /// <summary>The global weather field (rain/snow), advanced with the bundles.</summary>
    public WeatherSimulator Weather { get; } = new();

    /// <summary>The live bundles (m_ListBundle).</summary>
    public IReadOnlyList<FxBundleGame> Bundles => _bundles;

    /// <summary>The cached bundle origins (m_OriginBundle), keyed by .fxb filename.</summary>
    public int OriginCount => _origins.Count;

    /// <summary>
    /// CN3FXMgr::TriggerBundle(SourceID, SourceJoint, FXID, TargetID, TargetJoint, idx, MoveType)
    /// — spawn a bundle that follows the source and steers at the target entity.
    /// A no-op for an unknown FXID.
    /// </summary>
    public FxBundleGame? TriggerBundle(
        int sourceId, int sourceJoint, int fxId, int targetId, int targetJoint,
        int idx = 0, FxBundleAct moveType = FxBundleAct.MoveNone)
    {
        BundleOrigin? origin = ResolveOrigin(fxId);
        if (origin == null)
            return null;

        FxBundleGame bundle = Spawn(origin, fxId, sourceJoint, idx, moveType);
        bundle.Trigger(sourceId, targetId, targetJoint);
        _bundles.Add(bundle);
        origin.Num++;
        return bundle;
    }

    /// <summary>
    /// CN3FXMgr::TriggerBundle(SourceID, SourceJoint, FXID, TargetPos, idx, MoveType)
    /// — the region overload: spawn a bundle that flies from the source toward a
    /// fixed world point (no target entity).
    /// </summary>
    public FxBundleGame? TriggerBundle(
        int sourceId, int sourceJoint, int fxId, Vector3 targetPos,
        int idx = 0, FxBundleAct moveType = FxBundleAct.MoveNone)
    {
        BundleOrigin? origin = ResolveOrigin(fxId);
        if (origin == null)
            return null;

        FxBundleGame bundle = Spawn(origin, fxId, sourceJoint, idx, moveType);
        bundle.TriggerRegion(sourceId, targetPos);
        _bundles.Add(bundle);
        origin.Num++;
        return bundle;
    }

    /// <summary>
    /// CN3FXMgr::Stop — stop every bundle from <paramref name="sourceId"/> at index
    /// <paramref name="idx"/> (and matching <paramref name="fxId"/> when it is &gt;= 0).
    /// <paramref name="targetId"/> is accepted for signature parity but, as in the
    /// C++, not part of the match.
    /// </summary>
    public void Stop(int sourceId, int targetId, int fxId = -1, int idx = 0, bool immediately = false)
    {
        _ = targetId;
        foreach (FxBundleGame bundle in _bundles)
        {
            if (bundle.SourceId != sourceId || bundle.Idx != idx)
                continue;
            if (fxId >= 0 && bundle.FxId != fxId)
                continue;
            bundle.Simulator.Stop(immediately);
        }
    }

    /// <summary>CN3FXMgr::StopMine — retire (immediately) every bundle the local player cast.</summary>
    public void StopMine(int localId)
    {
        foreach (FxBundleGame bundle in _bundles)
        {
            if (bundle.SourceId == localId)
                bundle.Simulator.Stop(true);
        }
    }

    /// <summary>CN3FXMgr::SetBundlePos — point the first matching bundle's dest at a world position.</summary>
    public void SetBundlePos(int fxId, int idx, Vector3 pos)
    {
        foreach (FxBundleGame bundle in _bundles)
        {
            if (bundle.FxId == fxId && bundle.Idx == idx)
            {
                bundle.DestPos = pos;
                return;
            }
        }
    }

    /// <summary>
    /// CN3FXMgr::Tick — age + evict unused origins, cull dead bundles (decrementing
    /// their origin's ref count), advance the survivors and the weather field.
    /// </summary>
    public void Tick(float secPerFrame, Vector3 cameraPos)
    {
        // Age + evict origins whose last live copy has died.
        var stale = new List<string>();
        foreach ((string key, BundleOrigin origin) in _origins)
        {
            if (origin.Num <= 0)
            {
                origin.LimitedTime += secPerFrame;
                if (origin.LimitedTime > OriginLimitedTime)
                    stale.Add(key);
            }
        }

        foreach (string key in stale)
            _origins.Remove(key);

        // Cull dead bundles, then advance the survivors.
        for (int i = _bundles.Count - 1; i >= 0; i--)
        {
            FxBundleGame bundle = _bundles[i];
            if (bundle.IsDead)
            {
                if (_origins.TryGetValue(bundle.CacheKey, out BundleOrigin? origin))
                    origin.Num--;
                _bundles.RemoveAt(i);
            }
        }

        foreach (FxBundleGame bundle in _bundles)
            bundle.Tick(secPerFrame, cameraPos);

        Weather.Update(secPerFrame, cameraPos);
    }

    /// <summary>CN3FXMgr::ClearAll — drop all live bundles and the origin cache.</summary>
    public void ClearAll()
    {
        _bundles.Clear();
        _origins.Clear();
    }

    /// <summary>Feed the WIZ_WEATHER handler: (re)create the weather field.</summary>
    public void SetWeather(WeatherType type, int amount) => Weather.Create(type, amount);

    private BundleOrigin? ResolveOrigin(int fxId)
    {
        if (!_loader.TryResolve(fxId, out string cacheKey, out uint soundId, out N3FXBundle bundle))
            return null;

        if (!_origins.TryGetValue(cacheKey, out BundleOrigin? origin))
        {
            origin = new BundleOrigin { Key = cacheKey, Bundle = bundle, SoundId = soundId };
            _origins[cacheKey] = origin;
        }

        return origin;
    }

    private FxBundleGame Spawn(BundleOrigin origin, int fxId, int sourceJoint, int idx, FxBundleAct moveType)
    {
        // Decorrelate each spawned copy's particle RNG stream, so overlapping casts
        // of the same effect do not march in lockstep.
        _seed += 0x9E3779B9u;
        return new FxBundleGame(origin.Bundle, _locator, 0x1234u + _seed)
        {
            FxId = fxId,
            Idx = idx,
            SourceJoint = sourceJoint,
            MoveType = moveType,
            CacheKey = origin.Key,
            SoundId = origin.SoundId,
        };
    }
}
