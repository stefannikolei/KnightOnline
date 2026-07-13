using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Full-corpus scan: every .dxt file in Client/Data must parse, and the reader
/// must land exactly where the C++ reader would (full consumption, except the
/// preserved non-mip under-skip of width*height/4 bytes).
/// </summary>
public class N3TextureCorpusTests
{
    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryDxtInCorpus_ParsesWithExactStreamPositioning()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out (e.g. CI)

        // These four corpus files are 64x64 TGA images misnamed .dxt (header
        // 00 00 02 ...). CN3Texture has no TGA path — the C++ client fails to
        // load them too, so refusing them is the parity-correct behavior.
        var knownBad = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "itemicon_3_2041_01_1.dxt",
            "itemicon_3_2041_01_2.dxt",
            "itemicon_3_2041_01_3.dxt",
            "itemicon_3_3031_01_4.dxt",
        };

        var failures = new List<string>();
        int count = 0, decoded = 0;

        foreach (string path in AssetCorpus.EnumerateFiles("*.dxt"))
        {
            if (knownBad.Contains(Path.GetFileName(path)))
            {
                Assert.Throws<EndOfStreamException>(() =>
                {
                    using var s = File.OpenRead(path);
                    new N3Texture().Load(new BinaryReader(s));
                });
                continue;
            }

            count++;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                var tex = new N3Texture();
                tex.Load(reader);

                long gap = stream.Length - stream.Position;
                long expectedGap = 0;
                if (N3Texture.IsCompressed(tex.Format) && !tex.HasMipMaps)
                {
                    // Non-mip DXT: the C++ reader under-skips the 16bpp
                    // fallback by width*height/4 bytes (quirk kept verbatim).
                    expectedGap = tex.Width * tex.Height / 4;
                }

                if (gap != expectedGap)
                {
                    failures.Add($"{path}: {tex.Width}x{tex.Height} {tex.Format} mips={tex.HasMipMaps} " +
                                 $"gap {gap} (expected {expectedGap})");
                }
                else if (N3Texture.IsCompressed(tex.Format) && decoded < 200)
                {
                    // Decoder smoke over a sample of the corpus.
                    byte[] rgba = DxtDecoder.Decode(tex.Format, tex.MipLevels[0], tex.Width, tex.Height);
                    if (rgba.Length != tex.Width * tex.Height * 4)
                        failures.Add($"{path}: decoder returned {rgba.Length} bytes");
                    decoded++;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count > 25)
                break; // enough to diagnose; don't drown the assert output
        }

        Assert.True(count > 6000, $"Corpus scan found only {count} .dxt files — checkout incomplete?");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .dxt files failed:\n{string.Join('\n', failures)}");
    }
}
