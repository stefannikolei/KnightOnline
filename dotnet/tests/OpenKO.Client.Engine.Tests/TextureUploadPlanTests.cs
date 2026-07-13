using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Rendering;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.2 pins: the pure texture upload plan (no GraphicsDevice).</summary>
public class TextureUploadPlanTests
{
    private static N3Texture MakeDxt(int w, int h, N3PixelFormat format, bool mips)
    {
        var tex = new N3Texture();
        tex.Initialize(w, h, format, mips);
        int levels = mips ? N3Texture.CountMipLevels(w, h) : 1;
        for (int i = 0, lw = w, lh = h; i < levels; i++, lw /= 2, lh /= 2)
        {
            var data = new byte[N3Texture.GetLevelSize(lw, lh, format)];
            Array.Fill(data, (byte)(i + 1));
            tex.MipLevels.Add(data);
        }

        return tex;
    }

    [Fact]
    public void Dxt1WithMips_SynthesizesTheGlTail()
    {
        N3Texture tex = MakeDxt(16, 16, N3PixelFormat.Dxt1, mips: true); // KO levels: 16,8,4
        TextureUploadPlan plan = TextureUploadPlan.FromTexture(tex);

        Assert.Equal(SurfaceFormat.Dxt1, plan.Format);
        Assert.True(plan.MipMap);
        Assert.Equal(5, plan.Levels.Count); // 16,8,4 + synthesized 2,1

        for (int level = 0; level < plan.Levels.Count; level++)
        {
            (int w, int h) = TextureUploadPlan.LevelDims(16, 16, level);
            Assert.Equal(TextureUploadPlan.LevelSize(SurfaceFormat.Dxt1, w, h), plan.Levels[level].Length);
        }

        // Tail blocks clone the smallest real level's first block.
        Assert.Equal((byte)3, plan.Levels[3][0]);
        Assert.Equal((byte)3, plan.Levels[4][0]);
    }

    [Fact]
    public void NonMipDxt5_UploadsSingleLevel()
    {
        N3Texture tex = MakeDxt(32, 16, N3PixelFormat.Dxt5, mips: false);
        TextureUploadPlan plan = TextureUploadPlan.FromTexture(tex);

        Assert.Equal(SurfaceFormat.Dxt5, plan.Format);
        Assert.False(plan.MipMap);
        byte[] level = Assert.Single(plan.Levels);
        Assert.Equal(TextureUploadPlan.LevelSize(SurfaceFormat.Dxt5, 32, 16), level.Length);
    }

    [Fact]
    public void Dxt2And4_UseTheDxt3And5Layouts()
    {
        Assert.Equal(SurfaceFormat.Dxt3, TextureUploadPlan.FromTexture(MakeDxt(8, 8, N3PixelFormat.Dxt2, false)).Format);
        Assert.Equal(SurfaceFormat.Dxt5, TextureUploadPlan.FromTexture(MakeDxt(8, 8, N3PixelFormat.Dxt4, false)).Format);
    }

    [Fact]
    public void Uncompressed_ConvertsToColorRgba()
    {
        var tex = new N3Texture();
        tex.Initialize(2, 2, N3PixelFormat.A8R8G8B8, mipMaps: false);
        // ARGB memory order B,G,R,A per pixel.
        tex.MipLevels.Add([
            0x10, 0x20, 0x30, 0x40,
            0x11, 0x21, 0x31, 0x41,
            0x12, 0x22, 0x32, 0x42,
            0x13, 0x23, 0x33, 0x43,
        ]);

        TextureUploadPlan plan = TextureUploadPlan.FromTexture(tex);
        Assert.Equal(SurfaceFormat.Color, plan.Format);
        byte[] rgba = Assert.Single(plan.Levels);
        Assert.Equal(16, rgba.Length);
        Assert.Equal(0x30, rgba[0]); // R
        Assert.Equal(0x20, rgba[1]); // G
        Assert.Equal(0x10, rgba[2]); // B
        Assert.Equal(0x40, rgba[3]); // A
    }

    [Fact]
    public void FullChainLength_HandlesNonSquare()
    {
        Assert.Equal(9, TextureUploadPlan.FullChainLength(256, 256));
        Assert.Equal(10, TextureUploadPlan.FullChainLength(512, 256));
        Assert.Equal(1, TextureUploadPlan.FullChainLength(1, 1));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryCorpusTexture_ProducesAConsistentPlan()
    {
        string? root = FindCorpus();
        if (root == null)
            return; // Client/Data not checked out (e.g. CI)

        var failures = new List<string>();
        int count = 0;

        foreach (string path in Directory.EnumerateFiles(root, "*.dxt", new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
        }))
        {
            try
            {
                var tex = new N3Texture();
                tex.LoadFromFile(path);
                TextureUploadPlan plan = TextureUploadPlan.FromTexture(tex);
                count++;

                if (plan.MipMap && plan.Levels.Count != TextureUploadPlan.FullChainLength(plan.Width, plan.Height))
                {
                    failures.Add($"{path}: chain {plan.Levels.Count} != full");
                    continue;
                }

                for (int level = 0; level < plan.Levels.Count; level++)
                {
                    (int w, int h) = TextureUploadPlan.LevelDims(plan.Width, plan.Height, level);
                    if (plan.Levels[level].Length != TextureUploadPlan.LevelSize(plan.Format, w, h))
                    {
                        failures.Add($"{path}: level {level} size {plan.Levels[level].Length}");
                        break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // The four known TGA-misnamed corpus files — skipped by the
                // asset-layer scans too.
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count > 25)
                break;
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} upload plans failed:\n{string.Join('\n', failures)}");
        Assert.True(count > 6000, $"only {count} textures planned — corpus incomplete?");
    }

    private static string? FindCorpus()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Client", "Data");
            if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
                return candidate;
        }

        return null;
    }
}
