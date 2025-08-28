namespace OpenKO.Web.Tests;

[TestClass]
public sealed class CryptoTests
{
    [TestMethod]
    public void DecryptTest()
    {
        string plainText = "Hello, World!";
        string sharedSecret = "asdfasdf";

        string expected = LegacyCrypto.EncryptStringAes(plainText, sharedSecret);
        string actual = Crypto.DecryptStringAes(expected, sharedSecret);

        Assert.AreEqual(plainText, actual);
    }

    [TestMethod]
    public void EncryptTest()
    {
        string plainText = "Hello, World!";
        string sharedSecret = "asdfasdf";

        string expected = Crypto.EncryptStringAes(plainText, sharedSecret);
        string actual = LegacyCrypto.DecryptStringAes(expected, sharedSecret);

        Assert.AreEqual(plainText, actual);
    }
}
