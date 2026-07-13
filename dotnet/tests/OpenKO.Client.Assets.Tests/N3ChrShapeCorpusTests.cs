using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Full-corpus scans over the character/shape formats: every file must parse
/// and be consumed exactly.
/// </summary>
public class N3ChrShapeCorpusTests
{
    private static void Scan<T>(string pattern, int minimumCount) where T : N3BaseFile, new()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out (e.g. CI)

        var failures = new List<string>();
        int count = 0;

        foreach (string path in AssetCorpus.EnumerateFiles(pattern))
        {
            count++;
            try
            {
                using var stream = File.OpenRead(path);
                var asset = new T();
                asset.Load(new BinaryReader(stream));
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
        Assert.True(count >= minimumCount,
            $"Corpus scan found only {count} {pattern} files under {AssetCorpus.Root} — checkout incomplete?");
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryCPartInCorpus_Parses() => Scan<N3CPart>("*.n3cpart", 1000);

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryCPartSkinsInCorpus_Parses() => Scan<N3CPartSkins>("*.n3cskins", 900);

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryCPlugInCorpus_Parses() => Scan<N3CPlug>("*.n3cplug", 150);

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryCloakInCorpus_Parses() => Scan<N3CPlugCloak>("*.n3cloak", 0);

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryShapeInCorpus_Parses() => Scan<N3Shape>("*.n3shape", 150);

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryChrInCorpus_Parses()
    {
        if (AssetCorpus.Root is null)
            return;

        var failures = new List<string>();
        int count = 0, withSkinCollisionTail = 0;

        foreach (string path in AssetCorpus.EnumerateFiles("*.n3chr"))
        {
            count++;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                var chr = new N3Chr();
                chr.Load(reader);

                long remaining = stream.Length - stream.Position;
                if (remaining == 0)
                    continue;

                // Most 1298 chr files end with one extra [len][name] block —
                // the collision-skin reference the C++ never reads. Accept
                // exactly that (and nothing else) as unread tail.
                if (remaining >= 4)
                {
                    int tailLen = reader.ReadInt32();
                    if (tailLen >= 0 && 4 + tailLen == remaining)
                    {
                        withSkinCollisionTail++;
                        continue;
                    }

                    // One ChrSelect-era file pads the tail with 0xFF ints;
                    // the C++ reads the FXPlug name length as -1 (no name)
                    // and leaves the rest unread, exactly like our loader.
                    if (tailLen < 0 && remaining <= 16)
                        continue;
                }

                failures.Add($"{path}: {remaining} unexplained trailing bytes");
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .n3chr files failed:\n{string.Join('\n', failures.Take(25))}");
        Assert.True(count >= 90, $"Corpus scan found only {count} .n3chr files — checkout incomplete?");
        Assert.True(withSkinCollisionTail > 0, "expected some chr files with the unread skin-collision tail");
    }
}
