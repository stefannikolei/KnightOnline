namespace OpenKO.Client.Engine.Audio;

/// <summary>
/// A hardware streaming voice fed a queue of PCM buffers — the seam over MonoGame's
/// <c>DynamicSoundEffectInstance</c>, mirroring the mpg123 buffer-queue in
/// <c>StreamedAudioHandle</c> (submit buffers, refill as they drain). Kept an
/// interface so <see cref="BgmStream"/> is testable against a fake voice with no
/// audio device.
/// </summary>
public interface IStreamingVoice : IDisposable
{
    /// <summary>Buffers submitted but not yet played out (drives the refill decision).</summary>
    int PendingBufferCount { get; }

    /// <summary>Playback volume [0..1] (the fade ramp writes this).</summary>
    float Volume { get; set; }

    /// <summary>Submits <paramref name="count"/> bytes of interleaved 16-bit PCM to the queue.</summary>
    void QueuePcm(byte[] pcm, int count);

    /// <summary>Starts (or resumes) playback of the queued buffers.</summary>
    void Play();

    /// <summary>Stops playback and drops the queued buffers.</summary>
    void Stop();
}

/// <summary>Pure linear volume-ramp math for BGM fade-in / fade-out (CN3SndObj fade fields).</summary>
public static class BgmFade
{
    /// <summary>
    /// Linear ramp from <paramref name="from"/> to <paramref name="to"/> over
    /// <paramref name="duration"/> seconds, evaluated at <paramref name="elapsed"/>.
    /// A non-positive duration snaps straight to <paramref name="to"/>; the result is
    /// always clamped to the [from,to] segment.
    /// </summary>
    public static float Ramp(float elapsed, float duration, float from, float to)
    {
        if (duration <= 0f || elapsed >= duration)
            return to;
        if (elapsed <= 0f)
            return from;

        float t = elapsed / duration;
        return from + (to - from) * t;
    }
}

/// <summary>
/// Port of <c>StreamedAudioHandle</c> (Client/N3Base/AudioDecoderThread.cpp): the
/// buffer-queue BGM consumer. It keeps a small ring of decoded PCM buffers submitted
/// to an <see cref="IStreamingVoice"/>, refilling from an <see cref="IPcmStreamDecoder"/>
/// as they drain and rewinding the decoder to loop at end-of-stream. Volume follows a
/// linear fade-in on start and a fade-out on <see cref="BeginStop"/> (the start-delay
/// is folded into the fade-in). Pure orchestration — no MonoGame types — so it unit
/// tests against a fake decoder + fake voice.
/// </summary>
public sealed class BgmStream : IDisposable
{
    /// <summary>Target number of queued buffers to keep ahead of the play cursor.</summary>
    public const int TargetQueuedBuffers = 3;

    private readonly IPcmStreamDecoder _decoder;
    private readonly IStreamingVoice _voice;
    private readonly bool _loop;
    private readonly byte[] _chunk;
    private readonly float _fadeInSeconds;

    private float _fadeElapsed;
    private float _fadeFrom;
    private float _fadeTo = 1f;
    private float _fadeDuration;
    private bool _stopping;
    private bool _endOfStream;

    /// <summary>
    /// Creates the consumer. <paramref name="chunkFrames"/> is the number of sample
    /// frames per submitted buffer (bytes = frames × channels × 2). The default gives
    /// roughly a fifth of a second at 44.1 kHz stereo.
    /// </summary>
    public BgmStream(
        IPcmStreamDecoder decoder,
        IStreamingVoice voice,
        bool loop = true,
        float fadeInSeconds = 1f,
        int chunkFrames = 8192)
    {
        _decoder = decoder;
        _voice = voice;
        _loop = loop;
        _fadeInSeconds = Math.Max(0f, fadeInSeconds);

        int channels = Math.Max(1, decoder.Channels);
        ChunkBytes = Math.Max(channels * 2, chunkFrames * channels * 2);
        _chunk = new byte[ChunkBytes];

        // Prime the fade-in (0 → 1). A zero fade snaps to full volume immediately.
        _fadeFrom = 0f;
        _fadeTo = 1f;
        _fadeDuration = _fadeInSeconds;
        _voice.Volume = _fadeInSeconds > 0f ? 0f : 1f;
    }

    /// <summary>Bytes per submitted PCM buffer.</summary>
    public int ChunkBytes { get; }

    /// <summary>Current fade volume applied to the voice [0..1].</summary>
    public float Volume => _voice.Volume;

    /// <summary>True once a fade-out has completed and playback has been stopped.</summary>
    public bool Finished { get; private set; }

    /// <summary>True while fading out toward a stop.</summary>
    public bool Stopping => _stopping;

    /// <summary>Primes the initial buffers and starts playback.</summary>
    public void Start()
    {
        Refill();
        _voice.Play();
    }

    /// <summary>
    /// Advances the fade ramp and tops the buffer queue back up to
    /// <see cref="TargetQueuedBuffers"/>. Call once per frame with the frame delta.
    /// </summary>
    public void Update(float deltaSeconds)
    {
        if (Finished)
            return;

        if (_fadeDuration > 0f || _stopping)
        {
            _fadeElapsed += Math.Max(0f, deltaSeconds);
            _voice.Volume = BgmFade.Ramp(_fadeElapsed, _fadeDuration, _fadeFrom, _fadeTo);
        }

        if (_stopping)
        {
            if (_fadeElapsed >= _fadeDuration)
            {
                _voice.Stop();
                Finished = true;
            }

            return; // Do not refill while fading out.
        }

        Refill();
    }

    /// <summary>Begins a fade-out; playback stops (and <see cref="Finished"/> flips) when it completes.</summary>
    public void BeginStop(float fadeOutSeconds)
    {
        if (_stopping || Finished)
            return;

        _stopping = true;
        _fadeFrom = _voice.Volume;
        _fadeTo = 0f;
        _fadeDuration = Math.Max(0f, fadeOutSeconds);
        _fadeElapsed = 0f;

        if (_fadeDuration <= 0f)
        {
            _voice.Volume = 0f;
            _voice.Stop();
            Finished = true;
        }
    }

    private void Refill()
    {
        while (!_endOfStream && _voice.PendingBufferCount < TargetQueuedBuffers)
        {
            int read = _decoder.ReadPcm(_chunk, 0, _chunk.Length);
            if (read <= 0)
            {
                if (_loop)
                {
                    _decoder.SeekToStart();
                    read = _decoder.ReadPcm(_chunk, 0, _chunk.Length);
                    if (read <= 0)
                    {
                        _endOfStream = true; // Empty even after rewind — give up.
                        break;
                    }
                }
                else
                {
                    _endOfStream = true;
                    break;
                }
            }

            _voice.QueuePcm(_chunk, read);
        }
    }

    public void Dispose()
    {
        try
        {
            _voice.Stop();
        }
        catch (Exception)
        {
            // Tearing down must not throw.
        }

        _voice.Dispose();
        _decoder.Dispose();
    }
}
