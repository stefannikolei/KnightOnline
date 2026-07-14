namespace OpenKO.Client.Assets.Tests;

/// <summary>Builds a decrypted <see cref="N3TableFile"/> from typed column/row fixtures.</summary>
internal static class TblFixture
{
    public static N3TableFile Build(IReadOnlyList<TblType> columns, IReadOnlyList<object[]> rows)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(columns.Count);
        foreach (TblType t in columns)
            w.Write((int)t);
        w.Write(rows.Count);
        foreach (object[] row in rows)
            for (int j = 0; j < columns.Count; j++)
                WriteCell(w, columns[j], row[j]);
        w.Flush();
        return N3TableFile.Load(ms.ToArray(), encrypted: false);
    }

    private static void WriteCell(BinaryWriter w, TblType type, object value)
    {
        switch (type)
        {
            case TblType.Char:
            case TblType.Byte: w.Write(Convert.ToByte(value)); break;
            case TblType.Short: w.Write(Convert.ToInt16(value)); break;
            case TblType.Word: w.Write(Convert.ToUInt16(value)); break;
            case TblType.Int: w.Write(Convert.ToInt32(value)); break;
            case TblType.Dword: w.Write(Convert.ToUInt32(value)); break;
            case TblType.Float: w.Write(Convert.ToSingle(value)); break;
            case TblType.Double: w.Write(Convert.ToDouble(value)); break;
            case TblType.String:
                var s = (string)value;
                w.Write(s.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                break;
            default: throw new InvalidOperationException();
        }
    }
}
