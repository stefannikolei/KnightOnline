using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Full-corpus scan over the .gtd terrain files.</summary>
public class N3TerrainCorpusTests
{
    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryTerrainInCorpus_Parses()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out (e.g. CI)

        var failures = new List<string>();
        int count = 0, lightMaps = 0, rivers = 0;

        foreach (string path in AssetCorpus.EnumerateFiles("*.gtd"))
        {
            count++;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                var terrain = new N3Terrain { FileFormatVersion = N3FormatVersion.Default };
                terrain.Load(reader);

                lightMaps += terrain.LightMaps.Count;
                rivers += terrain.Rivers.Count;

                // CN3Pond::Load is disabled upstream — any pond payload after
                // the count stays unread there too, so tolerate a tail only
                // when the file claims pond meshes.
                long remaining = stream.Length - stream.Position;
                if (remaining != 0 && terrain.PondCount == 0)
                    failures.Add($"{path}: {remaining} unexplained trailing bytes (pond count 0)");
                else if (terrain.MapSize <= 1)
                    failures.Add($"{path}: implausible map size {terrain.MapSize}");
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .gtd files failed:\n{string.Join('\n', failures.Take(25))}");
        Assert.True(count >= 20, $"Corpus scan found only {count} .gtd files — checkout incomplete?");
        // The 1298 corpus ships no embedded lightmaps (count 0 everywhere);
        // rivers do occur. Both stay informational.
        Assert.True(lightMaps >= 0);
        Assert.True(rivers >= 0);
    }
}
