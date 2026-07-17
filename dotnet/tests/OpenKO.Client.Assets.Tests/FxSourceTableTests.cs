using OpenKO.Client.Assets.Effects;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Pins for the fx.tbl reader (__TABLE_FX resolution).</summary>
public class FxSourceTableTests
{
    // __TABLE_FX (GameDef.h:1187): dwID, szName, szFN, dwSoundID, byAOE. The real
    // file stores dwSoundID as an Int column, matched here.
    private static readonly TblType[] Columns =
    [
        TblType.Dword, TblType.String, TblType.String, TblType.Int, TblType.Byte,
    ];

    [Fact]
    public void TryGet_DecodesFieldOrder()
    {
        object[] fire =
        [
            101u, "", "fx/Fire_target0_1.fxb", 5150u, (byte)0,
        ];
        object[] aoe =
        [
            603u, "ClassChange", "fx/classchange.fxb", 42u, (byte)1,
        ];

        var table = new FxSourceTable(TblFixture.Build(Columns, [fire, aoe]));

        Assert.True(table.TryGet(101, out FxSourceRow f));
        Assert.Equal(101u, f.Id);
        Assert.Equal(string.Empty, f.Name);
        Assert.Equal("fx/Fire_target0_1.fxb", f.FileName);
        Assert.Equal(5150u, f.SoundId);
        Assert.Equal(0, f.Aoe);

        Assert.True(table.TryGet(603, out FxSourceRow c));
        Assert.Equal("ClassChange", c.Name);
        Assert.Equal("fx/classchange.fxb", c.FileName);
        Assert.Equal(42u, c.SoundId);
        Assert.Equal(1, c.Aoe);

        Assert.False(table.TryGet(999, out FxSourceRow _));
        Assert.Null(table.Find(999));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealFxTable_ResolvesBundleFilenames()
    {
        if (AssetCorpus.Root == null)
            return;

        string path = Path.Combine(AssetCorpus.Root, "Data", "fx.tbl");
        if (!File.Exists(path))
            return;

        var table = FxSourceTable.LoadFromFile(path);

        // Every effect that has a filename lives under the fx\ directory. The raw
        // value is exposed as-is: it may carry trailing whitespace and a few entries
        // omit the .fxb extension (the client trims, lower-cases and appends .fxb
        // before lookup) — so we validate the szFN column mapping via the path
        // prefix rather than a strict extension.
        int fxbCount = 0;
        for (uint id = 1; id < 100000; id++)
        {
            if (!table.TryGet(id, out FxSourceRow row))
                continue;
            if (string.IsNullOrWhiteSpace(row.FileName))
                continue;

            fxbCount++;
            Assert.StartsWith("fx", row.FileName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(fxbCount >= 10, $"expected many .fxb effects, found {fxbCount}");
    }
}
