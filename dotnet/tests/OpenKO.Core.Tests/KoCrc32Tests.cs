using OpenKO.Core.Crypto;
using Xunit;

namespace OpenKO.Core.Tests;

public class KoCrc32Tests
{
    [Fact]
    public void Compute_MatchesCppGoldenVectors()
    {
        foreach (var testCase in GoldenVectors.Load("crc32.json").EnumerateArray())
        {
            byte[] input = GoldenVectors.Hex(testCase.GetProperty("input").GetString()!);
            uint start = testCase.GetProperty("start").GetUInt32();
            uint expected = testCase.GetProperty("result").GetUInt32();

            Assert.Equal(expected, KoCrc32.Compute(input, start));
        }
    }

    [Fact]
    public void Compute_IsNotZlibCrc32()
    {
        // The KO variant has no final XOR; guard against someone "fixing" it.
        byte[] digits = "123456789"u8.ToArray();
        uint zlibCrc = 0xCBF43926; // standard CRC-32 of "123456789"
        Assert.NotEqual(zlibCrc, KoCrc32.Compute(digits, 0xFFFFFFFF));
    }
}
