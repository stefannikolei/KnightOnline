using OpenKO.Client.Engine.Sky;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-10.5 pins: the server game clock (WIZ_TIME) + sun/moon geometry.</summary>
public class SkyClockTests
{
    [Fact]
    public void GameClock_SeedsFromServerHourMinute()
    {
        var clock = new GameClock();
        Assert.False(clock.HasServerTime);

        clock.SetFromServer(12, 0);
        Assert.True(clock.HasServerTime);
        Assert.Equal(DayNightCycle.DayFraction(12, 0), clock.DayFraction, 5);
    }

    [Fact]
    public void GameClock_AdvancesAtGameSpeedAndWraps()
    {
        var clock = new GameClock();
        clock.SetFromServer(0, 0); // midnight
        Assert.Equal(0f, clock.DayFraction, 5);

        // A full game day is SecondsPerDay / TimeRealPerGame real seconds; advancing
        // exactly that must wrap back to the same fraction.
        float realDayLength = DayNightCycle.SecondsPerDay / DayNightCycle.TimeRealPerGame;
        clock.Advance(realDayLength);
        Assert.Equal(0f, clock.DayFraction, 3);

        // Half a game day → noon fraction.
        clock.Advance(realDayLength * 0.5f);
        Assert.Equal(0.5f, clock.DayFraction, 3);
    }

    [Fact]
    public void GameClock_AdvanceScalesByTimeRealPerGame()
    {
        var clock = new GameClock();
        clock.SetFromServer(0, 0);
        // One real second advances TimeRealPerGame game-seconds of the day.
        clock.Advance(1f);
        float expected = DayNightCycle.TimeRealPerGame / DayNightCycle.SecondsPerDay;
        Assert.Equal(expected, clock.DayFraction, 6);
    }

    [Theory]
    [InlineData(0, 0)]      // month 0, day 0 → phase 0
    [InlineData(1, 0, 30)]  // month*30 → 30 mod 24 = 6
    public void SkyBodies_MoonPhaseIndex_WrapsMod24(int month, int day, int rawExpected = -1)
    {
        int raw = rawExpected < 0 ? month * 30 + day : rawExpected;
        Assert.Equal(raw % 24, SkyBodies.MoonPhaseIndex(month, day));
        Assert.InRange(SkyBodies.MoonPhaseIndex(month, day), 0, 23);
    }

    [Fact]
    public void SkyBodies_MoonPhaseUv_MapsGridCells()
    {
        // Phase 0 = top-left cell (1/6 × 1/4).
        UvRect first = SkyBodies.MoonPhaseUv(0);
        Assert.Equal(0f, first.U0, 5);
        Assert.Equal(0f, first.V0, 5);
        Assert.Equal(1f / 6f, first.U1, 5);
        Assert.Equal(1f / 4f, first.V1, 5);

        // Phase 7 = row 1, col 1.
        UvRect p7 = SkyBodies.MoonPhaseUv(7);
        Assert.Equal(1f / 6f, p7.U0, 5);
        Assert.Equal(1f / 4f, p7.V0, 5);

        // Wraps mod 24.
        Assert.Equal(first, SkyBodies.MoonPhaseUv(24));
    }

    [Fact]
    public void SkyBodies_SunLayout_KeepsPartProportions()
    {
        SunPartLayout layout = SkyBodies.SunLayout(0.2f);
        // Disk scaled to the requested half-size; glow/flare keep their 0.25/0.13-to-0.1 ratios.
        Assert.Equal(0.2f, layout.DiskHalfSize, 5);
        Assert.Equal(0.2f * (SkyBodies.SunGlowDelta / SkyBodies.SunDiskDelta), layout.GlowHalfSize, 5);
        Assert.Equal(0.2f * (SkyBodies.SunFlareDelta / SkyBodies.SunDiskDelta), layout.FlareHalfSize, 5);
    }
}
