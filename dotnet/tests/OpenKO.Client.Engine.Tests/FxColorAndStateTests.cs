using OpenKO.Client.Engine.Fx;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Slice-9.10b pins: the pure colour/fade math and the base part state machine.</summary>
public class FxColorAndStateTests
{
    [Fact]
    public void FxRandom_IsDeterministicAndInRange()
    {
        var a = new FxRandom(0x1234u);
        var b = new FxRandom(0x1234u);
        for (int i = 0; i < 1000; i++)
        {
            int x = a.Next();
            Assert.Equal(x, b.Next());
            Assert.InRange(x, 0, FxRandom.RandMax);
        }

        // NextUnit stays in [0, 0.99].
        var c = new FxRandom(7);
        for (int i = 0; i < 1000; i++)
            Assert.InRange(c.NextUnit(), 0f, 0.99f);
    }

    [Fact]
    public void ColorKeyAt_PicksTheBucket_ClampedToLastKey()
    {
        var colors = new uint[N3KeyCount];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = (uint)i;

        // t=0 -> key 0.
        Assert.Equal(0u, FxColor.ColorKeyAt(colors, 0f, 1f, N3KeyCount));
        // mid-life -> the middle bucket (0.5 * 100 = 50).
        Assert.Equal(50u, FxColor.ColorKeyAt(colors, 0.5f, 1f, N3KeyCount));
        // just under end.
        Assert.Equal(99u, FxColor.ColorKeyAt(colors, 0.99f, 1f, N3KeyCount));
        // at/past end -> clamped to the last key.
        Assert.Equal(99u, FxColor.ColorKeyAt(colors, 1.0f, 1f, N3KeyCount));
        Assert.Equal(99u, FxColor.ColorKeyAt(colors, 5.0f, 1f, N3KeyCount));
    }

    [Fact]
    public void ParticleFade_RampsInHoldsAndRampsOut()
    {
        // fadeIn 1, life 2, fadeOut 1.
        // Halfway through fade-in: alpha ~127.
        uint mid = FxColor.ParticleFade(0.5f, 1f, 2f, 1f);
        Assert.Equal(127u, mid >> 24);
        Assert.Equal(0x00ffffffu, mid & 0x00ffffffu);

        // In the opaque plateau: full white.
        Assert.Equal(FxColor.White, FxColor.ParticleFade(2f, 1f, 2f, 1f));

        // Halfway through fade-out (currLife 3.5, total 4): alpha ~127.
        uint fo = FxColor.ParticleFade(3.5f, 1f, 2f, 1f);
        Assert.Equal(127u, fo >> 24);

        // Past the end: transparent.
        Assert.Equal(FxColor.TransparentWhite, FxColor.ParticleFade(5f, 1f, 2f, 1f));
    }

    [Fact]
    public void BoardFade_OnlyRampsOutWhenDying()
    {
        // Alive, past fade-in: opaque regardless of remaining life.
        Assert.Equal(FxColor.White, FxColor.BoardFade(3f, 1f, 2f, 1f, dying: false));
        // Dying, halfway through fade-out: alpha ~127.
        uint dying = FxColor.BoardFade(3.5f, 1f, 2f, 1f, dying: true);
        Assert.Equal(127u, dying >> 24);
        // Dying, past total: transparent.
        Assert.Equal(FxColor.TransparentWhite, FxColor.BoardFade(4f, 1f, 2f, 1f, dying: true));
    }

    [Fact]
    public void PartState_WalksReadyLiveDyingDead()
    {
        bool dead = false;
        var state = new FxPartState(life: 1f, fadeIn: 0f, isDead: () => dead);

        // READY: Tick is a no-op.
        Assert.Equal(FxPartLifeState.Ready, state.State);
        Assert.False(state.Tick(0.1f));

        state.Start();
        Assert.Equal(FxPartLifeState.Live, state.State);
        Assert.True(state.Tick(0.4f)); // currLife 0.4
        Assert.Equal(FxPartLifeState.Live, state.State);

        // Cross life (1.0) -> auto Stop -> DYING; isDead still false so it lingers.
        Assert.True(state.Tick(0.7f)); // currLife would be 1.1 -> Stop snaps to 1.0
        Assert.Equal(FxPartLifeState.Dying, state.State);

        // Now let it die.
        dead = true;
        Assert.False(state.Tick(0.1f));
        Assert.Equal(FxPartLifeState.Dead, state.State);
    }

    [Fact]
    public void PartState_Stop_SnapsClockToEndOfPlay()
    {
        var state = new FxPartState(life: 2f, fadeIn: 0.5f, isDead: () => false);
        state.Start();
        state.Tick(0.1f);
        state.Stop();
        Assert.Equal(FxPartLifeState.Dying, state.State);
        Assert.Equal(2.5f, state.CurrLife, 4); // life + fadeIn
    }

    private const int N3KeyCount = 100;
}
