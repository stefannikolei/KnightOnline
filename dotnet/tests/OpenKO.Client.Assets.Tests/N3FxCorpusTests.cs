using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Full-corpus scan of the effect files. Skipped when the Client/Data submodule
/// is not checked out (e.g. CI).
/// </summary>
public class N3FxCorpusTests
{
    private static void ScanCorpus<T>(string pattern) where T : N3BaseFile, new()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out

        var failures = new List<string>();
        int count = 0;

        foreach (string path in AssetCorpus.EnumerateFiles(pattern))
        {
            count++;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                var file = new T();
                file.Load(reader);

                if (stream.Position != stream.Length)
                    failures.Add($"{path}: {stream.Length - stream.Position} trailing bytes");
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count > 25)
                break;
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} {pattern} files failed:\n{string.Join('\n', failures)}");
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryBundleInCorpus_ParsesAndConsumesWholeFile()
        => ScanCorpus<N3FXBundle>("*.fxb");

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryGroupInCorpus_ParsesAndConsumesWholeFile()
        => ScanCorpus<N3FXGroup>("*.fxg");

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryFxShapeInCorpus_ParsesAndConsumesWholeFile()
        => ScanCorpus<N3FXShape>("*.n3fxshape");

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryFxPMeshInCorpus_ParsesAndConsumesWholeFile()
        => ScanCorpus<N3FXPMesh>("*.n3fxpmesh");

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryFxPlugInCorpus_ParsesAndConsumesWholeFile()
        => ScanCorpus<N3FXPlug>("*.n3fxplug");
}
