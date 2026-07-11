using System.Globalization;
using OpenKO.Core.Crypto;
using Xunit;

namespace OpenKO.Core.Tests;

public class JvCryptionTests
{
    [Fact]
    public void Transform_MatchesCppGoldenVectors()
    {
        foreach (var testCase in GoldenVectors.Load("jvcryption.json").EnumerateArray())
        {
            ulong key = ulong.Parse(testCase.GetProperty("key").GetString()!, NumberStyles.HexNumber);
            byte[] input = GoldenVectors.Hex(testCase.GetProperty("input").GetString()!);
            byte[] expected = GoldenVectors.Hex(testCase.GetProperty("output").GetString()!);

            var crypt = new JvCryption();
            crypt.SetPublicKey(key);
            crypt.Init();

            var output = new byte[input.Length];
            crypt.Transform(input, output);

            Assert.Equal(expected, output);
        }
    }

    [Fact]
    public void Transform_IsInvolution()
    {
        var crypt = new JvCryption();
        crypt.SetPublicKey(0xDCE04F8975278163UL);
        crypt.Init();

        byte[] input = Enumerable.Range(0, 300).Select(i => (byte)(i * 3 + 1)).ToArray();
        var encrypted = new byte[input.Length];
        var decrypted = new byte[input.Length];

        crypt.Transform(input, encrypted);
        crypt.Transform(encrypted, decrypted);

        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void DecryptWithCrc32_MatchesCppGoldenVectors()
    {
        foreach (var testCase in GoldenVectors.Load("jvcryption_crc.json").EnumerateArray())
        {
            ulong key = ulong.Parse(testCase.GetProperty("key").GetString()!, NumberStyles.HexNumber);
            byte[] wire = GoldenVectors.Hex(testCase.GetProperty("wire").GetString()!);
            int expectedResult = testCase.GetProperty("result").GetInt32();

            var crypt = new JvCryption();
            crypt.SetPublicKey(key);
            crypt.Init();

            var output = new byte[wire.Length];
            int result = crypt.DecryptWithCrc32(wire, output);

            Assert.Equal(expectedResult, result);

            if (expectedResult >= 0)
            {
                byte[] expectedPayload = GoldenVectors.Hex(testCase.GetProperty("payload").GetString()!);
                Assert.Equal(expectedPayload, output[..expectedResult]);
            }
        }
    }

    [Fact]
    public void GenerateKey_NeverReturnsZero()
    {
        var crypt = new JvCryption();
        ulong key = crypt.GenerateKey();
        Assert.NotEqual(0UL, key);
        Assert.Equal(key, crypt.PublicKey);
    }
}
