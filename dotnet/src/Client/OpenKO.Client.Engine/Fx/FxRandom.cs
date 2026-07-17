namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// A tiny deterministic RNG standing in for the C library <c>rand()</c> the N3
/// effect code uses for particle spread/jitter (N3FXPartParticles.cpp). The N3
/// client is built with MSVC, whose <c>rand()</c> is a linear congruential
/// generator returning a value in <c>[0, 0x7fff]</c>; reproducing that formula
/// keeps the emit jitter faithful, and because it is seedable and self-contained
/// the whole particle simulation is bit-deterministic and headless-testable
/// (no <c>System.Random.Shared</c>, no wall-clock, no <c>Math.Random</c>).
/// </summary>
public struct FxRandom
{
    /// <summary>RAND_MAX for MSVC's <c>rand()</c>.</summary>
    public const int RandMax = 0x7fff;

    private uint _state;

    public FxRandom(uint seed) => _state = seed;

    /// <summary>The MSVC <c>rand()</c>: advance the LCG, return bits 30..16 (0..32767).</summary>
    public int Next()
    {
        _state = (_state * 214013u) + 2531011u;
        return (int)((_state >> 16) & 0x7fff);
    }

    /// <summary>The C idiom <c>rand() % n</c>. <paramref name="n"/> must be &gt; 0.</summary>
    public int NextMod(int n) => n <= 0 ? 0 : Next() % n;

    /// <summary>
    /// The recurring N3 expression <c>(float)(rand()%100) / 100.0f</c> — a value
    /// in <c>[0, 0.99]</c> used to lerp size/life/create-range/spread.
    /// </summary>
    public float NextUnit() => NextMod(100) / 100.0f;
}
