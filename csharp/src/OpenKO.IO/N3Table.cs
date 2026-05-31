using System.Text;

namespace OpenKO.IO;

/// <summary>A single row of an <see cref="N3Table"/>, addressed by column index.</summary>
public sealed class N3Row
{
    private readonly object[] _values;

    public N3Row(object[] values)
    {
        _values = values;
    }

    public int ColumnCount => _values.Length;

    public object this[int column] => _values[column];

    /// <summary>The row key — the first column, which is always a DWORD (port requirement).</summary>
    public uint Id => Convert.ToUInt32(_values[0]);

    public byte GetByte(int column) => Convert.ToByte(_values[column]);
    public sbyte GetSByte(int column) => Convert.ToSByte(_values[column]);
    public short GetInt16(int column) => Convert.ToInt16(_values[column]);
    public ushort GetUInt16(int column) => Convert.ToUInt16(_values[column]);
    public int GetInt32(int column) => Convert.ToInt32(_values[column]);
    public uint GetUInt32(int column) => Convert.ToUInt32(_values[column]);
    public float GetSingle(int column) => Convert.ToSingle(_values[column]);
    public double GetDouble(int column) => Convert.ToDouble(_values[column]);
    public string GetString(int column) => (string)_values[column];
}

/// <summary>
/// Port of the C++ <c>CN3TableBase&lt;T&gt;</c> / <c>CN3TableBaseImpl</c> (Client/N3Base) — a reader
/// for KO's column-typed ".tbl" data tables.
///
/// Where the original deserialised each row into a fixed C++ struct (using a manually computed,
/// 4-byte-aligned offset table), this reads each row into a typed <see cref="N3Row"/>, keyed by the
/// first column (which must be a <see cref="TblDataType.Dword"/> id). The on-disk byte format is read
/// identically, so it stays compatible with the game's table files.
///
/// On-disk layout (after optional whole-file decryption):
/// <code>
///   int32 columnCount
///   int32 columnTypes[columnCount]   // TblDataType values
///   int32 rowCount
///   row[rowCount]                    // each column read per its type; strings are int32 len + bytes
/// </code>
/// </summary>
public sealed class N3Table
{
    public TblDataType[] Columns { get; private set; } = Array.Empty<TblDataType>();

    private readonly Dictionary<uint, N3Row> _rows = new();

    public IReadOnlyDictionary<uint, N3Row> Rows => _rows;
    public int Count => _rows.Count;

    public N3Row? Find(uint id) => _rows.TryGetValue(id, out N3Row? row) ? row : null;

    public void Release()
    {
        Columns = Array.Empty<TblDataType>();
        _rows.Clear();
    }

    /// <summary>Loads a table from an already-decrypted stream (port of <c>CN3TableBase::Load</c>).</summary>
    public bool Load(IFile file)
    {
        var reader = file as FileReader
            ?? throw new ArgumentException("N3Table.Load requires a FileReader", nameof(file));

        Release();

        int columnCount = reader.Read<int>();
        if (columnCount <= 0)
            return false;

        var columns = new TblDataType[columnCount];
        for (int i = 0; i < columnCount; i++)
            columns[i] = (TblDataType)reader.Read<int>();

        // The first column is always the unique id and must be a DWORD.
        if (columns[0] != TblDataType.Dword)
            return false;

        Columns = columns;

        int rowCount = reader.Read<int>();
        for (int r = 0; r < rowCount; r++)
        {
            var values = new object[columnCount];
            for (int c = 0; c < columnCount; c++)
                values[c] = ReadValue(reader, columns[c]);

            var row = new N3Row(values);
            _rows[row.Id] = row;
        }

        return true;
    }

    /// <summary>Opens, decrypts and loads a ".tbl" file (port of <c>CN3TableBaseImpl::LoadFromFile</c>).</summary>
    public bool LoadFromFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        byte[] encrypted = File.ReadAllBytes(path);
        if (encrypted.Length == 0)
            return false;

        byte[] decrypted = Decrypt(encrypted);

        var reader = new FileReader();
        reader.OpenFromMemory(decrypted, path);
        bool result = Load(reader);
        reader.Close();
        return result;
    }

    private static object ReadValue(FileReader file, TblDataType type) => type switch
    {
        TblDataType.Char => (sbyte)file.Read<byte>(),
        TblDataType.Byte => file.Read<byte>(),
        TblDataType.Short => file.Read<short>(),
        TblDataType.Word => file.Read<ushort>(),
        TblDataType.Int => file.Read<int>(),
        TblDataType.Dword => file.Read<uint>(),
        TblDataType.Float => file.Read<float>(),
        TblDataType.Double => file.Read<double>(),
        TblDataType.String => ReadString(file),
        _ => throw new InvalidDataException($"Unsupported TBL data type: {type}"),
    };

    private static string ReadString(FileReader file)
    {
        int len = file.Read<int>();
        if (len <= 0)
            return string.Empty;

        Span<byte> buf = len <= 256 ? stackalloc byte[len] : new byte[len];
        file.Read(buf);
        return Encoding.Latin1.GetString(buf);
    }

    /// <summary>
    /// Reverses the simple stream cipher KO applies to .tbl files (port of the key_r/key_c1/key_c2
    /// loop in <c>CN3TableBaseImpl::LoadFromFile</c>). All key arithmetic is 16-bit and wraps.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> input)
    {
        ushort keyR = 0x0816;
        const ushort keyC1 = 0x6081;
        const ushort keyC2 = 0x1608;

        var output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            byte cipher = input[i];
            output[i] = (byte)(cipher ^ (keyR >> 8));
            keyR = (ushort)((cipher + keyR) * keyC1 + keyC2);
        }

        return output;
    }

    /// <summary>Applies the .tbl stream cipher (inverse of <see cref="Decrypt"/>), useful for tests/tools.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> input)
    {
        ushort keyR = 0x0816;
        const ushort keyC1 = 0x6081;
        const ushort keyC2 = 0x1608;

        var output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            byte cipher = (byte)(input[i] ^ (keyR >> 8));
            output[i] = cipher;
            keyR = (ushort)((cipher + keyR) * keyC1 + keyC2);
        }

        return output;
    }
}
