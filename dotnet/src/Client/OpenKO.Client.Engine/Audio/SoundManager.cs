using System.Numerics;

namespace OpenKO.Client.Engine.Audio;

/// <summary>
/// The device seam for the sound system — an <see cref="IAudioBackend"/> uploads
/// PCM buffers and plays them 2D/3D and moves the listener. The
/// <see cref="SoundManager"/> logic is testable against a fake backend; the
/// production one wraps MonoGame's OpenAL audio.
/// </summary>
public interface IAudioBackend
{
    /// <summary>False when no audio device could be opened (playback silently no-ops).</summary>
    bool IsAvailable { get; }

    /// <summary>Uploads PCM to a playable buffer, or null when unavailable.</summary>
    object? UploadBuffer(WavAudio audio);

    /// <summary>Plays a buffer (positional when <paramref name="type"/> is 3D).</summary>
    void Play(object buffer, SoundSettings settings, SoundType type, Vector3 position);

    /// <summary>Moves the 3D listener (CN3SndObj::SetListenerPos/Orientation).</summary>
    void SetListener(Vector3 position, Vector3 forward, Vector3 up);
}

/// <summary>
/// Port of CN3SndMgr: registers named sounds (the .tbl sound records), caches
/// their uploaded buffers and plays them 2D or 3D, and forwards the listener
/// pose. Pure orchestration over an <see cref="IAudioBackend"/>.
/// </summary>
public sealed class SoundManager(IAudioBackend backend)
{
    private sealed record Sound(object? Buffer, SoundType Type);

    private readonly Dictionary<string, Sound> _sounds = new(StringComparer.OrdinalIgnoreCase);

    public IAudioBackend Backend => backend;

    /// <summary>Registers (and uploads) a sound under a name.</summary>
    public void Register(string name, WavAudio audio, SoundType type)
        => _sounds[name] = new Sound(backend.UploadBuffer(audio), type);

    public bool IsRegistered(string name) => _sounds.ContainsKey(name);

    /// <summary>Plays a registered sound at a world position (ignored for 2D). Returns false if unplayable.</summary>
    public bool Play(string name, float gain, Vector3 position = default, bool loop = false)
    {
        if (!_sounds.TryGetValue(name, out Sound? sound) || sound.Buffer is null)
            return false;

        var settings = new SoundSettings { CurrentGain = gain, MaxGain = gain, IsLooping = loop };
        backend.Play(sound.Buffer, settings, sound.Type, position);
        return true;
    }

    public void SetListener(Vector3 position, Vector3 forward, Vector3 up)
        => backend.SetListener(position, forward, up);
}
