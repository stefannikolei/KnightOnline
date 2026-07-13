using System.Buffers.Binary;
using System.Numerics;
using OpenKO.Client.Engine.Audio;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-7.7 pins: WAV decode, 3D attenuation and the sound manager.</summary>
public class AudioTests
{
    private static byte[] BuildWav(short channels, int sampleRate, short bits, byte[] pcm)
    {
        int byteRate = sampleRate * channels * (bits / 8);
        short blockAlign = (short)(channels * (bits / 8));
        var buffer = new byte[44 + pcm.Length];
        var span = buffer.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + pcm.Length);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bits);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], pcm.Length);
        pcm.CopyTo(span[44..]);
        return buffer;
    }

    [Fact]
    public void WavAudio_ParsesPcmChunk()
    {
        byte[] pcm = [1, 2, 3, 4, 5, 6, 7, 8];
        WavAudio wav = WavAudio.Load(BuildWav(2, 44100, 16, pcm));

        Assert.Equal(2, wav.Channels);
        Assert.Equal(44100, wav.SampleRate);
        Assert.Equal(16, wav.BitsPerSample);
        Assert.Equal(pcm, wav.Pcm);
    }

    [Fact]
    public void WavAudio_RejectsNonRiff()
    {
        Assert.Throws<InvalidDataException>(() => WavAudio.Load(new byte[16]));
    }

    [Fact]
    public void Attenuation_ClampsAndFallsOff()
    {
        // Within the reference distance → full gain.
        Assert.Equal(1f, Audio3D.Attenuation(1f, referenceDistance: 5f, maxDistance: 100f, rolloffFactor: 1f), 4);

        // At 2× reference with rolloff 1 → ref / (ref + (d-ref)) = 5/10 = 0.5.
        Assert.Equal(0.5f, Audio3D.Attenuation(10f, 5f, 100f, 1f), 4);

        // Beyond max distance → clamped (no further falloff).
        float atMax = Audio3D.Attenuation(100f, 5f, 100f, 1f);
        float beyond = Audio3D.Attenuation(1000f, 5f, 100f, 1f);
        Assert.Equal(atMax, beyond, 5);
    }

    [Fact]
    public void EffectiveGain_FoldsSourceGainAndDistance()
    {
        float gain = Audio3D.EffectiveGain(
            listener: Vector3.Zero, emitter: new Vector3(10, 0, 0), sourceGain: 0.8f,
            referenceDistance: 5f, maxDistance: 100f, rolloffFactor: 1f);
        Assert.Equal(0.4f, gain, 4); // 0.8 * 0.5
    }

    private sealed class FakeBackend : IAudioBackend
    {
        public List<(SoundType Type, float Gain, Vector3 Pos, bool Loop)> Plays { get; } = [];

        public Vector3 ListenerPosition { get; private set; }

        public bool IsAvailable => true;

        public object? UploadBuffer(WavAudio audio) => new object();

        public void Play(object buffer, SoundSettings settings, SoundType type, Vector3 position)
            => Plays.Add((type, settings.CurrentGain, position, settings.IsLooping));

        public void SetListener(Vector3 position, Vector3 forward, Vector3 up) => ListenerPosition = position;
    }

    [Fact]
    public void SoundManager_RegistersAndPlays()
    {
        var backend = new FakeBackend();
        var mgr = new SoundManager(backend);
        WavAudio wav = WavAudio.Load(BuildWav(1, 22050, 16, [0, 0, 0, 0]));

        mgr.Register("bgm", wav, SoundType.Stream);
        mgr.Register("hit", wav, SoundType.Sound3D);
        Assert.True(mgr.IsRegistered("bgm"));

        Assert.True(mgr.Play("bgm", 0.7f, loop: true));
        Assert.True(mgr.Play("hit", 1.0f, new Vector3(3, 0, 4)));
        Assert.False(mgr.Play("missing", 1f));

        Assert.Equal(2, backend.Plays.Count);
        Assert.Equal(SoundType.Stream, backend.Plays[0].Type);
        Assert.True(backend.Plays[0].Loop);
        Assert.Equal(new Vector3(3, 0, 4), backend.Plays[1].Pos);

        mgr.SetListener(new Vector3(1, 2, 3), -Vector3.UnitZ, Vector3.UnitY);
        Assert.Equal(new Vector3(1, 2, 3), backend.ListenerPosition);
    }

    [Fact]
    public void SoundManager_UnplayableBufferReturnsFalse()
    {
        // A backend that cannot upload (no device) → Play is a safe no-op.
        var mgr = new SoundManager(new NullBackend());
        mgr.Register("x", WavAudio.Load(BuildWav(1, 8000, 16, [0, 0])), SoundType.Sound2D);
        Assert.False(mgr.Play("x", 1f));
    }

    private sealed class NullBackend : IAudioBackend
    {
        public bool IsAvailable => false;

        public object? UploadBuffer(WavAudio audio) => null;

        public void Play(object buffer, SoundSettings settings, SoundType type, Vector3 position) { }

        public void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }
    }
}
