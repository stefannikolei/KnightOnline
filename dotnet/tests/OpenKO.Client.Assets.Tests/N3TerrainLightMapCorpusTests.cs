using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Full-corpus scan over the runtime .tlt lightmap files.</summary>
public class N3TerrainLightMapCorpusTests
{
    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryLightMapInCorpus_Parses()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out (e.g. CI)

        var failures = new List<string>();
        int count = 0, skipped = 0, patchesWithTiles = 0, totalTiles = 0;

        foreach (string tltPath in AssetCorpus.EnumerateFiles("*.tlt"))
        {
            // The .tlt does not store its patch grid size — take it from the
            // sibling .gtd, exactly as the client sizes its Addr table.
            string gtdPath = Path.ChangeExtension(tltPath, ".gtd");
            if (!File.Exists(gtdPath))
            {
                skipped++;
                continue;
            }

            count++;
            try
            {
                int patchMapSize;
                using (var gtdStream = File.OpenRead(gtdPath))
                using (var gtdReader = new BinaryReader(gtdStream))
                {
                    var terrain = new N3Terrain { FileFormatVersion = N3FormatVersion.Default };
                    terrain.Load(gtdReader);
                    patchMapSize = terrain.PatchMapSize;
                }

                using var stream = File.OpenRead(tltPath);
                using var reader = new BinaryReader(stream);
                var file = new N3TerrainLightMapFile { FileFormatVersion = N3FormatVersion.Default };
                file.Load(reader, patchMapSize);

                foreach (N3TerrainLightMapPatch? patch in file.Patches)
                {
                    if (patch is null || patch.Tiles.Count == 0)
                        continue;
                    patchesWithTiles++;
                    totalTiles += patch.Tiles.Count;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(tltPath)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .tlt files failed:\n{string.Join('\n', failures.Take(25))}");
        // Real zones carry baked lightmaps; a total of zero would mean the
        // offset-table walk is finding nothing.
        Assert.True(count == 0 || totalTiles > 0,
            $"Parsed {count} .tlt files but found no lightmap tiles (patchesWithTiles={patchesWithTiles})");
    }
}
