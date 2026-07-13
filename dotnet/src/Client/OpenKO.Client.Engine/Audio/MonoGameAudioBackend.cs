using Microsoft.Xna.Framework.Audio;
using OpenKO.Client.Engine.Interop;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenKO.Client.Engine.Audio;

/// <summary>
/// The production <see cref="IAudioBackend"/> over MonoGame's OpenAL audio
/// (the deferred stage-6 choice — MonoGame.DesktopGL bundles OpenAL Soft, giving
/// SoundEffect + AudioEmitter/AudioListener 3D). Every native call is guarded:
/// when no audio device can be opened (e.g. a headless container) the backend
/// reports <see cref="IsAvailable"/> = false and all playback silently no-ops.
/// </summary>
public sealed class MonoGameAudioBackend : IAudioBackend
{
    private readonly AudioListener _listener = new();
    private readonly AudioEmitter _emitter = new();
    private readonly List<SoundEffectInstance> _active = [];

    public MonoGameAudioBackend()
    {
        try
        {
            // Probe: constructing a tiny effect forces the OpenAL device open.
            using var probe = new SoundEffect(new byte[4], 8000, AudioChannels.Mono);
            IsAvailable = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; }

    public object? UploadBuffer(WavAudio audio)
    {
        if (!IsAvailable)
            return null;

        try
        {
            AudioChannels channels = audio.Channels >= 2 ? AudioChannels.Stereo : AudioChannels.Mono;
            return new SoundEffect(audio.Pcm, audio.SampleRate, channels);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Play(object buffer, SoundSettings settings, SoundType type, NumericsVector3 position)
    {
        if (!IsAvailable || buffer is not SoundEffect effect)
            return;

        try
        {
            PruneStopped();
            SoundEffectInstance instance = effect.CreateInstance();
            instance.Volume = Math.Clamp(settings.CurrentGain, 0f, 1f);
            instance.IsLooped = settings.IsLooping;

            if (type == SoundType.Sound3D)
            {
                _emitter.Position = position.ToXna();
                instance.Apply3D(_listener, _emitter);
            }

            instance.Play();
            _active.Add(instance);
        }
        catch (Exception)
        {
            // Playback failure is non-fatal.
        }
    }

    public void SetListener(NumericsVector3 position, NumericsVector3 forward, NumericsVector3 up)
    {
        _listener.Position = position.ToXna();
        _listener.Forward = forward.ToXna();
        _listener.Up = up.ToXna();
    }

    private void PruneStopped()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].State == SoundState.Stopped)
            {
                _active[i].Dispose();
                _active.RemoveAt(i);
            }
        }
    }
}
