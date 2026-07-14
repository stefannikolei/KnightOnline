using OpenKO.Client.Assets.Zones;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Pins for the Zones.tbl reader (__TABLE_ZONE resolution).</summary>
public class ZoneTableTests
{
    [Fact]
    public void Find_ResolvesZoneMapFiles()
    {
        // dwID, terrain, name, colormap, lightmap, opd, opdext, minimap, sky, indicateEnemy
        TblType[] cols =
        [
            TblType.Dword, TblType.String, TblType.String, TblType.String, TblType.String,
            TblType.String, TblType.String, TblType.String, TblType.String, TblType.Int,
        ];
        object[] moradon =
        [
            21u, "Moradon.gtd", "Moradon", "Moradon.tct", "Moradon.tlt",
            "Moradon.opd", "Moradon.opdext", "Moradon.dxt", "moradon_sky", 0,
        ];
        object[] kingdom =
        [
            1u, "Karus.gtd", "Luferson", "Karus.tct", "Karus.tlt",
            "", "", "Karus.dxt", "karus_sky", 1,
        ];

        var table = new ZoneTable(TblFixture.Build(cols, [moradon, kingdom]));

        ZoneRow? z = table.Find(21);
        Assert.NotNull(z);
        Assert.Equal("Moradon.gtd", z!.TerrainFileName);
        Assert.Equal("Moradon", z.Name);
        Assert.Equal("Moradon.tct", z.ColorMapFileName);
        Assert.Equal("moradon_sky", z.SkySetting);
        Assert.False(z.IndicateEnemyPlayer);

        ZoneRow? luf = table.Find(1);
        Assert.NotNull(luf);
        Assert.Equal("Karus.gtd", luf!.TerrainFileName);
        Assert.True(luf.IndicateEnemyPlayer);

        Assert.Null(table.Find(999));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealZonesTable_ResolvesTerrainFilenames()
    {
        if (AssetCorpus.Root == null)
            return;

        string path = Path.Combine(AssetCorpus.Root, "Data", "Zones.tbl");
        if (!File.Exists(path))
            return;

        var table = ZoneTable.LoadFromFile(path);

        // Collect the real zone rows whose terrain field is a .gtd (id 0 is a
        // special exe-check row with no map) and confirm the sibling map fields
        // line up — validates the column mapping against the real file.
        int gtdZones = 0;
        for (uint id = 1; id < 4096; id++)
        {
            ZoneRow? z = table.Find(id);
            if (z == null || !z.TerrainFileName.EndsWith(".gtd", StringComparison.OrdinalIgnoreCase))
                continue;

            gtdZones++;
            Assert.EndsWith(".tct", z.ColorMapFileName, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".tlt", z.LightMapFileName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("N3Sky", z.SkySetting, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(gtdZones >= 2, $"expected several .gtd zones, found {gtdZones}");

        // The Karus start zone (id 10) resolves to its terrain.
        ZoneRow? karus = table.Find(10);
        Assert.NotNull(karus);
        Assert.EndsWith("karus2004.gtd", karus!.TerrainFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("karus2004", karus.Name, ignoreCase: true);
    }
}
