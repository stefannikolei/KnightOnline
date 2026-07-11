namespace OpenKO.Core.Hashing;

/// <summary>
/// Port of <c>deps/djb2/djb2_hasher.h</c>: classic djb2 (h = h*33 + c) with seed 5381
/// over 64-bit state (std::size_t on x64). The C++ static_casts each (signed) char,
/// which sign-extends bytes &gt;= 0x80 — replicated here for bit-exact hashes.
/// Used by the Ebenezer quest VM for opcode dispatch ("GIVE_ITEM"_djb2 etc.).
/// </summary>
public static class Djb2
{
    public const ulong Seed = 5381;

    public static ulong Hash(ReadOnlySpan<byte> data, ulong seed = Seed)
    {
        ulong h = seed;
        foreach (byte b in data)
            h = unchecked(((h << 5) + h) + (ulong)(long)(sbyte)b);
        return h;
    }

    public static ulong Hash(string text, ulong seed = Seed)
    {
        ulong h = seed;
        foreach (char c in text)
            h = unchecked(((h << 5) + h) + c);
        return h;
    }
}
