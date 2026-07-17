using OpenKO.Client.Engine.Audio;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Slice-9.11d pins: the pure town/battle BGM choice ported from
/// <c>CGameProcMain</c> (nation × battle → the shipped ID_SOUND_BGM_* track).
/// </summary>
public class BgmSelectorTests
{
    [Fact]
    public void OutOfBattle_AnyNation_PicksTheTownTheme()
    {
        foreach (BgmNation nation in new[] { BgmNation.None, BgmNation.Karus, BgmNation.ElMorad })
        {
            BgmTrack track = BgmSelector.Select(nation, battle: false);
            Assert.Equal(BgmSelector.TownId, track.Id);
            Assert.Equal(BgmSelector.TownName, track.Name);
        }
    }

    [Fact]
    public void InBattle_Karus_PicksTheKarusBattleTheme()
    {
        BgmTrack track = BgmSelector.Select(BgmNation.Karus, battle: true);
        Assert.Equal(BgmSelector.KarusBattleId, track.Id);
        Assert.Equal(BgmSelector.KarusBattleName, track.Name);
        Assert.Equal(20002, track.Id);
    }

    [Fact]
    public void InBattle_ElMorad_PicksTheElMoradBattleTheme()
    {
        BgmTrack track = BgmSelector.Select(BgmNation.ElMorad, battle: true);
        Assert.Equal(BgmSelector.ElMoradBattleId, track.Id);
        Assert.Equal(BgmSelector.ElMoradBattleName, track.Name);
        Assert.Equal(20003, track.Id);
    }

    [Fact]
    public void InBattle_NoNation_DefaultsToElMoradBattle()
    {
        // Faithful to the C++ ternary NATION_KARUS ? KA : EL — anything not Karus
        // falls to the El Morad battle theme.
        BgmTrack track = BgmSelector.Select(BgmNation.None, battle: true);
        Assert.Equal(BgmSelector.ElMoradBattleId, track.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(1000)]
    public void Zone_DoesNotAlterTheShippedChoice(int zone)
    {
        // The shipped client does not vary the town/battle theme by zone.
        Assert.Equal(BgmSelector.Select(BgmNation.Karus, true).Id, BgmSelector.Select(BgmNation.Karus, true, zone).Id);
        Assert.Equal(BgmSelector.Select(BgmNation.ElMorad, false).Id, BgmSelector.Select(BgmNation.ElMorad, false, zone).Id);
    }

    [Fact]
    public void TrackIds_MatchTheShippedGameDefConstants()
    {
        Assert.Equal(20000, BgmSelector.TownId);
        Assert.Equal(20002, BgmSelector.KarusBattleId);
        Assert.Equal(20003, BgmSelector.ElMoradBattleId);
    }
}
