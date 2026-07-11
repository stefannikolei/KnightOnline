using OpenKO.Core.Compression;
using Xunit;

namespace OpenKO.Core.Tests;

public class LzfTests
{
    [Fact]
    public void Compress_MatchesCppGoldenVectors()
    {
        foreach (var testCase in GoldenVectors.Load("lzf.json").EnumerateArray())
        {
            string name = testCase.GetProperty("name").GetString()!;
            byte[] input = GoldenVectors.Hex(testCase.GetProperty("input").GetString()!);
            int outLen = testCase.GetProperty("outLen").GetInt32();
            byte[] expected = GoldenVectors.Hex(testCase.GetProperty("compressed").GetString()!);

            var output = new byte[outLen];
            int compressedLen = Lzf.Compress(input, output);

            Assert.True(expected.Length == compressedLen,
                $"case '{name}': expected length {expected.Length}, got {compressedLen}");
            Assert.Equal(expected, output[..compressedLen]);
        }
    }

    [Fact]
    public void Decompress_RoundTripsGoldenVectors()
    {
        foreach (var testCase in GoldenVectors.Load("lzf.json").EnumerateArray())
        {
            byte[] input = GoldenVectors.Hex(testCase.GetProperty("input").GetString()!);
            byte[] compressed = GoldenVectors.Hex(testCase.GetProperty("compressed").GetString()!);

            if (compressed.Length == 0)
                continue; // compression didn't fit its buffer

            var output = new byte[input.Length];
            int len = Lzf.Decompress(compressed, output);

            Assert.Equal(input.Length, len);
            Assert.Equal(input, output);
        }
    }

    [Fact]
    public void Decompress_ReturnsZeroOnTruncatedInput()
    {
        // Control byte announces a 5-byte literal run but only 2 bytes follow
        // (CHECK_INPUT path -> EINVAL -> 0).
        byte[] truncated = { 0x04, (byte)'a', (byte)'b' };

        var output = new byte[64];
        Assert.Equal(0, Lzf.Decompress(truncated, output));
    }

    [Fact]
    public void Decompress_ReturnsZeroOnTooSmallOutput()
    {
        byte[] input = Enumerable.Repeat((byte)'x', 500).ToArray();
        var compressed = new byte[1024];
        int compressedLen = Lzf.Compress(input, compressed);

        var output = new byte[10];
        Assert.Equal(0, Lzf.Decompress(compressed.AsSpan(0, compressedLen), output));
    }
}
