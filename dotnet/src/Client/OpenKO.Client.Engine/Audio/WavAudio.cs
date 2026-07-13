using System.Buffers.Binary;

namespace OpenKO.Client.Engine.Audio;

/// <summary>
/// Decoded PCM audio from a RIFF/WAVE file — the KO sound assets are WAV
/// (misc\... .wav). Only linear PCM (format 1) is supported; the mpg123 MP3
/// streaming path (CN3SndObj SNDTYPE_STREAM) is a later addition.
/// </summary>
public sealed class WavAudio
{
    public required int Channels { get; init; }

    public required int SampleRate { get; init; }

    public required int BitsPerSample { get; init; }

    /// <summary>Raw little-endian PCM sample bytes (what OpenAL/MonoGame consume).</summary>
    public required byte[] Pcm { get; init; }

    /// <summary>Parses a WAV byte stream (RIFF "fmt "/"data" chunks).</summary>
    public static WavAudio Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12
            || data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F'
            || data[8] != (byte)'W' || data[9] != (byte)'A' || data[10] != (byte)'V' || data[11] != (byte)'E')
        {
            throw new InvalidDataException("Not a RIFF/WAVE file");
        }

        short channels = 0, bitsPerSample = 0, audioFormat = 0;
        int sampleRate = 0;
        byte[]? pcm = null;

        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            ReadOnlySpan<byte> id = data.Slice(pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));
            int body = pos + 8;
            if (body + size > data.Length)
                size = data.Length - body; // tolerate a truncated trailing chunk

            if (id[0] == 'f' && id[1] == 'm' && id[2] == 't' && id[3] == ' ')
            {
                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(body, 2));
                channels = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(body + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(body + 14, 2));
            }
            else if (id[0] == 'd' && id[1] == 'a' && id[2] == 't' && id[3] == 'a')
            {
                pcm = data.Slice(body, size).ToArray();
            }

            // Chunks are word-aligned (pad byte on odd sizes).
            pos = body + size + (size & 1);
        }

        if (audioFormat != 1)
            throw new NotSupportedException($"Unsupported WAV format {audioFormat} (only PCM)");
        if (pcm == null)
            throw new InvalidDataException("WAV has no data chunk");

        return new WavAudio
        {
            Channels = channels,
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample,
            Pcm = pcm,
        };
    }

    public static WavAudio LoadFromFile(string path) => Load(File.ReadAllBytes(path));
}
