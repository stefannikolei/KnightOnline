using OpenKO.Client.Engine.Terrain;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.8 pins: the pure river index/wave math (CN3River).</summary>
public class RiverTests
{
    [Fact]
    public void BuildIndices_ExpandsTheStencilPerFourVertexBlock()
    {
        short[] one = RiverVertexBuilder.BuildIndices(18);
        Assert.Equal(RiverVertexBuilder.Stencil.Select(v => (short)v), one);

        short[] two = RiverVertexBuilder.BuildIndices(36);
        Assert.Equal(36, two.Length);
        // Second block is the stencil shifted by +4.
        for (int j = 0; j < 18; j++)
            Assert.Equal((short)(RiverVertexBuilder.Stencil[j] + 4), two[18 + j]);
    }

    [Fact]
    public void BuildWaveDiff_AlternatesWeightAndStepsEveryFourth()
    {
        RiverWaveDiff[] diff = RiverVertexBuilder.BuildWaveDiff(8);

        // Weight alternates ±1 per vertex.
        Assert.Equal(1.0f, diff[0].Weight);
        Assert.Equal(-1.0f, diff[1].Weight);
        Assert.Equal(1.0f, diff[2].Weight);

        // Diff is assigned before the every-fourth step, so it lags one block:
        // 0, then 0.002 for l=1..4, then 0.004 for l=5..7.
        Assert.Equal(0.000f, diff[0].Diff, 5);
        Assert.Equal(0.002f, diff[1].Diff, 5);
        Assert.Equal(0.002f, diff[3].Diff, 5);
        Assert.Equal(0.002f, diff[4].Diff, 5);
        Assert.Equal(0.004f, diff[7].Diff, 5);
    }

    [Fact]
    public void StepWave_SkipsCornerVerticesAndOscillates()
    {
        RiverWaveDiff[] diff = RiverVertexBuilder.BuildWaveDiff(8);
        var yDelta = new float[8];

        RiverVertexBuilder.StepWave(diff, yDelta);

        // j%4 == 0 or 3 are left untouched (delta 0).
        Assert.Equal(0f, yDelta[0]);
        Assert.Equal(0f, yDelta[3]);
        Assert.Equal(0f, yDelta[4]);
        Assert.Equal(0f, yDelta[7]);

        // j%4 == 1: diff 0.002 + 0.001*(-1) = 0.001.
        Assert.Equal(0.001f, yDelta[1], 5);
        // j%4 == 2: diff 0.002 + 0.001*(+1) = 0.003.
        Assert.Equal(0.003f, yDelta[2], 5);
    }

    [Fact]
    public void StepWave_BouncesAtWaveTop()
    {
        // Drive one oscillator well past +WAVE_TOP and confirm the weight flips.
        var diff = new RiverWaveDiff[4];
        diff[1] = new RiverWaveDiff { Diff = RiverVertexBuilder.WaveTop, Weight = 1.0f };
        var yDelta = new float[4];

        RiverVertexBuilder.StepWave(diff, yDelta);

        // Diff exceeded WaveTop → weight must have flipped to -1.
        Assert.Equal(-1.0f, diff[1].Weight);
        Assert.True(diff[1].Diff > RiverVertexBuilder.WaveTop);
    }
}
