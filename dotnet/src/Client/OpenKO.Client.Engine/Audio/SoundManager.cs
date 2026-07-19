using System.Numerics;
using OpenKO.Client.Assets.Audio;

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

    /// <summary>
    /// Opens a hardware streaming voice (the BGM buffer-queue sink, SNDTYPE_STREAM).
    /// Returns null when no audio device is available (headless) — the default
    /// implementation is a silent no-op so existing backends keep compiling.
    /// </summary>
    IStreamingVoice? OpenStreamingVoice(int sampleRate, int channels) => null;
}

/// <summary>
/// Port of CN3SndMgr: registers named sounds (the .tbl sound records), caches
/// their uploaded buffers and plays them 2D or 3D, and forwards the listener
/// pose. Pure orchestration over an <see cref="IAudioBackend"/>.
/// </summary>
public sealed class SoundManager
{
    private sealed record Sound(object? Buffer, SoundType Type);

    private readonly IAudioBackend _backend;
    private readonly SoundTable? _soundTable;
    private readonly Func<string, Stream?>? _bgmFileOpener;
    private readonly Func<Stream, IPcmStreamDecoder> _bgmDecoderFactory;
    private readonly Dictionary<string, Sound> _sounds = new(StringComparer.OrdinalIgnoreCase);

    private BgmStream? _bgm;
    private string? _currentBgmFile;

    /// <summary>
    /// Music on/off (Option.ini <c>Sound/Bgm</c>). When false, <see cref="PlayBgm(string,bool,float)"/>
    /// is a no-op and any playing BGM is left to fade/finish. Applied from <c>GameSettings</c>.
    /// </summary>
    public bool BgmEnabled { get; set; } = true;

    /// <summary>
    /// Sound effects on/off (Option.ini <c>Sound/Effect</c>). When false, <see cref="Play"/> is a
    /// no-op. Applied from <c>GameSettings</c>.
    /// </summary>
    public bool SfxEnabled { get; set; } = true;

    /// <summary>BGM volume [0..1] — the fade-in target for a newly started stream. Applied from <c>GameSettings</c>.</summary>
    public float BgmVolume { get; set; } = 1f;

    /// <summary>SFX volume [0..1] — scales the per-play gain. Applied from <c>GameSettings</c>.</summary>
    public float SfxVolume { get; set; } = 1f;

    /// <summary>
    /// Creates the sound manager.
    /// </summary>
    /// <param name="backend">The device seam (uploads/plays PCM).</param>
    /// <param name="soundTable">
    /// Optional <c>sound.tbl</c> for <see cref="ResolveBgm"/> (id → filename). Omit for
    /// the pure SFX manager or the tests that don't drive BGM.
    /// </param>
    /// <param name="bgmFileOpener">
    /// Opens a BGM filename (from <see cref="ResolveBgm"/> / <see cref="PlayBgm(string,bool,float)"/>)
    /// to a readable stream, or null if the file is absent. The executable wires this to
    /// its <c>KoPathResolver</c>; when omitted, <see cref="PlayBgm(string,bool,float)"/> is a no-op.
    /// </param>
    /// <param name="bgmDecoderFactory">
    /// Turns an opened stream into a PCM decoder. Defaults to the NLayer MP3 decoder
    /// (<see cref="Mp3Audio.OpenStream"/>); override to mock decoding in tests.
    /// </param>
    public SoundManager(
        IAudioBackend backend,
        SoundTable? soundTable = null,
        Func<string, Stream?>? bgmFileOpener = null,
        Func<Stream, IPcmStreamDecoder>? bgmDecoderFactory = null)
    {
        _backend = backend;
        _soundTable = soundTable;
        _bgmFileOpener = bgmFileOpener;
        _bgmDecoderFactory = bgmDecoderFactory ?? Mp3Audio.OpenStream;
    }

    private IAudioBackend backend => _backend;

    public IAudioBackend Backend => _backend;

    /// <summary>The BGM file currently playing (its resolved filename), or null.</summary>
    public string? CurrentBgm => _currentBgmFile;

    /// <summary>Registers (and uploads) a sound under a name.</summary>
    public void Register(string name, WavAudio audio, SoundType type)
        => _sounds[name] = new Sound(backend.UploadBuffer(audio), type);

    public bool IsRegistered(string name) => _sounds.ContainsKey(name);

    /// <summary>Plays a registered sound at a world position (ignored for 2D). Returns false if unplayable.</summary>
    public bool Play(string name, float gain, Vector3 position = default, bool loop = false)
    {
        if (!SfxEnabled)
            return false;
        if (!_sounds.TryGetValue(name, out Sound? sound) || sound.Buffer is null)
            return false;

        float scaled = gain * SfxVolume;
        var settings = new SoundSettings { CurrentGain = scaled, MaxGain = scaled, IsLooping = loop };
        backend.Play(sound.Buffer, settings, sound.Type, position);
        return true;
    }

    public void SetListener(Vector3 position, Vector3 forward, Vector3 up)
        => backend.SetListener(position, forward, up);

    // ---- Streaming BGM (SNDTYPE_STREAM) --------------------------------------

    /// <summary>
    /// Resolves a selected <see cref="BgmTrack"/> to its wave/MP3 filename via the
    /// injected <c>sound.tbl</c> (id → <c>szFN</c>), mirroring how the C++ client looks
    /// a sound id up in <c>__TABLE_SOUND</c> before <c>CreateStreamObj</c>. Returns null
    /// when no <c>sound.tbl</c> was injected or the id is unknown. The filename is
    /// returned lower-cased (the C++ client lower-cases before lookup).
    /// </summary>
    public string? ResolveBgm(BgmTrack track)
    {
        if (_soundTable != null && _soundTable.TryGet((uint)track.Id, out SoundRow row)
            && !string.IsNullOrWhiteSpace(row.FileName))
        {
            return row.FileName.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>
    /// Starts streaming a BGM file (decoded on the fly, buffer-queued, looping by
    /// default with a linear fade-in). Any BGM already playing is faded out first.
    /// A no-op when no file opener was injected, the file is absent, or no audio device
    /// is available (headless) — never throws.
    /// <para>
    /// <b>Executable wiring (KnightOnlineGame.cs):</b> on a zone/battle change, replace
    /// the current <c>BgmSelector</c>-only log with:
    /// <code>
    /// BgmTrack track = BgmSelector.Select(nation, IsNearHostile(), _context.Spawn.Zone);
    /// _sound.PlayBgm(_sound.ResolveBgm(track) ?? $"snd\\{track.Name}.mp3", loop: true);
    /// </code>
    /// and call <c>_sound.UpdateBgm((float)gameTime.ElapsedGameTime.TotalSeconds);</c>
    /// once per frame in Update.
    /// </para>
    /// </summary>
    public void PlayBgm(string fileName, bool loop = true, float fadeInSeconds = 1f)
    {
        if (!BgmEnabled || string.IsNullOrWhiteSpace(fileName) || _bgmFileOpener == null)
            return;

        // Already streaming this exact file and not fading out → leave it.
        if (_bgm is { Finished: false, Stopping: false }
            && string.Equals(_currentBgmFile, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Stream? stream;
        try
        {
            stream = _bgmFileOpener(fileName);
        }
        catch (Exception)
        {
            stream = null;
        }

        if (stream == null)
            return;

        IPcmStreamDecoder decoder;
        try
        {
            decoder = _bgmDecoderFactory(stream);
        }
        catch (Exception)
        {
            stream.Dispose();
            return;
        }

        IStreamingVoice? voice = backend.OpenStreamingVoice(decoder.SampleRate, decoder.Channels);
        if (voice == null)
        {
            // Headless / no device: decode isn't wasted on a stream we can't hear.
            decoder.Dispose();
            return;
        }

        // Swap out the previous stream immediately (hard cut — the new track fades in
        // toward the configured BGM volume).
        _bgm?.Dispose();
        _bgm = new BgmStream(decoder, voice, loop, fadeInSeconds, maxVolume: BgmVolume);
        _currentBgmFile = fileName;
        _bgm.Start();
    }

    /// <summary>
    /// Fades the current BGM out over <paramref name="fadeOutSeconds"/> and stops it.
    /// Safe to call with nothing playing.
    /// </summary>
    public void StopBgm(float fadeOutSeconds = 1f)
    {
        _bgm?.BeginStop(fadeOutSeconds);
    }

    /// <summary>
    /// Advances the BGM fade ramp and refills the buffer queue. Call once per frame
    /// with the frame delta (seconds). Disposes the stream once a fade-out completes.
    /// </summary>
    public void UpdateBgm(float deltaSeconds)
    {
        if (_bgm == null)
            return;

        _bgm.Update(deltaSeconds);
        if (_bgm.Finished)
        {
            _bgm.Dispose();
            _bgm = null;
            _currentBgmFile = null;
        }
    }
}
