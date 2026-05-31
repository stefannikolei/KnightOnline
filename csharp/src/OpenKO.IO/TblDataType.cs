namespace OpenKO.IO;

/// <summary>
/// Column data types for N3 ".tbl" tables (port of <c>TBL_DATA_TYPE</c> in
/// Client/N3Base/N3TableBaseImpl.h). The numeric values match the originals and are written to disk.
/// </summary>
public enum TblDataType
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

public static class TblDataTypeExtensions
{
    /// <summary>On-disk fixed size of a value of this type (strings are variable, so 0 here).</summary>
    public static int SizeOf(this TblDataType type) => type switch
    {
        TblDataType.Char => 1,
        TblDataType.Byte => 1,
        TblDataType.Short => 2,
        TblDataType.Word => 2,
        TblDataType.Int => 4,
        TblDataType.Dword => 4,
        TblDataType.Float => 4,
        TblDataType.Double => 8,
        TblDataType.String => 0, // variable length (int32 length prefix + bytes)
        _ => 0,
    };
}
