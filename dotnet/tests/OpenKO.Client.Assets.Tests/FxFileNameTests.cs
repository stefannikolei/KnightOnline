using OpenKO.Client.Assets.Effects;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Slice-10.4 pins: the <c>.fxb</c> filename normalization
/// (CN3FXMgr::TriggerBundle's <c>_strlwr(szFN)</c> + missing-extension handling)
/// and the FXID → normalized-name + sound resolution the bundle loader performs.
/// </summary>
public class FxFileNameTests
{
    [Theory]
    [InlineData("fx\\Fire_target0_1.fxb", "fx\\fire_target0_1.fxb")] // lower-case
    [InlineData("  fx\\Snow.FXB  ", "fx\\snow.fxb")]                  // trim + lower-case
    [InlineData("fx\\classchange", "fx\\classchange.fxb")]           // append missing extension
    [InlineData("Bare", "bare.fxb")]                                  // no dir, no extension
    [InlineData("fx/Mixed.Path/effect", "fx/mixed.path/effect.fxb")]  // dot in a dir segment is not an extension
    public void Normalize_TrimsLowercasesAndAppendsExtension(string raw, string expected) =>
        Assert.Equal(expected, FxFileName.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyInputYieldsEmpty(string? raw) =>
        Assert.Equal(string.Empty, FxFileName.Normalize(raw));

    [Fact]
    public void FxSourceTable_ResolvesKnownIdToNormalizedFileNameAndSound()
    {
        object[] fire = [101u, "", "fx\\Fire_target0_1.FXB ", 5150u, (byte)0];
        object[] noExt = [102u, "", "fx\\classchange", 42u, (byte)1];
        var table = new FxSourceTable(TblFixture.Build(
            [TblType.Dword, TblType.String, TblType.String, TblType.Int, TblType.Byte], [fire, noExt]));

        Assert.True(table.TryGet(101, out FxSourceRow f));
        Assert.Equal("fx\\fire_target0_1.fxb", FxFileName.Normalize(f.FileName));
        Assert.Equal(5150u, f.SoundId);

        Assert.True(table.TryGet(102, out FxSourceRow c));
        Assert.Equal("fx\\classchange.fxb", FxFileName.Normalize(c.FileName));
        Assert.Equal(42u, c.SoundId);

        // Unknown FXID → false (the trigger is a no-op).
        Assert.False(table.TryGet(999, out FxSourceRow _));
    }
}
