using System.Numerics;

namespace OpenKO.Client.Engine.Sky;

/// <summary>The interpolated sky/fog colour pair at a moment of the day.</summary>
public readonly record struct SkyColorSample(uint SkyColor, uint FogColor);

/// <summary>One point of a placed star (unit-ish direction + its base alpha).</summary>
public readonly record struct StarPoint(Vector3 Position, byte BaseAlpha);

/// <summary>
/// Pure port of the day-change colour simulation and the sun/moon arc that
/// <c>CN3SkyMng</c> drives from the game clock (Client/N3Base/N3SkyMng.cpp
/// <c>Tick</c> / <c>SetCheckGameTime</c> / <c>ChangeSky</c>, plus
/// <c>CN3Sun</c>/<c>CN3Moon</c>/<c>CN3Star</c>). Everything here is
/// deterministic and headless-testable; the device layer feeds these outputs
/// into the fans and billboards.
///
/// The C++ time source is a seconds-of-day counter (0..86400) advanced at
/// TIME_REAL_PER_GAME=10 (game runs 10× real time). There is no WIZ_TIME hook
/// in the port yet, so this layer is driven from an injected time-of-day
/// (hour+minute, seconds, or a 0..1 day fraction) — the caller supplies it.
/// </summary>
public static class DayNightCycle
{
    /// <summary>Seconds in a game day (CONVERT_SEC(24,0,0)).</summary>
    public const int SecondsPerDay = 86400;

    /// <summary>Real/game time ratio (N3SkyMng.h TIME_REAL_PER_GAME).</summary>
    public const float TimeRealPerGame = 10.0f;

    /// <summary>Maximum number of stars (N3Star.h MAX_STAR).</summary>
    public const int MaxStars = 200;

    // ---- SDC_SKYCOLOR keyframes (CN3SkyMng::InitToDefaultHardCoding) -------
    // ARGB targets reached fHowLong*TIME_REAL_PER_GAME game-seconds after dwWhen.
    public const uint SkySunrise = 0xFFA57355; // 05:00  (165,115, 85)
    public const uint SkyNoon = 0xFF2E61BD;    // 06:00  ( 46, 97,189)
    public const uint SkySunset = 0xFF62737D;  // 20:00  ( 98,115,125)
    public const uint SkyMidnight = 0xFF0F1231;// 21:00  ( 15, 18, 49)

    // ---- SDC_FOGCOLOR keyframes (drives the horizon fans) -----------------
    public const uint FogSunrise = 0xFF506EA0; // 05:00  ( 80,110,160)
    public const uint FogNoon = 0xFFA9CBD7;    // 06:00  (169,203,215)
    public const uint FogSunset = 0xFF87A29F;  // 20:00  (135,162,159)
    public const uint FogMidnight = 0xFF273459;// 21:00  ( 39, 52, 95)

    private readonly struct ColorKey(int startSec, uint target, float howLongReal)
    {
        public readonly int StartSec = startSec;
        public readonly uint Target = target;
        // Change duration in *game* seconds (fHowLong is real seconds).
        public readonly float DurationSec = howLongReal * TimeRealPerGame;
    }

    private readonly struct ScalarKey(int startSec, float target, float howLongReal)
    {
        public readonly int StartSec = startSec;
        public readonly float Target = target;
        public readonly float DurationSec = howLongReal * TimeRealPerGame;
    }

    private static int Sec(int h, int m) => (h * 3600) + (m * 60);

    // Keyframes kept sorted by StartSec, exactly as qsort(CompareTime) leaves them.
    private static readonly ColorKey[] SkyKeys =
    [
        new(Sec(5, 0), SkySunrise, 180f),
        new(Sec(6, 0), SkyNoon, 180f),
        new(Sec(20, 0), SkySunset, 180f),
        new(Sec(21, 0), SkyMidnight, 180f),
    ];

    private static readonly ColorKey[] FogKeys =
    [
        new(Sec(5, 0), FogSunrise, 180f),
        new(Sec(6, 0), FogNoon, 180f),
        new(Sec(20, 0), FogSunset, 180f),
        new(Sec(21, 0), FogMidnight, 180f),
    ];

    // SDC_STARCOUNT: 0 at 06:00, MAX_STAR at 21:00 (both over 300 real-seconds).
    private static readonly ScalarKey[] StarKeys =
    [
        new(Sec(6, 0), 0f, 300f),
        new(Sec(21, 0), MaxStars, 300f),
    ];

    /// <summary>Day fraction (0..1) from a game clock hour+minute, wrapping.</summary>
    public static float DayFraction(int hour, int minute)
    {
        int total = (((hour * 60) + minute) % 1440 + 1440) % 1440;
        return total / 1440f;
    }

    /// <summary>Day fraction (0..1) from a seconds-of-day counter, wrapping.</summary>
    public static float DayFractionFromSeconds(float seconds)
    {
        float f = (seconds % SecondsPerDay + SecondsPerDay) % SecondsPerDay;
        return f / SecondsPerDay;
    }

    /// <summary>
    /// Sun world direction on its arc: <c>RotationZ(dayFraction·360°+270°)</c>
    /// applied to (5,0,0) then normalised — matches
    /// <c>m_pSun->SetCurAngle(fAngleTime+270)</c> + <c>CN3Sun::Render</c>.
    /// Y&gt;0 means the sun is above the horizon (noon), Y&lt;0 below (midnight).
    /// </summary>
    public static Vector3 SunDirection(float dayFraction)
    {
        float radians = ((dayFraction * 360f) + 270f) * (MathF.PI / 180f);
        return new Vector3(MathF.Cos(radians), MathF.Sin(radians), 0f);
    }

    /// <summary>
    /// Moon world direction: <c>fAngleTime+90°</c> — 180° opposite the sun, so
    /// the moon rises as the sun sets (CN3Moon::SetCurAngle).
    /// </summary>
    public static Vector3 MoonDirection(float dayFraction)
    {
        float radians = ((dayFraction * 360f) + 90f) * (MathF.PI / 180f);
        return new Vector3(MathF.Cos(radians), MathF.Sin(radians), 0f);
    }

    /// <summary>The simulated sky colour (SDC_SKYCOLOR) at this day fraction.</summary>
    public static uint SkyColor(float dayFraction) => Evaluate(SkyKeys, DayToSec(dayFraction));

    /// <summary>The simulated fog colour (SDC_FOGCOLOR) — feeds the horizon fans.</summary>
    public static uint FogColor(float dayFraction) => Evaluate(FogKeys, DayToSec(dayFraction));

    /// <summary>Both colours at once.</summary>
    public static SkyColorSample Sample(float dayFraction)
    {
        float t = DayToSec(dayFraction);
        return new SkyColorSample(Evaluate(SkyKeys, t), Evaluate(FogKeys, t));
    }

    /// <summary>
    /// Star field visibility 0..1 (current star count / MAX_STAR): 0 at noon,
    /// 1 at midnight, ramping across dusk/dawn (SDC_STARCOUNT).
    /// </summary>
    public static float StarAlpha(float dayFraction)
        => EvaluateScalar(StarKeys, DayToSec(dayFraction)) / MaxStars;

    /// <summary>Current animated star count (0..MAX_STAR), rounded.</summary>
    public static int StarCount(float dayFraction)
        => (int)MathF.Round(EvaluateScalar(StarKeys, DayToSec(dayFraction)));

    /// <summary>
    /// Deterministic star placement (CN3Star::Init): MAX_STAR points in a box,
    /// rejecting any within radius 2 of the camera, with a descending base
    /// alpha ramp (0xFF..0x80). C++ uses rand(); this uses a seeded
    /// <see cref="Random"/> so the field is reproducible but not bit-identical.
    /// </summary>
    public static StarPoint[] GenerateStarField(int seed)
    {
        var random = new Random(seed);
        var stars = new StarPoint[MaxStars];
        const int alphaMin = 0x80;
        const int alphaMax = 0xFF;
        float inc = (float)(alphaMax - alphaMin) / MaxStars;
        for (int i = 0; i < MaxStars; i++)
        {
            float x, y, z;
            do
            {
                x = (random.Next(10000) / 1000f) - 5.0f;
                y = (random.Next(10000) / 1000f) - 2.0f;
                z = (random.Next(10000) / 1000f) - 5.0f;
            }
            while ((x * x) + (y * y) + (z * z) < 2.0f * 2.0f);

            byte alpha = (byte)(alphaMax - (int)(inc * i));
            stars[i] = new StarPoint(new Vector3(x, y, z), alpha);
        }

        return stars;
    }

    private static float DayToSec(float dayFraction)
    {
        float f = dayFraction - MathF.Floor(dayFraction); // wrap into [0,1)
        return f * SecondsPerDay;
    }

    // Port of the "most recent change, ramping toward its target" evaluation
    // in CN3SkyMng::SetCheckGameTime (GetLatestChange + ChangeSky + SetPercentage).
    private static uint Evaluate(ColorKey[] keys, float t)
    {
        FindActive(keys.Length, i => keys[i].StartSec, t, out int active, out int prev, out float elapsed);
        float pct = keys[active].DurationSec <= 0f
            ? 1f
            : Math.Clamp(elapsed / keys[active].DurationSec, 0f, 1f);
        return LerpColor(keys[prev].Target, keys[active].Target, pct);
    }

    private static float EvaluateScalar(ScalarKey[] keys, float t)
    {
        FindActive(keys.Length, i => keys[i].StartSec, t, out int active, out int prev, out float elapsed);
        float pct = keys[active].DurationSec <= 0f
            ? 1f
            : Math.Clamp(elapsed / keys[active].DurationSec, 0f, 1f);
        return keys[prev].Target + ((keys[active].Target - keys[prev].Target) * pct);
    }

    private static void FindActive(
        int count, Func<int, int> startOf, float t,
        out int active, out int prev, out float elapsed)
    {
        active = -1;
        for (int i = 0; i < count; i++)
        {
            if (startOf(i) <= t)
                active = i;
        }

        bool wrapped = active < 0;
        if (wrapped)
            active = count - 1; // the last change of the previous day still holds

        prev = active - 1;
        if (prev < 0)
            prev = count - 1;

        elapsed = wrapped
            ? t + SecondsPerDay - startOf(active)
            : t - startOf(active);
    }

    private static uint LerpColor(uint from, uint to, float pct)
    {
        byte La(int shift)
        {
            float a = (from >> shift) & 0xFF;
            float b = (to >> shift) & 0xFF;
            return (byte)MathF.Round(a + ((b - a) * pct));
        }

        return ((uint)La(24) << 24) | ((uint)La(16) << 16) | ((uint)La(8) << 8) | La(0);
    }
}
