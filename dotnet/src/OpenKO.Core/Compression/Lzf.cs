namespace OpenKO.Core.Compression;

/// <summary>
/// Port of <c>shared/lzf.cpp</c> (LibLZF 1.5) with the exact configuration used there:
/// HLOG=16, VERY_FAST=1, ULTRA_FAST=0, CHECK_INPUT=1.
/// The compressor's output depends on these parameters, so they must not be changed —
/// golden-vector tests pin byte-identical output against the C++ implementation.
/// The C++ leaves its hash table uninitialized on the stack (INIT_HTAB=0); a zeroed
/// table is equivalent because index 0 is rejected by the ref &gt; in_data check, which
/// is also what the C++ sees on a freshly mapped (zeroed) stack.
/// </summary>
public static class Lzf
{
    private const int Hlog = 16;
    private const int Hsize = 1 << Hlog;
    private const int MaxLit = 1 << 5;
    private const int MaxOff = 1 << 13;
    private const int MaxRef = (1 << 8) + (1 << 3);

    // IDX(h) for VERY_FAST: ((h >> (3*8 - HLOG)) - h*5) & (HSIZE - 1)
    private static int Idx(uint hval) => (int)(((hval >> (3 * 8 - Hlog)) - hval * 5) & (Hsize - 1));

    /// <summary>
    /// Returns the compressed length, or 0 if the output buffer is too small
    /// (callers then send the data uncompressed, as the C++ does).
    /// </summary>
    public static int Compress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int inEnd = input.Length;
        int outEnd = output.Length;

        if (inEnd == 0 || outEnd == 0)
            return 0;

        var htab = new int[Hsize];

        int ip = 0;
        int op = 0;
        int lit = 0;
        op++; /* start run */

        if (inEnd > 2)
        {
            uint hval = (uint)((input[0] << 8) | input[1]); /* FRST */
            while (ip < inEnd - 2)
            {
                hval = (hval << 8) | input[ip + 2]; /* NEXT */
                int slot = Idx(hval);
                int refIdx = htab[slot];
                htab[slot] = ip;

                long off = ip - refIdx - 1;
                if ((ulong)off < MaxOff
                    && ip + 4 < inEnd
                    && refIdx > 0
                    && input[refIdx] == input[ip]
                    && input[refIdx + 1] == input[ip + 1]
                    && input[refIdx + 2] == input[ip + 2])
                {
                    /* match found at refIdx */
                    int len = 2;
                    int maxlen = inEnd - ip - len;
                    maxlen = maxlen > MaxRef ? MaxRef : maxlen;

                    if (op + 3 + 1 >= outEnd)      /* first a faster conservative test */
                        if (op - (lit == 0 ? 1 : 0) + 3 + 1 >= outEnd) /* second the exact but rare test */
                            return 0;

                    output[op - lit - 1] = (byte)(lit - 1); /* stop run */
                    if (lit == 0)
                        op--; /* undo run if length is zero */

                    while (true)
                    {
                        if (maxlen > 16)
                        {
                            bool broke = false;
                            for (int u = 0; u < 16; u++)
                            {
                                len++;
                                if (input[refIdx + len] != input[ip + len])
                                {
                                    broke = true;
                                    break;
                                }
                            }

                            if (broke)
                                break;
                        }

                        do
                        {
                            len++;
                        }
                        while (len < maxlen && input[refIdx + len] == input[ip + len]);

                        break;
                    }

                    len -= 2; /* len is now #octets - 1 */
                    ip++;

                    if (len < 7)
                    {
                        output[op++] = (byte)((off >> 8) + (len << 5));
                    }
                    else
                    {
                        output[op++] = (byte)((off >> 8) + (7 << 5));
                        output[op++] = (byte)(len - 7);
                    }

                    output[op++] = (byte)off;
                    lit = 0;
                    op++; /* start run */

                    ip += len + 1;

                    if (ip >= inEnd - 2)
                        break;

                    /* VERY_FAST && !ULTRA_FAST: re-insert two hash entries */
                    ip -= 2;

                    hval = (uint)((input[ip] << 8) | input[ip + 1]); /* FRST */
                    hval = (hval << 8) | input[ip + 2];              /* NEXT */
                    htab[Idx(hval)] = ip;
                    ip++;

                    hval = (hval << 8) | input[ip + 2];              /* NEXT */
                    htab[Idx(hval)] = ip;
                    ip++;
                }
                else
                {
                    /* one more literal byte we must copy */
                    if (op >= outEnd)
                        return 0;

                    lit++;
                    output[op++] = input[ip++];

                    if (lit == MaxLit)
                    {
                        output[op - lit - 1] = (byte)(lit - 1); /* stop run */
                        lit = 0;
                        op++; /* start run */
                    }
                }
            }
        }

        if (op + 3 > outEnd) /* at most 3 bytes can be missing here */
            return 0;

        while (ip < inEnd)
        {
            lit++;
            output[op++] = input[ip++];

            if (lit == MaxLit)
            {
                output[op - lit - 1] = (byte)(lit - 1); /* stop run */
                lit = 0;
                op++; /* start run */
            }
        }

        output[op - lit - 1] = (byte)(lit - 1); /* end run */
        if (lit == 0)
            op--; /* undo run if length is zero */

        return op;
    }

    /// <summary>
    /// Returns the decompressed length, or 0 on a truncated/corrupt stream or
    /// insufficient output space (mirrors the C++ EINVAL/E2BIG returns).
    /// </summary>
    public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int inEnd = input.Length;
        int outEnd = output.Length;

        if (inEnd == 0)
            return 0;

        int ip = 0;
        int op = 0;

        do
        {
            uint ctrl = input[ip++];

            if (ctrl < 1 << 5) /* literal run */
            {
                ctrl++;

                if (op + ctrl > (uint)outEnd)
                    return 0; /* E2BIG */

                if (ip + ctrl > (uint)inEnd)
                    return 0; /* EINVAL (CHECK_INPUT) */

                do
                {
                    output[op++] = input[ip++];
                }
                while (--ctrl != 0);
            }
            else /* back reference */
            {
                uint len = ctrl >> 5;

                int refIdx = (int)(op - ((ctrl & 0x1f) << 8) - 1);

                if (ip >= inEnd)
                    return 0; /* EINVAL */

                if (len == 7)
                {
                    len += input[ip++];

                    if (ip >= inEnd)
                        return 0; /* EINVAL */
                }

                refIdx -= input[ip++];

                if (op + len + 2 > (uint)outEnd)
                    return 0; /* E2BIG */

                if (refIdx < 0)
                    return 0; /* EINVAL */

                /* byte-by-byte copy is required: back references may overlap the output cursor */
                output[op++] = output[refIdx++];
                output[op++] = output[refIdx++];

                do
                {
                    output[op++] = output[refIdx++];
                }
                while (--len != 0);
            }
        }
        while (ip < inEnd);

        return op;
    }
}
