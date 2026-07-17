namespace OpenKO.Client.Assets.Player;

/// <summary>
/// Typed accessors over an <see cref="N3TableFile"/> row's cells. The reader
/// stores each cell as its native CLR type (byte/short/ushort/int/uint/float/
/// double/string); these coerce by column index with an out-of-range guard so a
/// short row falls back to the default instead of throwing.
/// </summary>
internal static class TblCell
{
    public static string Str(object[] cells, int i) =>
        i >= 0 && i < cells.Length && cells[i] is string s ? s : string.Empty;

    public static uint U32(object[] cells, int i) =>
        i >= 0 && i < cells.Length ? Convert.ToUInt32(cells[i]) : 0u;

    public static int I32(object[] cells, int i) =>
        i >= 0 && i < cells.Length ? Convert.ToInt32(cells[i]) : 0;

    public static short I16(object[] cells, int i) =>
        i >= 0 && i < cells.Length ? Convert.ToInt16(cells[i]) : (short)0;

    public static byte U8(object[] cells, int i) =>
        i >= 0 && i < cells.Length ? Convert.ToByte(cells[i]) : (byte)0;

    /// <summary>Signed 8-bit cell (a C++ <c>char</c> column, e.g. cNeedLevel).</summary>
    public static sbyte S8(object[] cells, int i) => unchecked((sbyte)U8(cells, i));
}
