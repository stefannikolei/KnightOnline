using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Fx;

namespace OpenKO.Client.Game.Tests;

/// <summary>A settable fake for <see cref="IFxEntityLocator"/> (entity id → position).</summary>
internal sealed class FakeLocator : IFxEntityLocator
{
    private readonly Dictionary<int, Vector3> _positions = [];

    public FakeLocator Set(int entityId, Vector3 pos)
    {
        _positions[entityId] = pos;
        return this;
    }

    public bool TryGetPosition(int entityId, int joint, out Vector3 pos) =>
        _positions.TryGetValue(entityId, out pos);
}

/// <summary>
/// A fake <see cref="IFxBundleLoader"/> that hands back one synthetic bundle per
/// FXID, keyed by <c>"fx{id}"</c> so the manager's origin cache can be exercised.
/// Unknown ids (not registered) resolve to false.
/// </summary>
internal sealed class FakeBundleLoader : IFxBundleLoader
{
    private readonly Dictionary<int, (string Key, uint SoundId, N3FXBundle Bundle)> _bundles = [];

    /// <summary>Register an FXID → bundle mapping. A shared <paramref name="key"/> dedupes origins.</summary>
    public FakeBundleLoader Add(int fxId, float life0 = 0f, float velocity = 10f, string? key = null, uint soundId = 0)
    {
        _bundles[fxId] = (key ?? $"fx{fxId}", soundId, FxTestBundles.Build(life0, velocity));
        return this;
    }

    public bool TryResolve(int fxId, out string cacheKey, out uint soundId, out N3FXBundle bundle)
    {
        if (_bundles.TryGetValue(fxId, out (string Key, uint SoundId, N3FXBundle Bundle) entry))
        {
            cacheKey = entry.Key;
            soundId = entry.SoundId;
            bundle = entry.Bundle;
            return true;
        }

        cacheKey = string.Empty;
        soundId = 0;
        bundle = null!;
        return false;
    }
}

/// <summary>Builds tiny synthetic <see cref="N3FXBundle"/>s for the FX manager/bundle tests.</summary>
internal static class FxTestBundles
{
    /// <summary>
    /// A one-particle-part bundle. <paramref name="life0"/> 0 = infinite (the bundle
    /// only dies on Stop); &gt; 0 bounds the lifetime so the manager culls it. The
    /// emitter's own Life is 0 (infinite) so a part never dies on its own.
    /// </summary>
    public static N3FXBundle Build(float life0, float velocity)
    {
        var bundle = new N3FXBundle
        {
            Life0 = life0,
            Velocity = velocity,
            DependScale = false,
        };

        bundle.Parts[0] = new N3FXBundlePart
        {
            StartTime = 0f,
            Part = new N3FXPartParticles
            {
                Type = FxPartType.Particle,
                Life = 0f, // infinite emitter — the part never retires on its own
                NumParticle = 4,
                NumCreate = 1,
                CreateDelay = 0.05f,
                ParticleLifeMin = 1f,
                ParticleLifeMax = 1f,
                ParticleSizeMin = 1f,
                ParticleSizeMax = 1f,
                EmitType = FxPartParticleEmitType.Spread,
                EmitCondition = new ParticleEmitCondition { EmitAngle = 0f },
                PtEmitDir = new Vector3(0f, 0f, 1f),
                PtVelocity = 1f,
                NumTex = 1,
                TexFps = 1f,
                FadeIn = 0f,
                FadeOut = 0f,
                MinCreateRange = Vector3.Zero,
                MaxCreateRange = Vector3.Zero,
            },
        };

        return bundle;
    }
}
