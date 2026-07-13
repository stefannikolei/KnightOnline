using System.Numerics;

namespace OpenKO.Client.Engine.Audio;

/// <summary>
/// The OpenAL 3D attenuation the C++ N3SndMgr relies on: the inverse-distance
/// clamped model. Pure math so the spatial falloff is headless-testable
/// (the device backend hands the same emitter/listener to OpenAL/MonoGame).
/// </summary>
public static class Audio3D
{
    /// <summary>
    /// AL_INVERSE_DISTANCE_CLAMPED gain for a source at
    /// <paramref name="distance"/> metres. Distance is clamped to
    /// [<paramref name="referenceDistance"/>, <paramref name="maxDistance"/>]
    /// first; the result is in [0..1].
    /// </summary>
    public static float Attenuation(
        float distance, float referenceDistance, float maxDistance, float rolloffFactor)
    {
        float d = Math.Clamp(distance, referenceDistance, maxDistance);
        float denom = referenceDistance + rolloffFactor * (d - referenceDistance);
        if (denom <= 0f)
            return 1f;
        return referenceDistance / denom;
    }

    /// <summary>The effective gain heard for an emitter, folding in the source gain.</summary>
    public static float EffectiveGain(
        Vector3 listener, Vector3 emitter, float sourceGain,
        float referenceDistance, float maxDistance, float rolloffFactor)
    {
        float distance = Vector3.Distance(listener, emitter);
        return sourceGain * Attenuation(distance, referenceDistance, maxDistance, rolloffFactor);
    }
}
