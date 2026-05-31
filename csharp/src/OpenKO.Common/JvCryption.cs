using System.Buffers.Binary;

namespace OpenKO.Common;

/// <summary>
/// Port of the C++ <c>CJvCryption</c> (shared/JvCryption.cpp) — the KO packet stream cipher.
///
/// Encryption and decryption are the same operation (a reversible XOR keystream), so
/// <see cref="Decrypt"/> simply forwards to <see cref="Encrypt"/>. The implementation is kept
/// byte-for-byte identical to the original so encrypted traffic stays compatible with the
/// official server.
/// </summary>
public sealed class JvCryption
{
    private const ulong PrivateKey = 0x1234567890123456UL;

    private ulong _publicKey;
    private ulong _tkey;

    public ulong PublicKey
    {
        get => _publicKey;
        set => _publicKey = value;
    }

    public void Init()
    {
        _tkey = _publicKey ^ PrivateKey;
    }

    /// <summary>Generates a non-zero public key and initialises the cipher.</summary>
    public ulong GenerateKey(Func<ulong>? rng = null)
    {
        rng ??= DefaultRng;
        do
        {
            _publicKey = rng();
        }
        while (_publicKey == 0);

        Init();
        return _publicKey;
    }

    private static ulong DefaultRng()
    {
        Span<byte> bytes = stackalloc byte[8];
        Random.Shared.NextBytes(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    /// <summary>Encrypts <paramref name="length"/> bytes from <paramref name="input"/> into <paramref name="output"/>.</summary>
    public void Encrypt(int length, ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> pkey = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(pkey, _tkey);

        // rkey is a 32-bit value multiplied each step; matches the original int arithmetic (wraps at 32 bits).
        uint rkey = 2157;
        byte lkey = (byte)((length * 157) & 0xff);

        for (int i = 0; i < length; i++)
        {
            byte rsk = (byte)((rkey >> 8) & 0xff);
            output[i] = (byte)(((input[i] ^ rsk) ^ pkey[i % 8]) ^ lkey);
            rkey *= 2171;
        }
    }

    /// <summary>Decryption is identical to encryption for this cipher.</summary>
    public void Decrypt(int length, ReadOnlySpan<byte> input, Span<byte> output)
        => Encrypt(length, input, output);

    /// <summary>
    /// Decrypts and validates the trailing CRC-32. Returns the payload length (len - 4) on success
    /// or -1 if the CRC does not match.
    /// </summary>
    public int DecryptWithCrc32(int length, ReadOnlySpan<byte> input, Span<byte> output)
    {
        Decrypt(length, input, output);
        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(output.Slice(length - 4, 4));
        return Crc32.Compute(output[..(length - 4)]) == expected ? length - 4 : -1;
    }
}
