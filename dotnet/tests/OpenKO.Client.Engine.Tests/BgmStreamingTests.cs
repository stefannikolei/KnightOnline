using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Audio;
using OpenKO.Client.Engine.Audio;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Stage-10.7 pins: NLayer MP3 decode + loop seek, sound.tbl BGM resolution, the
/// streaming buffer-queue (loop + fade), and graceful headless degradation.
/// </summary>
public class BgmStreamingTests
{
    // ---- Real MP3 decode (guarded — skips when the corpus is absent) --------

    private static string? FindBgmMp3()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string snd = Path.Combine(dir.FullName, "Client", "Data", "Snd");
            if (!Directory.Exists(snd))
                continue;

            // Prefer the town theme called out by the slice, else any BGM mp3.
            foreach (string name in new[] { "bgm_co_town.MP3", "BGM_KA_BATTLE.MP3", "bgm_castle.mp3" })
            {
                string p = Path.Combine(snd, name);
                if (File.Exists(p))
                    return p;
            }

            string? any = Directory
                .EnumerateFiles(snd, "*.*")
                .FirstOrDefault(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
            if (any != null)
                return any;
        }

        return null;
    }

    [Fact]
    public void Mp3Audio_DecodesRealFile_ToNonEmptyPcm()
    {
        string? path = FindBgmMp3();
        if (path == null)
            return; // corpus not available (e.g. CI)

        Mp3Audio audio = Mp3Audio.LoadFromFile(path);

        Assert.NotEmpty(audio.Pcm);
        Assert.Equal(16, audio.BitsPerSample);
        Assert.InRange(audio.Channels, 1, 2);
        Assert.InRange(audio.SampleRate, 8000, 48000);

        // 16-bit interleaved → the byte length is frame-aligned.
        Assert.Equal(0, audio.Pcm.Length % (audio.Channels * 2));
    }

    [Fact]
    public void Mp3StreamDecoder_SeekToStart_RewindsToFirstSamples()
    {
        string? path = FindBgmMp3();
        if (path == null)
            return;

        using IPcmStreamDecoder decoder = Mp3Audio.OpenStreamFromFile(path);
        var first = new byte[8192];
        int n1 = decoder.ReadPcm(first, 0, first.Length);
        Assert.True(n1 > 0);

        // Advance a bit, then rewind and re-read the opening bytes.
        var scratch = new byte[8192];
        decoder.ReadPcm(scratch, 0, scratch.Length);

        decoder.SeekToStart();
        var again = new byte[8192];
        int n2 = decoder.ReadPcm(again, 0, again.Length);

        Assert.Equal(n1, n2);
        Assert.Equal(first[..n1], again[..n2]);
    }

    // ---- BGM track resolution via a fake sound.tbl --------------------------

    /// <summary>Builds a plaintext __TABLE_SOUND (Dword id, String file, Int type, Int inst).</summary>
    private static SoundTable FakeSoundTable(params (uint Id, string File)[] rows)
    {
        var ms = new MemoryStream();
        void WriteInt(int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, v);
            ms.Write(b);
        }

        WriteInt(4); // column count
        WriteInt((int)TblType.Dword);
        WriteInt((int)TblType.String);
        WriteInt((int)TblType.Int);
        WriteInt((int)TblType.Int);
        WriteInt(rows.Length);
        foreach ((uint id, string file) in rows)
        {
            WriteInt((int)id);
            byte[] fb = Encoding.ASCII.GetBytes(file);
            WriteInt(fb.Length);
            ms.Write(fb);
            WriteInt(2);  // type
            WriteInt(1);  // numInst
        }

        return new SoundTable(N3TableFile.Load(ms.ToArray(), encrypted: false));
    }

    [Fact]
    public void ResolveBgm_MapsTrackIdToFileName_ViaSoundTable()
    {
        SoundTable table = FakeSoundTable(
            (BgmSelector.TownId, "Snd\\BGM_CO_TOWN.MP3"),
            (BgmSelector.KarusBattleId, "Snd\\BGM_KA_BATTLE.MP3"));
        var mgr = new SoundManager(new NullStreamBackend(), table);

        BgmTrack town = BgmSelector.Select(BgmNation.ElMorad, battle: false);
        BgmTrack battle = BgmSelector.Select(BgmNation.Karus, battle: true);

        // Lower-cased, matching the C++ client's pre-lookup normalisation.
        Assert.Equal("snd\\bgm_co_town.mp3", mgr.ResolveBgm(town));
        Assert.Equal("snd\\bgm_ka_battle.mp3", mgr.ResolveBgm(battle));
        Assert.Null(mgr.ResolveBgm(new BgmTrack(99999, "missing")));
    }

    [Fact]
    public void ResolveBgm_WithoutSoundTable_IsNull()
    {
        var mgr = new SoundManager(new NullStreamBackend());
        Assert.Null(mgr.ResolveBgm(BgmSelector.Select(BgmNation.None, false)));
    }

    // ---- Fade ramp math -----------------------------------------------------

    [Fact]
    public void BgmFade_Ramp_IsLinearAndClamped()
    {
        Assert.Equal(0f, BgmFade.Ramp(0f, 2f, 0f, 1f), 4);
        Assert.Equal(0.5f, BgmFade.Ramp(1f, 2f, 0f, 1f), 4);
        Assert.Equal(1f, BgmFade.Ramp(2f, 2f, 0f, 1f), 4);
        Assert.Equal(1f, BgmFade.Ramp(5f, 2f, 0f, 1f), 4);       // past the end → clamp to target
        Assert.Equal(1f, BgmFade.Ramp(0f, 0f, 0f, 1f), 4);       // zero duration snaps to target
        Assert.Equal(0.25f, BgmFade.Ramp(1.5f, 2f, 1f, 0f), 4);  // fade-out 1→0 at 3/4
    }

    // ---- Streaming buffer-queue: loop + fade --------------------------------

    [Fact]
    public void BgmStream_PrimesBuffers_AndLoopsAtEndOfStream()
    {
        var decoder = new FakeDecoder(readsBeforeEos: 1);
        var voice = new FakeVoice();
        var stream = new BgmStream(decoder, voice, loop: true, fadeInSeconds: 0f);

        stream.Start();

        // Primed the target ring, and had to rewind because the source is short.
        Assert.Equal(BgmStream.TargetQueuedBuffers, voice.QueuedBuffers.Count);
        Assert.True(decoder.SeekCount >= 1);
        Assert.True(voice.Playing);
        Assert.Equal(1f, voice.Volume, 4); // zero fade-in → immediately full
    }

    [Fact]
    public void BgmStream_NonLooping_StopsQueuingAtEndOfStream()
    {
        var decoder = new FakeDecoder(readsBeforeEos: 1);
        var voice = new FakeVoice();
        var stream = new BgmStream(decoder, voice, loop: false, fadeInSeconds: 0f);

        stream.Start();

        Assert.Equal(0, decoder.SeekCount);   // never rewinds
        Assert.Single(voice.QueuedBuffers);   // only the one available buffer
    }

    [Fact]
    public void BgmStream_FadesInThenOut_AndFinishes()
    {
        var decoder = new FakeDecoder(readsBeforeEos: 1000);
        var voice = new FakeVoice();
        var stream = new BgmStream(decoder, voice, loop: true, fadeInSeconds: 1f);

        stream.Start();
        Assert.Equal(0f, stream.Volume, 4);

        stream.Update(0.5f);
        Assert.Equal(0.5f, stream.Volume, 2);
        stream.Update(0.5f);
        Assert.Equal(1f, stream.Volume, 4);

        stream.BeginStop(1f);
        Assert.True(stream.Stopping);
        stream.Update(0.5f);
        Assert.Equal(0.5f, stream.Volume, 2);
        Assert.False(stream.Finished);

        stream.Update(0.5f);
        Assert.True(stream.Finished);
        Assert.True(voice.Stopped);
        Assert.Equal(0f, stream.Volume, 4);
    }

    // ---- SoundManager BGM API + headless no-op ------------------------------

    [Fact]
    public void SoundManager_PlayBgm_NoDevice_IsSilentNoOp()
    {
        // Backend returns no streaming voice (headless): PlayBgm must not throw and
        // must not leave a track "playing".
        var mgr = new SoundManager(
            new NullStreamBackend(),
            soundTable: null,
            bgmFileOpener: _ => new MemoryStream(new byte[16]),
            bgmDecoderFactory: _ => new FakeDecoder(readsBeforeEos: 5));

        mgr.PlayBgm("snd\\bgm.mp3");
        mgr.UpdateBgm(0.1f);
        mgr.StopBgm();

        Assert.Null(mgr.CurrentBgm);
    }

    [Fact]
    public void SoundManager_PlayBgm_WithDevice_StreamsAndStops()
    {
        var backend = new FakeStreamBackend();
        var mgr = new SoundManager(
            backend,
            soundTable: null,
            bgmFileOpener: _ => new MemoryStream(new byte[16]),
            bgmDecoderFactory: _ => new FakeDecoder(readsBeforeEos: 1000));

        mgr.PlayBgm("snd\\bgm_co_town.mp3", loop: true, fadeInSeconds: 0f);
        Assert.Equal("snd\\bgm_co_town.mp3", mgr.CurrentBgm);
        Assert.NotNull(backend.LastVoice);
        Assert.True(backend.LastVoice!.Playing);
        Assert.Equal(BgmStream.TargetQueuedBuffers, backend.LastVoice.QueuedBuffers.Count);

        mgr.UpdateBgm(0.1f); // pump refill/fade
        mgr.StopBgm(0f);     // instant stop
        mgr.UpdateBgm(0.0f);

        Assert.Null(mgr.CurrentBgm);
        Assert.True(backend.LastVoice.Stopped);
    }

    // ---- Stage-11.2: settings gates + volume --------------------------------

    [Fact]
    public void SoundManager_BgmDisabled_PlayBgmIsNoOp()
    {
        var backend = new FakeStreamBackend();
        var mgr = new SoundManager(
            backend,
            soundTable: null,
            bgmFileOpener: _ => new MemoryStream(new byte[16]),
            bgmDecoderFactory: _ => new FakeDecoder(readsBeforeEos: 1000))
        {
            BgmEnabled = false,
        };

        mgr.PlayBgm("snd\\bgm_co_town.mp3", loop: true, fadeInSeconds: 0f);

        Assert.Null(mgr.CurrentBgm);
        Assert.Null(backend.LastVoice); // never opened a voice
    }

    [Fact]
    public void SoundManager_BgmVolume_IsTheFadeInTarget()
    {
        var backend = new FakeStreamBackend();
        var mgr = new SoundManager(
            backend,
            soundTable: null,
            bgmFileOpener: _ => new MemoryStream(new byte[16]),
            bgmDecoderFactory: _ => new FakeDecoder(readsBeforeEos: 1000))
        {
            BgmVolume = 0.3f,
        };

        // Zero fade-in snaps straight to the configured BGM volume, not 1.0.
        mgr.PlayBgm("snd\\bgm_co_town.mp3", loop: true, fadeInSeconds: 0f);

        Assert.NotNull(backend.LastVoice);
        Assert.Equal(0.3f, backend.LastVoice!.Volume, 4);
    }

    private static WavAudio SilentWav() =>
        new() { Channels = 1, SampleRate = 44100, BitsPerSample = 16, Pcm = new byte[8] };

    [Fact]
    public void SoundManager_SfxDisabled_PlayReturnsFalse()
    {
        var backend = new FakeStreamBackend();
        var mgr = new SoundManager(backend) { SfxEnabled = false };
        mgr.Register("hit", SilentWav(), SoundType.Sound2D);

        Assert.False(mgr.Play("hit", 1f));
        Assert.Null(backend.LastPlaySettings);
    }

    [Fact]
    public void SoundManager_SfxVolume_ScalesTheGain()
    {
        var backend = new FakeStreamBackend();
        var mgr = new SoundManager(backend) { SfxVolume = 0.5f };
        mgr.Register("hit", SilentWav(), SoundType.Sound2D);

        Assert.True(mgr.Play("hit", 0.8f));
        Assert.NotNull(backend.LastPlaySettings);
        Assert.Equal(0.4f, backend.LastPlaySettings!.CurrentGain, 4);
        Assert.Equal(0.4f, backend.LastPlaySettings!.MaxGain, 4);
    }

    // ---- Fakes --------------------------------------------------------------

    /// <summary>A decoder that yields a fixed number of full reads, then EOS; seek rewinds.</summary>
    private sealed class FakeDecoder(int readsBeforeEos) : IPcmStreamDecoder
    {
        private int _reads;

        public int SampleRate => 44100;

        public int Channels => 2;

        public int SeekCount { get; private set; }

        public int ReadPcm(byte[] destination, int offset, int count)
        {
            if (_reads >= readsBeforeEos)
                return 0;
            _reads++;
            Array.Clear(destination, offset, count);
            destination[offset] = 0x7F; // a recognisable non-zero sample
            return count;
        }

        public void SeekToStart()
        {
            SeekCount++;
            _reads = 0;
        }

        public void Dispose() { }
    }

    private sealed class FakeVoice : IStreamingVoice
    {
        public List<byte[]> QueuedBuffers { get; } = [];

        public bool Playing { get; private set; }

        public bool Stopped { get; private set; }

        public int PendingBufferCount => QueuedBuffers.Count;

        public float Volume { get; set; }

        public void QueuePcm(byte[] pcm, int count)
        {
            // Copy synchronously — mirrors DynamicSoundEffectInstance.SubmitBuffer so the
            // producer's reused chunk buffer is safe.
            QueuedBuffers.Add(pcm[..count]);
        }

        public void Play() => Playing = true;

        public void Stop()
        {
            Playing = false;
            Stopped = true;
        }

        public void Dispose() { }
    }

    /// <summary>Backend with a working streaming voice (a "device").</summary>
    private sealed class FakeStreamBackend : IAudioBackend
    {
        public FakeVoice? LastVoice { get; private set; }

        public SoundSettings? LastPlaySettings { get; private set; }

        public bool IsAvailable => true;

        public object? UploadBuffer(WavAudio audio) => new object();

        public void Play(object buffer, SoundSettings settings, SoundType type, Vector3 position)
            => LastPlaySettings = settings;

        public void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }

        public IStreamingVoice? OpenStreamingVoice(int sampleRate, int channels)
        {
            LastVoice = new FakeVoice();
            return LastVoice;
        }
    }

    /// <summary>Backend with no streaming voice (headless) — uses the interface default.</summary>
    private sealed class NullStreamBackend : IAudioBackend
    {
        public bool IsAvailable => false;

        public object? UploadBuffer(WavAudio audio) => null;

        public void Play(object buffer, SoundSettings settings, SoundType type, Vector3 position) { }

        public void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }

        // OpenStreamingVoice uses the interface default (returns null).
    }
}
