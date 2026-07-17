using System.Numerics;
using OpenKO.Client.Engine.Sky;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Slice-9.11a pins: the pure day-night colour simulation and the sun/moon arc
/// (CN3SkyMng day-change table + CN3Sun/CN3Moon/CN3Star), driven by an injected
/// game day fraction.
/// </summary>
public class DayNightCycleTests
{
    // Times at which a change has fully settled (start + 180 real-sec = +30 game-min).
    private static float Frac(int hour, int minute) => DayNightCycle.DayFraction(hour, minute);

    [Fact]
    public void SkyAndFogColour_SettleToTheKeyValues()
    {
        // Each SDC change ramps over 180 real-sec (= 30 game-min) after its start.
        // Sunrise 05:00 → 05:30, Noon 06:00 → 06:30, Sunset 20:00 → 20:30,
        // Midnight 21:00 → 21:30.
        Assert.Equal(DayNightCycle.SkySunrise, DayNightCycle.SkyColor(Frac(5, 30)));
        Assert.Equal(DayNightCycle.SkyNoon, DayNightCycle.SkyColor(Frac(6, 30)));
        Assert.Equal(DayNightCycle.SkySunset, DayNightCycle.SkyColor(Frac(20, 30)));
        Assert.Equal(DayNightCycle.SkyMidnight, DayNightCycle.SkyColor(Frac(21, 30)));

        Assert.Equal(DayNightCycle.FogSunrise, DayNightCycle.FogColor(Frac(5, 30)));
        Assert.Equal(DayNightCycle.FogNoon, DayNightCycle.FogColor(Frac(6, 30)));
        Assert.Equal(DayNightCycle.FogSunset, DayNightCycle.FogColor(Frac(20, 30)));
        Assert.Equal(DayNightCycle.FogMidnight, DayNightCycle.FogColor(Frac(21, 30)));
    }

    [Fact]
    public void SkyColour_HoldsTheDayColour_ThroughMidday()
    {
        // Between the settled noon (06:30) and the sunset start (20:00) the sky
        // holds the noon key.
        Assert.Equal(DayNightCycle.SkyNoon, DayNightCycle.SkyColor(Frac(12, 0)));
        Assert.Equal(DayNightCycle.FogNoon, DayNightCycle.FogColor(Frac(15, 0)));
    }

    [Fact]
    public void SkyColour_HoldsMidnight_ThroughTheDeepNight()
    {
        // From settled midnight (21:30) across 00:00 to the sunrise start (05:00)
        // the sky holds the midnight key — including the wrap through 00:00.
        Assert.Equal(DayNightCycle.SkyMidnight, DayNightCycle.SkyColor(Frac(23, 0)));
        Assert.Equal(DayNightCycle.SkyMidnight, DayNightCycle.SkyColor(Frac(0, 0)));
        Assert.Equal(DayNightCycle.SkyMidnight, DayNightCycle.SkyColor(Frac(3, 0)));
    }

    [Fact]
    public void SkyColour_RampsBetweenKeys_AtTheHalfwayPoint()
    {
        // 20:15 is halfway through the 20:00→20:30 sunset ramp: each channel is
        // the midpoint of the noon key and the sunset key.
        uint mid = DayNightCycle.SkyColor(Frac(20, 15));
        uint a = DayNightCycle.SkyNoon;
        uint b = DayNightCycle.SkySunset;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            int ca = (int)((a >> shift) & 0xFF);
            int cb = (int)((b >> shift) & 0xFF);
            int cm = (int)((mid >> shift) & 0xFF);
            Assert.InRange(cm, Math.Min(ca, cb), Math.Max(ca, cb));
            Assert.True(Math.Abs(((ca + cb) / 2) - cm) <= 1); // within a rounding step of the midpoint
        }
    }

    [Fact]
    public void SunAndMoon_AreOpposite()
    {
        for (int h = 0; h < 24; h += 3)
        {
            Vector3 sun = DayNightCycle.SunDirection(Frac(h, 0));
            Vector3 moon = DayNightCycle.MoonDirection(Frac(h, 0));
            Assert.Equal(-sun.X, moon.X, 4);
            Assert.Equal(-sun.Y, moon.Y, 4);
            Assert.Equal(-sun.Z, moon.Z, 4);
        }
    }

    [Fact]
    public void Sun_IsUpAtNoon_DownAtMidnight()
    {
        Vector3 noon = DayNightCycle.SunDirection(Frac(12, 0));
        Vector3 midnight = DayNightCycle.SunDirection(Frac(0, 0));

        Assert.True(noon.Y > 0.99f);      // sun at the top of its arc
        Assert.True(midnight.Y < -0.99f); // sun below the horizon

        // The moon mirrors it.
        Assert.True(DayNightCycle.MoonDirection(Frac(0, 0)).Y > 0.99f);
        Assert.True(DayNightCycle.MoonDirection(Frac(12, 0)).Y < -0.99f);
    }

    [Fact]
    public void StarAlpha_ZeroAtNoon_MaxAtMidnight_RampsAtDusk()
    {
        Assert.Equal(0f, DayNightCycle.StarAlpha(Frac(12, 0)), 4);
        Assert.Equal(1f, DayNightCycle.StarAlpha(Frac(0, 0)), 4);

        // Dusk ramp: 21:00 → 21:50 fades 0 → 1. 21:25 is roughly halfway.
        float dusk = DayNightCycle.StarAlpha(Frac(21, 25));
        Assert.InRange(dusk, 0.1f, 0.9f);

        // Monotonic increase across the dusk window.
        Assert.True(DayNightCycle.StarAlpha(Frac(21, 10)) < DayNightCycle.StarAlpha(Frac(21, 40)));
    }

    [Fact]
    public void DayFraction_WrapsContinuously_AcrossMidnight()
    {
        // 23:59 → 00:00 must be continuous (no jump in the arc or colour).
        float before = Frac(23, 59);
        float after = Frac(0, 0);

        Assert.Equal(0f, after, 5);
        Assert.True(before > 0.999f);

        // Sun direction is nearly identical either side of the wrap.
        Vector3 sBefore = DayNightCycle.SunDirection(before);
        Vector3 sAfter = DayNightCycle.SunDirection(after);
        Assert.Equal(sBefore.X, sAfter.X, 2);
        Assert.Equal(sBefore.Y, sAfter.Y, 2);

        // Colour is continuous too (both deep-night = midnight key).
        Assert.Equal(DayNightCycle.SkyColor(before), DayNightCycle.SkyColor(after));
    }

    [Fact]
    public void DayFractionFromSeconds_MatchesHourMinute_AndWraps()
    {
        Assert.Equal(0.5f, DayNightCycle.DayFractionFromSeconds(43200f), 5); // 12:00
        Assert.Equal(DayNightCycle.DayFraction(12, 0), DayNightCycle.DayFractionFromSeconds(43200f), 5);
        // Wrap: 86400 + 3600 == 01:00.
        Assert.Equal(DayNightCycle.DayFraction(1, 0), DayNightCycle.DayFractionFromSeconds(90000f), 5);
    }

    [Fact]
    public void StarField_IsDeterministic_AndRespectsTheExclusionRadius()
    {
        StarPoint[] a = DayNightCycle.GenerateStarField(seed: 1234);
        StarPoint[] b = DayNightCycle.GenerateStarField(seed: 1234);

        Assert.Equal(DayNightCycle.MaxStars, a.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Position, b[i].Position);
            Assert.Equal(a[i].BaseAlpha, b[i].BaseAlpha);
            // No star inside radius 2 of the camera (CN3Star::Init reject loop).
            Assert.True(a[i].Position.LengthSquared() >= 4f - 0.001f);
        }

        // Base alpha ramps down from 0xFF toward 0x80.
        Assert.Equal(0xFF, a[0].BaseAlpha);
        Assert.True(a[^1].BaseAlpha <= a[0].BaseAlpha);
        Assert.True(a[^1].BaseAlpha >= 0x80);
    }
}
