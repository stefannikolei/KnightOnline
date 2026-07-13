namespace OpenKO.Client.Engine.Audio;

/// <summary>e_SndType (N3SndDef.h): playback behaviour of a sound.</summary>
public enum SoundType
{
    Sound2D = 0,   // SNDTYPE_2D — UI/global, unspatialised
    Sound3D = 1,   // SNDTYPE_3D — positional
    Stream = 2,    // SNDTYPE_STREAM — decoded on the fly (BGM); mpg123 deferred
    Unknown = 3,
}

/// <summary>SoundSettings (N3SndDef.h): per-sound volume + loop state.</summary>
public sealed class SoundSettings
{
    public bool IsLooping { get; set; }

    /// <summary>Current gain [0..1].</summary>
    public float CurrentGain { get; set; }

    /// <summary>Target/maximum gain [0..1].</summary>
    public float MaxGain { get; set; } = 1.0f;
}
