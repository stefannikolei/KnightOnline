using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenKO.Core.Crypto;

/// <summary>
/// Port of <c>shared/JvCryption.cpp</c> — the Knight Online packet cipher.
/// The transform is symmetric (decryption == encryption) and must stay bit-exact
/// with the C++ implementation; the golden-vector tests pin it.
/// </summary>
public sealed class JvCryption
{
    private const ulong PrivateKey = 0x1234567890123456UL;

    private ulong _tkey;

    public ulong PublicKey { get; private set; }

    public void SetPublicKey(ulong key)
    {
        PublicKey = key;
    }

    public void Init()
    {
        _tkey = PublicKey ^ PrivateKey;
    }

    public ulong GenerateKey()
    {
        // A zero key would effectively disable the encryption (see the C++ comment),
        // so keep rolling until we get a non-zero one.
        Span<byte> buf = stackalloc byte[8];
        do
        {
            RandomNumberGenerator.Fill(buf);
            PublicKey = BinaryPrimitives.ReadUInt64LittleEndian(buf);
        }
        while (PublicKey == 0);

        Init();
        return PublicKey;
    }

    /// <summary>
    /// JvEncryptionFast / JvDecryptionFast (the cipher is an involution).
    /// <paramref name="input"/> and <paramref name="output"/> may refer to the same memory.
    /// </summary>
    public void Transform(ReadOnlySpan<byte> input, Span<byte> output)
    {
        // The C++ takes the 8 key bytes via pointer aliasing ((uint8_t*)&m_tkey),
        // i.e. in little-endian memory order.
        Span<byte> tkeyBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tkeyBytes, _tkey);

        int rkey = 2157;
        byte lkey = (byte)(input.Length * 157);

        for (int i = 0; i < input.Length; i++)
        {
            byte rsk = (byte)(rkey >> 8);
            output[i] = (byte)(((input[i] ^ rsk) ^ tkeyBytes[i & 7]) ^ lkey);
            // Deliberate signed 32-bit wrapping multiply, as in the C++ (int rkey).
            rkey = unchecked(rkey * 2171);
        }
    }

    /// <summary>
    /// JvDecryptionWithCRC32: decrypt, then validate the trailing little-endian CRC32
    /// (seeded with 0xFFFFFFFF, no final XOR) over the payload.
    /// Returns the payload length (input length - 4) or -1 on CRC mismatch.
    /// </summary>
    public int DecryptWithCrc32(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.Length < 4)
            return -1;

        Transform(input, output);

        int payloadLen = input.Length - 4;
        uint computed = KoCrc32.Compute(output[..payloadLen], 0xFFFFFFFF);
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(output.Slice(payloadLen, 4));
        return computed == stored ? payloadLen : -1;
    }
}
