using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-7.10 pins: the .tbl reader (cipher + column/row parse).</summary>
public class N3TableTests
{
    private static byte[] BuildDecryptedTable()
    {
        // columns: [DWORD id, STRING name, BYTE race, FLOAT scale]; 2 rows.
        // BinaryWriter is little-endian, matching the C++ file layout.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(4); // column count
        w.Write((int)TblType.Dword);
        w.Write((int)TblType.String);
        w.Write((int)TblType.Byte);
        w.Write((int)TblType.Float);
        w.Write(2); // row count

        void WriteRow(uint id, string name, byte race, float scale)
        {
            w.Write((int)id);
            w.Write(name.Length);
            w.Write(System.Text.Encoding.ASCII.GetBytes(name));
            w.Write(race);
            w.Write(scale);
        }

        WriteRow(1001, "Karus", 1, 1.5f);
        WriteRow(1002, "ElMorad", 2, 1.0f);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Cipher_RoundTrips()
    {
        byte[] plain = [0, 1, 2, 250, 99, 200, 7, 7, 7, 128, 255];
        byte[] cipher = N3TableFile.Encrypt(plain);
        Assert.NotEqual(plain, cipher);
        Assert.Equal(plain, N3TableFile.Decrypt(cipher));
    }

    [Fact]
    public void Parse_TypedCellsAndFind()
    {
        N3TableFile table = N3TableFile.Load(BuildDecryptedTable(), encrypted: false);

        Assert.Equal(4, table.Columns.Count);
        Assert.Equal(2, table.Rows.Count);

        object[]? row = table.Find(1001);
        Assert.NotNull(row);
        Assert.Equal(1001u, row![0]);
        Assert.Equal("Karus", row[1]);
        Assert.Equal((byte)1, row[2]);
        Assert.Equal(1.5f, (float)row[3], 4);

        Assert.Equal("ElMorad", table.Find(1002)![1]);
        Assert.Null(table.Find(9999));
    }

    [Fact]
    public void EncryptedTable_LoadsThroughDecrypt()
    {
        byte[] encrypted = N3TableFile.Encrypt(BuildDecryptedTable());
        N3TableFile table = N3TableFile.Load(encrypted, encrypted: true);
        Assert.Equal("Karus", table.Find(1001)![1]);
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealTables_DecryptAndParse()
    {
        if (AssetCorpus.Root == null)
            return;

        // The player-looks + item tables the character assembly needs.
        foreach (string name in (string[])["UPC_DefaultLooks.tbl", "NPC_Looks.tbl", "Item_Org_us.tbl"])
        {
            string path = Path.Combine(AssetCorpus.Root, "Data", name);
            if (!File.Exists(path))
                continue;

            N3TableFile table = N3TableFile.LoadFromFile(path);
            Assert.True(table.Columns.Count > 0, $"{name}: no columns");
            Assert.Equal(TblType.Dword, table.Columns[0]);
            Assert.True(table.Rows.Count > 0, $"{name}: no rows");
            // Every row's id indexes back to itself.
            Assert.All(table.Rows, r => Assert.Same(r, table.Find((uint)r[0])));
        }
    }
}
