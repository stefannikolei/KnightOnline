using System.Buffers.Binary;
using System.Text;
using OpenKO.IO;
using Xunit;

namespace OpenKO.Tests;

public class N3TableTests
{
    private static byte[] BuildRawTable()
    {
        // Columns: DWORD id, INT, STRING, FLOAT
        var ms = new MemoryStream();
        Span<byte> i32 = stackalloc byte[4];

        void WriteInt(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(i32, v);
            ms.Write(i32);
        }

        void WriteUInt(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(i32, v);
            ms.Write(i32);
        }

        void WriteFloat(float v)
        {
            BinaryPrimitives.WriteSingleLittleEndian(i32, v);
            ms.Write(i32);
        }

        void WriteString(string s)
        {
            byte[] b = Encoding.Latin1.GetBytes(s);
            WriteInt(b.Length);
            ms.Write(b);
        }

        WriteInt(4); // column count
        WriteInt((int)TblDataType.Dword);
        WriteInt((int)TblDataType.Int);
        WriteInt((int)TblDataType.String);
        WriteInt((int)TblDataType.Float);

        WriteInt(2); // row count

        WriteUInt(10);
        WriteInt(-5);
        WriteString("Sword");
        WriteFloat(1.5f);

        WriteUInt(20);
        WriteInt(99);
        WriteString("Shield");
        WriteFloat(-2.25f);

        return ms.ToArray();
    }

    [Fact]
    public void ParsesColumnsAndRows()
    {
        var reader = new FileReader();
        reader.OpenFromMemory(BuildRawTable());

        var table = new N3Table();
        Assert.True(table.Load(reader));

        Assert.Equal(4, table.Columns.Length);
        Assert.Equal(TblDataType.Dword, table.Columns[0]);
        Assert.Equal(2, table.Count);

        N3Row? row = table.Find(10);
        Assert.NotNull(row);
        Assert.Equal(10u, row!.Id);
        Assert.Equal(-5, row.GetInt32(1));
        Assert.Equal("Sword", row.GetString(2));
        Assert.Equal(1.5f, row.GetSingle(3));

        N3Row? row2 = table.Find(20);
        Assert.Equal("Shield", row2!.GetString(2));
        Assert.Equal(-2.25f, row2.GetSingle(3));
    }

    [Fact]
    public void RejectsTableWhoseFirstColumnIsNotDword()
    {
        var ms = new MemoryStream();
        Span<byte> i32 = stackalloc byte[4];
        void WriteInt(int v) { BinaryPrimitives.WriteInt32LittleEndian(i32, v); ms.Write(i32); }

        WriteInt(1);                       // column count
        WriteInt((int)TblDataType.Int);    // first column is INT, not DWORD
        WriteInt(0);                       // row count

        var reader = new FileReader();
        reader.OpenFromMemory(ms.ToArray());
        Assert.False(new N3Table().Load(reader));
    }

    [Fact]
    public void StreamCipherRoundTrips()
    {
        byte[] plain = BuildRawTable();
        byte[] encrypted = N3Table.Encrypt(plain);
        byte[] decrypted = N3Table.Decrypt(encrypted);

        Assert.NotEqual(plain, encrypted);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void LoadFromFileDecryptsAndParses()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openko_tbl_{Guid.NewGuid():N}.tbl");
        try
        {
            File.WriteAllBytes(path, N3Table.Encrypt(BuildRawTable()));

            var table = new N3Table();
            Assert.True(table.LoadFromFile(path));
            Assert.Equal(2, table.Count);
            Assert.Equal("Sword", table.Find(10)!.GetString(2));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
