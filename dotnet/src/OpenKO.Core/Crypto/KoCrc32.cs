namespace OpenKO.Core.Crypto;

/// <summary>
/// Port of <c>shared/crc32.cpp</c>.
/// This is NOT the standard zlib CRC32: the start value is caller-seeded and there is
/// no final XOR, so <see cref="System.IO.Hashing.Crc32"/> cannot be used as a substitute.
/// <c>JvDecryptionWithCRC32</c> seeds it with 0xFFFFFFFF and compares the raw register value.
/// </summary>
public static class KoCrc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        // Reflected polynomial 0xEDB88320; produces the identical 256 values as the
        // Gary S. Brown table hardcoded in shared/crc32.cpp (verified by golden vectors).
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data, uint startVal = 0)
    {
        uint crc = startVal;
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
