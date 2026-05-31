namespace OpenKO.Common;

/// <summary>
/// Standard CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB8820) matching the original
/// shared/crc32.cpp used by the packet encryption layer.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        const uint poly = 0xEDB88320u;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? poly ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    /// <summary>
    /// Computes the CRC-32 over <paramref name="data"/>.
    /// The original is called as <c>crc32(buf, len, -1)</c>, i.e. an initial value of 0xFFFFFFFF
    /// with the final result NOT inverted (it returns the running register directly).
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> data, uint initial = 0xFFFFFFFFu)
    {
        uint crc = initial;
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
