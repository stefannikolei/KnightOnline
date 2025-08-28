﻿using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
#pragma warning disable SYSLIB0041
#pragma warning disable SYSLIB0022
#pragma warning disable CA1507
#pragma warning disable CA5379

namespace OpenKO.Web.Tests;

[SuppressMessage("Usage", "CA2201:Keine reservierten Ausnahmetypen auslösen")]
public class LegacyCrypto
{
    private static readonly byte[] s_salt = Encoding.ASCII.GetBytes("o6806642kbM7c5");

    /// <summary>
    /// Encrypt the given string using AES.  The string can be decrypted using
    /// DecryptStringAES().  The sharedSecret parameters must match.
    /// </summary>
    /// <param name="plainText">The text to encrypt.</param>
    /// <param name="sharedSecret">A password used to generate a key for encryption.</param>
    public static string EncryptStringAes(string plainText, string sharedSecret)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            throw new ArgumentNullException("plainText");
        }

        if (string.IsNullOrEmpty(sharedSecret))
        {
            throw new ArgumentNullException("sharedSecret");
        }

        string? outStr; // Encrypted string to return
        RijndaelManaged? aesAlg = null; // RijndaelManaged object used to encrypt the data.

        try
        {
            // generate the key from the shared secret and the salt
            Rfc2898DeriveBytes key = new(sharedSecret, s_salt);

            // Create a RijndaelManaged object
            aesAlg = new RijndaelManaged();
            aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);

            // Create a decryptor to perform the stream transform.
            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            // Create the streams used for encryption.
            using (MemoryStream msEncrypt = new())
            {
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
            }
        }
        finally
        {
            // Clear the RijndaelManaged object.
            aesAlg?.Clear();
        }

        // Return the encrypted bytes from the memory stream.
        return outStr;
    }

    /// <summary>
    /// Decrypt the given string.  Assumes the string was encrypted using
    /// EncryptStringAES(), using an identical sharedSecret.
    /// </summary>
    /// <param name="cipherText">The text to decrypt.</param>
    /// <param name="sharedSecret">A password used to generate a key for decryption.</param>
    public static string DecryptStringAes(string cipherText, string sharedSecret)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            throw new ArgumentNullException("cipherText");
        }

        if (string.IsNullOrEmpty(sharedSecret))
        {
            throw new ArgumentNullException("sharedSecret");
        }

        // Declare the RijndaelManaged object
        // used to decrypt the data.
        RijndaelManaged? aesAlg = null;

        // Declare the string used to hold
        // the decrypted text.
        string? plaintext;

        try
        {
            // generate the key from the shared secret and the salt
            Rfc2898DeriveBytes key = new(sharedSecret, s_salt);

            // Create the streams used for decryption.
            byte[] bytes = Convert.FromBase64String(cipherText);
            using (MemoryStream msDecrypt = new(bytes))
            {
                // Create a RijndaelManaged object
                // with the specified key and IV.
                aesAlg = new RijndaelManaged();
                aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
                // Get the initialization vector from the encrypted stream
                aesAlg.IV = ReadByteArray(msDecrypt);
                // Create a decrytor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
                using (CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read))
                {
                    using (StreamReader srDecrypt = new(csDecrypt))
                    // Read the decrypted bytes from the decrypting stream
                    // and place them in a string.
                    {
                        plaintext = srDecrypt.ReadToEnd();
                    }
                }
            }
        }
        finally
        {
            // Clear the RijndaelManaged object.
            aesAlg?.Clear();
        }

        return plaintext;
    }

    private static byte[] ReadByteArray(Stream s)
    {
        byte[] rawLength = new byte[sizeof(int)];
        if (s.Read(rawLength, 0, rawLength.Length) != rawLength.Length)
        {
            throw new SystemException("Stream did not contain properly formatted byte array");
        }

        byte[] buffer = new byte[BitConverter.ToInt32(rawLength, 0)];
        if (s.Read(buffer, 0, buffer.Length) != buffer.Length)
        {
            throw new SystemException("Did not read byte array properly");
        }

        return buffer;
    }
}
