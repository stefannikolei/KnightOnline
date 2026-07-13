using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Full-corpus scans: every .n3joint and .n3anim in Client/Data must parse
/// and consume the file exactly.
/// </summary>
public class N3AnimCorpusTests
{
    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryJointInCorpus_ParsesAndConsumesWholeFile()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out (e.g. CI)

        // Eleven ChrSelect joints are in the pre-KeyOrient legacy layout
        // (channels: pos, rot, scale, then the child count — no orient key).
        // The 1298 C++ misparses them the same way: it reads the child-name
        // bytes as KeyOrient metadata, then a negative child count, and
        // silently yields a root-only skeleton (its File reads past EOF
        // don't throw). Failing/stopping on them IS the parity behavior.
        var knownLegacy = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "upc_el_ba_bone.n3joint",
            "upc_el_rf_bone.n3joint",
            "upc_el_rf_bone_hips.n3joint",
            "upc_el_rf_bone_rog.n3joint",
            "upc_el_rf_rog0120.n3joint",
            "upc_el_rm_bone.n3joint",
            "upc_el_rm_bone_war.n3joint",
            "upc_ka_ba_bone.n3joint",
            "upc_ka_pri.n3joint",
            "upc_ka_priest.n3joint",
            "upc_ka_rog_bone.n3joint",
        };

        var failures = new List<string>();
        int count = 0;

        foreach (string path in AssetCorpus.EnumerateFiles("*.n3joint"))
        {
            bool legacy = knownLegacy.Contains(Path.GetFileName(path));
            count++;
            try
            {
                using var stream = File.OpenRead(path);
                var joint = new N3Joint();
                joint.Load(new BinaryReader(stream));

                if (legacy)
                {
                    // Like the C++, the parse "succeeds" but must stop early.
                    if (stream.Position == stream.Length)
                        failures.Add($"{path}: legacy file unexpectedly parsed cleanly");
                }
                else if (stream.Position != stream.Length)
                {
                    failures.Add($"{path}: {stream.Length - stream.Position} trailing bytes");
                }
                else if (joint.NodeCount() < 1)
                {
                    failures.Add($"{path}: empty joint tree");
                }
            }
            catch (EndOfStreamException) when (legacy)
            {
                // Garbage key metadata ran past EOF — the C++ reads garbage
                // instead; either way the file yields no usable skeleton.
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(count > 100, $"Corpus scan found only {count} .n3joint files — checkout incomplete?");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .n3joint files failed:\n{string.Join('\n', failures.Take(25))}");
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryAnimControlInCorpus_ParsesAndConsumesWholeFile()
    {
        if (AssetCorpus.Root is null)
            return;

        var failures = new List<string>();
        int count = 0, clips = 0;

        foreach (string path in AssetCorpus.EnumerateFiles("*.n3anim"))
        {
            count++;
            try
            {
                using var stream = File.OpenRead(path);
                var anim = new N3AnimControl();
                anim.Load(new BinaryReader(stream));

                if (stream.Position != stream.Length)
                    failures.Add($"{path}: {stream.Length - stream.Position} trailing bytes");
                clips += anim.Clips.Count;
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(count > 100, $"Corpus scan found only {count} .n3anim files — checkout incomplete?");
        Assert.True(clips > 0, "No clips parsed from the corpus");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .n3anim files failed:\n{string.Join('\n', failures.Take(25))}");
    }
}
