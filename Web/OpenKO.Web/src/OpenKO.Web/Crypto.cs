using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace OpenKO.Web;

public class Crypto
{
    private static readonly byte[] s_salt = "o6806642kbM7c5"u8.ToArray();

    /// <summary>
    /// Encrypt the given string using AES.  The string can be decrypted using
    /// DecryptStringAES().  The sharedSecret parameters must match.
    /// </summary>
    /// <param name="plainText">The text to encrypt.</param>
    /// <param name="sharedSecret">A password used to generate a key for encryption.</param>
    public static string EncryptStringAes(string? plainText, string? sharedSecret)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentNullException.ThrowIfNull(sharedSecret);

        string? outStr; // Encrypted string to return
        Aes? aesAlg; // RijndaelManaged object used to encrypt the data.

        // generate the key from the shared secret and the salt
#pragma warning disable CA5379
        Rfc2898DeriveBytes key = new(sharedSecret, s_salt, 1000, HashAlgorithmName.SHA1);
#pragma warning restore CA5379

        // Create a RijndaelManaged object
        aesAlg = Aes.Create();
        aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);

        // Create a decryptor to perform the stream transform.
        ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

        // Create the streams used for encryption.
        using MemoryStream msEncrypt = new();
        // prepend the IV
        msEncrypt.Write(BitConverter.GetBytes(aesAlg.IV.Length), 0, sizeof(int));
        msEncrypt.Write(aesAlg.IV, 0, aesAlg.IV.Length);
        using (CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write))
        {
            using (StreamWriter swEncrypt = new(csEncrypt))
            {
                //Write all data to the stream.
                swEncrypt.Write(plainText);
            }
        }
        outStr = Convert.ToBase64String(msEncrypt.ToArray());

        // Return the encrypted bytes from the memory stream.
        return outStr;
    }

    /// <summary>
    /// Decrypt the given string.  Assumes the string was encrypted using
    /// EncryptStringAES(), using an identical sharedSecret.
    /// </summary>
    /// <param name="cipherText">The text to decrypt.</param>
    /// <param name="sharedSecret">A password used to generate a key for decryption.</param>
    public static string DecryptStringAes(string? cipherText, string? sharedSecret)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        ArgumentNullException.ThrowIfNull(sharedSecret);

        // Declare the RijndaelManaged object
        // used to decrypt the data.
        Aes? aesAlg = null;

        // Declare the string used to hold
        // the decrypted text.
        string? plaintext;

        // generate the key from the shared secret and the salt
#pragma warning disable CA5379
        Rfc2898DeriveBytes key = new(sharedSecret, s_salt, 1000, HashAlgorithmName.SHA1);
#pragma warning restore CA5379

        // Create the streams used for decryption.
        byte[] bytes = Convert.FromBase64String(cipherText);
        using MemoryStream msDecrypt = new(bytes);
        // Create a RijndaelManaged object
        // with the specified key and IV.
        aesAlg = Aes.Create();
        aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
        // Get the initialization vector from the encrypted stream
        aesAlg.IV = ReadByteArray(msDecrypt);
        // Create a decrytor to perform the stream transform.
        ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
        using CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read);
        using StreamReader srDecrypt = new(csDecrypt);
        plaintext = srDecrypt.ReadToEnd();

        return plaintext;
    }

    [SuppressMessage("Usage", "CA2201:Keine reservierten Ausnahmetypen auslösen")]
    private static byte[] ReadByteArray(Stream s)
    {
        byte[] rawLength = new byte[sizeof(int)];
        if (s.Read(rawLength, 0, rawLength.Length) != rawLength.Length)
        {
            throw new SystemException("Stream did not contain properly formatted byte array");
        }

        byte[] buffer = new byte[BitConverter.ToInt32(rawLength, 0)];
        return s.Read(buffer, 0, buffer.Length) != buffer.Length
            ? throw new SystemException("Did not read byte array properly")
            : buffer;
    }
}
