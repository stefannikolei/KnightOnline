using System.Buffers.Binary;
using OpenKO.Core.Text;

namespace OpenKO.Client.Assets;

/// <summary>The column cell types (TBL_DATA_TYPE in N3TableBaseImpl.h).</summary>
public enum TblType
{
    None = 0,
    Char = 1,
    Byte = 2,
    Short = 3,
    Word = 4,
    Int = 5,
    Dword = 6,
    String = 7,
    Float = 8,
    Double = 9,
}

/// <summary>
/// Port of <c>CN3TableBase</c> (Client/N3Base/N3TableBase*.{h,cpp}): the client's
/// <c>.tbl</c> game-data tables. The file is a per-byte XOR stream cipher; once
/// decrypted the layout is <c>[int32 columnCount][int32 type × columnCount]
/// [int32 rowCount]</c> then the rows, each cell read by its column type. The
/// first column is always the DWORD row id. Rows are exposed as typed cells and
/// indexed by that id (the C++ <c>Find</c>).
/// </summary>
public sealed class N3TableFile
{
    private readonly Dictionary<uint, int> _index = [];

    public IReadOnlyList<TblType> Columns { get; private set; } = [];

    public IReadOnlyList<object[]> Rows { get; private set; } = [];

    /// <summary>The row keyed by its id (first DWORD column), or null.</summary>
    public object[]? Find(uint id) => _index.TryGetValue(id, out int r) ? Rows[r] : null;

    public static N3TableFile LoadFromFile(string path) => Load(File.ReadAllBytes(path));

    public static N3TableFile Load(ReadOnlySpan<byte> raw, bool encrypted = true)
    {
        byte[] data = encrypted ? Decrypt(raw) : raw.ToArray();
        var table = new N3TableFile();
        table.Parse(data);
        return table;
    }

    /// <summary>
    /// CN3TableBaseImpl::LoadFromFile decryption — the XOR stream keyed by
    /// r=0x0816, c1=0x6081, c2=0x1608 (uint16 wrapping); the key advances on the
    /// original cipher byte.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> data)
    {
        var result = new byte[data.Length];
        ushort r = 0x0816;
        const ushort c1 = 0x6081;
        const ushort c2 = 0x1608;
        for (int i = 0; i < data.Length; i++)
        {
            byte cipher = data[i];
            result[i] = (byte)(cipher ^ (r >> 8));
            r = (ushort)((cipher + r) * c1 + c2);
        }

        return result;
    }

    /// <summary>The inverse cipher (the table tool's Encrypt) — for round-trips/tools.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plain)
    {
        var result = new byte[plain.Length];
        ushort r = 0x0816;
        const ushort c1 = 0x6081;
        const ushort c2 = 0x1608;
        for (int i = 0; i < plain.Length; i++)
        {
            byte cipher = (byte)(plain[i] ^ (r >> 8));
            result[i] = cipher;
            r = (ushort)((cipher + r) * c1 + c2);
        }

        return result;
    }

    private void Parse(byte[] data)
    {
        int pos = 0;
        int columnCount = ReadInt(data, ref pos);
        if (columnCount <= 0)
            throw new InvalidDataException("Table has no columns");

        var columns = new TblType[columnCount];
        for (int i = 0; i < columnCount; i++)
            columns[i] = (TblType)ReadInt(data, ref pos);
        Columns = columns;

        if (columns[0] != TblType.Dword)
            throw new InvalidDataException("Table first column is not the DWORD id");

        int rowCount = ReadInt(data, ref pos);
        var rows = new object[rowCount][];
        for (int i = 0; i < rowCount; i++)
        {
            var cells = new object[columnCount];
            for (int j = 0; j < columnCount; j++)
                cells[j] = ReadCell(data, ref pos, columns[j]);
            rows[i] = cells;
            _index[(uint)cells[0]] = i; // last wins on a duplicate id, like the C++ map
        }

        Rows = rows;
    }

    private static object ReadCell(byte[] data, ref int pos, TblType type)
    {
        switch (type)
        {
            case TblType.Char:
            case TblType.Byte:
                return data[pos++];
            case TblType.Short:
            {
                short v = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(pos));
                pos += 2;
                return v;
            }

            case TblType.Word:
            {
                ushort v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
                pos += 2;
                return v;
            }

            case TblType.Int:
                return ReadInt(data, ref pos);
            case TblType.Dword:
                return (uint)ReadInt(data, ref pos);
            case TblType.Float:
            {
                float v = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos));
                pos += 4;
                return v;
            }

            case TblType.Double:
            {
                double v = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos));
                pos += 8;
                return v;
            }

            case TblType.String:
            {
                int len = ReadInt(data, ref pos);
                string s = len > 0 ? KoEncoding.Cp949.GetString(data, pos, len) : string.Empty;
                pos += Math.Max(len, 0);
                return s;
            }

            default:
                throw new InvalidDataException($"Unsupported table column type {type}");
        }
    }

    private static int ReadInt(byte[] data, ref int pos)
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        return v;
    }
}
