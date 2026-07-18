using NLayer;

namespace OpenKO.Client.Engine.Audio;

/// <summary>
/// The pure/mockable seam a streaming voice pulls 16-bit little-endian PCM from —
/// mirrors the mpg123 read loop inside <c>StreamedAudioHandle</c>
/// (Client/N3Base/AudioDecoderThread.cpp <c>decode_impl_mp3</c>): pull N bytes of
/// PCM at a time, and rewind to the file start to loop at end-of-stream. Keeping
/// this an interface lets <see cref="BgmStream"/> be unit-tested with a fake
/// decoder (no NLayer / no real MP3).
/// </summary>
public interface IPcmStreamDecoder : IDisposable
{
    /// <summary>Output sample rate (Hz).</summary>
    int SampleRate { get; }

    /// <summary>Output channel count (1 = mono, 2 = stereo).</summary>
    int Channels { get; }

    /// <summary>
    /// Fills <paramref name="destination"/> with up to <paramref name="count"/> bytes
    /// of interleaved 16-bit little-endian PCM starting at <paramref name="offset"/>.
    /// Returns the number of bytes written; 0 means end-of-stream.
    /// </summary>
    int ReadPcm(byte[] destination, int offset, int count);

    /// <summary>Rewind to the first sample (the loop point at EOS).</summary>
    void SeekToStart();
}

/// <summary>
/// Fully-decoded MP3 PCM — the streaming counterpart to <see cref="WavAudio"/>
/// (same shape: sample rate, channels, 16-bit little-endian PCM). Decodes the KO
/// BGM assets (<c>Snd\bgm_*.mp3</c>) with the managed NLayer decoder, replacing the
/// C++ mpg123 path (<c>N3SndMgr::CreateStreamObj</c> SNDTYPE_STREAM). Prefer the
/// streaming <see cref="Mp3StreamDecoder"/> for BGM playback — this full decode is
/// used when the whole clip is wanted at once (e.g. verification/tests).
/// </summary>
public sealed class Mp3Audio
{
    public required int Channels { get; init; }

    public required int SampleRate { get; init; }

    /// <summary>Always 16 — NLayer's <c>ReadSamplesInt16</c> output.</summary>
    public int BitsPerSample => 16;

    /// <summary>Interleaved 16-bit little-endian PCM (what OpenAL/MonoGame consume).</summary>
    public required byte[] Pcm { get; init; }

    /// <summary>Fully decodes an MP3 stream to interleaved 16-bit PCM.</summary>
    public static Mp3Audio Load(Stream stream)
    {
        using var file = new MpegFile(stream);
        int channels = Math.Max(1, file.Channels);
        int sampleRate = file.SampleRate;

        // NLayer's Length is the total sample count (all channels); 2 bytes each.
        // It can be -1 for non-seekable/unknown streams — fall back to growing.
        long totalSamples = file.Length;
        int initialBytes = totalSamples > 0 ? checked((int)(totalSamples * 2)) : 64 * 1024;

        using var ms = new MemoryStream(initialBytes);
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = file.ReadSamplesInt16(chunk, 0, chunk.Length)) > 0)
            ms.Write(chunk, 0, read);

        return new Mp3Audio
        {
            Channels = channels,
            SampleRate = sampleRate,
            Pcm = ms.ToArray(),
        };
    }

    public static Mp3Audio LoadFromFile(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>Opens a streaming decoder over an MP3 stream (the BGM buffer-queue source).</summary>
    public static IPcmStreamDecoder OpenStream(Stream stream) => new Mp3StreamDecoder(stream);

    public static IPcmStreamDecoder OpenStreamFromFile(string path) => new Mp3StreamDecoder(File.OpenRead(path));
}

/// <summary>
/// NLayer-backed streaming MP3 decoder (the managed mpg123 replacement). Decodes
/// incrementally — <see cref="ReadPcm"/> pulls one buffer's worth per call — and
/// <see cref="SeekToStart"/> rewinds to the first sample so <see cref="BgmStream"/>
/// can loop seamlessly at EOS.
/// </summary>
public sealed class Mp3StreamDecoder : IPcmStreamDecoder
{
    private readonly MpegFile _file;

    public Mp3StreamDecoder(Stream stream)
    {
        _file = new MpegFile(stream);
        Channels = Math.Max(1, _file.Channels);
        SampleRate = _file.SampleRate;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int ReadPcm(byte[] destination, int offset, int count)
        => _file.ReadSamplesInt16(destination, offset, count);

    public void SeekToStart()
    {
        // Position is measured in samples; 0 is the first sample. Guard non-seekable
        // streams so a decode-only fallback never throws.
        try
        {
            if (_file.CanSeek)
                _file.Position = 0;
        }
        catch (Exception)
        {
            // Non-seekable stream: looping degrades to stopping at EOS.
        }
    }

    public void Dispose() => _file.Dispose();
}
