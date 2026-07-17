using OpenKO.Client.Assets.Audio;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Pins for the sound.tbl reader (__TABLE_SOUND resolution).</summary>
public class SoundTableTests
{
    // __TABLE_SOUND (N3SndDef.h:17): dwID, szFN, iType, iNumInst.
    private static readonly TblType[] Columns =
    [
        TblType.Dword, TblType.String, TblType.Int, TblType.Int,
    ];

    [Fact]
    public void TryGet_DecodesFieldOrder()
    {
        object[] attack =
        [
            1u, "Snd\\kecoon_attack_0.wav", 1, 3,
        ];
        object[] ambient =
        [
            5150u, "Snd\\fire.wav", 2, 8,
        ];

        var table = new SoundTable(TblFixture.Build(Columns, [attack, ambient]));

        Assert.True(table.TryGet(1, out SoundRow a));
        Assert.Equal(1u, a.Id);
        Assert.Equal("Snd\\kecoon_attack_0.wav", a.FileName);
        Assert.Equal(1, a.Type);
        Assert.Equal(3, a.NumInst);

        Assert.True(table.TryGet(5150, out SoundRow s));
        Assert.Equal("Snd\\fire.wav", s.FileName);
        Assert.Equal(2, s.Type);
        Assert.Equal(8, s.NumInst);

        Assert.False(table.TryGet(999, out SoundRow _));
        Assert.Null(table.Find(999));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealSoundTable_ResolvesWaveFilenames()
    {
        if (AssetCorpus.Root == null)
            return;

        string path = Path.Combine(AssetCorpus.Root, "Data", "sound.tbl");
        if (!File.Exists(path))
            return;

        var table = SoundTable.LoadFromFile(path);

        // Every sound with a filename references an audio file (.wav for SFX,
        // .mp3 for streamed BGM) — validates the szFN column mapping against the
        // real file.
        int soundCount = 0;
        for (uint id = 1; id < 100000; id++)
        {
            if (!table.TryGet(id, out SoundRow row))
                continue;
            if (string.IsNullOrWhiteSpace(row.FileName))
                continue;

            soundCount++;
            string fn = row.FileName.Trim();
            Assert.True(
                fn.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                fn.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase),
                $"unexpected sound file extension: {fn}");
            Assert.InRange(row.NumInst, 0, 1000);
        }

        Assert.True(soundCount >= 10, $"expected many sounds, found {soundCount}");
    }
}
