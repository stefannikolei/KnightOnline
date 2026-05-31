using OpenKO.Common;
using Xunit;

namespace OpenKO.Tests;

public class CryptionTests
{
    [Fact]
    public void EncryptDecryptIsReversibleWithSharedKey()
    {
        var a = new JvCryption();
        ulong key = a.GenerateKey();
        var b = new JvCryption { PublicKey = key };
        b.Init();

        var plain = new byte[64];
        for (int i = 0; i < plain.Length; i++)
            plain[i] = (byte)(i * 13 + 1);

        var enc = new byte[plain.Length];
        var dec = new byte[plain.Length];
        a.Encrypt(plain.Length, plain, enc);
        b.Decrypt(plain.Length, enc, dec);

        Assert.Equal(plain, dec);
        Assert.NotEqual(plain, enc);
    }

    [Fact]
    public void GeneratedKeyIsNeverZero()
    {
        var crypt = new JvCryption();
        for (int i = 0; i < 100; i++)
            Assert.NotEqual(0UL, crypt.GenerateKey());
    }

    [Fact]
    public void Crc32MatchesKnownAnswer()
    {
        // Standard CRC-32/ISO-HDLC check value for "123456789" is 0xCBF43926
        // once the final XOR with 0xFFFFFFFF is applied to the running register.
        uint register = Crc32.Compute(System.Text.Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal(0xCBF43926u, register ^ 0xFFFFFFFFu);
    }
}
