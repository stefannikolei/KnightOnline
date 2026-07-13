namespace OpenKO.Client.Engine.Terrain;

/// <summary>Per-vertex wave oscillator state (CN3River::_RIVER_DIFF).</summary>
public struct RiverWaveDiff
{
    public float Diff;
    public float Weight;
}

/// <summary>
/// Pure port of the CPU-side river setup in <c>CN3River::Load</c>/<c>Tick</c>
/// (Client/WarFare/N3River.cpp): the strip index expansion from the fixed
/// <c>wIndex[18]</c> stencil and the per-vertex wave oscillator table, plus the
/// <c>UpdateWaterPositions</c> step. The device <see cref="RiverRenderer"/>
/// consumes these; keeping them here makes the wave math headless-testable.
/// </summary>
public static class RiverVertexBuilder
{
    public const float WaveTop = 0.02f;   // WAVE_TOP
    public const float WaveStep = 0.001f; // WAVE_STEP

    /// <summary>The 18-index stencil for one row of four river vertices.</summary>
    public static readonly ushort[] Stencil = [4, 0, 1, 4, 1, 5, 5, 1, 2, 5, 2, 6, 6, 2, 3, 6, 3, 7];

    /// <summary>
    /// Expands the stencil into the full index list: block <c>l</c> uses
    /// <c>wIndex[j] + l*4</c> (N3River.cpp:72-78). <paramref name="indexCount"/>
    /// is the stored iIC (a multiple of 18).
    /// </summary>
    public static short[] BuildIndices(int indexCount)
    {
        var indices = new short[indexCount];
        int blocks = indexCount / 18;
        for (int l = 0; l < blocks; l++)
            for (int j = 0; j < 18; j++)
                indices[l * 18 + j] = (short)(Stencil[j] + l * 4);
        return indices;
    }

    /// <summary>
    /// The initial wave-diff table (N3River.cpp:81-99): fWeight alternates
    /// ±1 per vertex; fDiff steps by ±0.002 every fourth vertex, bouncing at
    /// ±WAVE_TOP.
    /// </summary>
    public static RiverWaveDiff[] BuildWaveDiff(int vertexCount)
    {
        var diff = new RiverWaveDiff[vertexCount];
        float add = 0f;
        float mul = 0.002f;
        for (int l = 0; l < vertexCount; l++)
        {
            diff[l].Diff = add;
            diff[l].Weight = l % 2 == 0 ? 1.0f : -1.0f;
            if (l % 4 == 0)
            {
                add += mul;
                if (add > WaveTop)
                    mul = -0.002f;
                else if (add < -WaveTop)
                    mul = 0.002f;
            }
        }

        return diff;
    }

    /// <summary>
    /// One <c>UpdateWaterPositions</c> step: advances the oscillators and
    /// returns the per-vertex Y deltas to add (0 for the j%4==0/3 vertices,
    /// which the C++ skips). Mutates <paramref name="diff"/> in place, exactly
    /// like the C++ walks its pDiff pointer for every vertex.
    /// </summary>
    public static void StepWave(RiverWaveDiff[] diff, Span<float> yDelta)
    {
        for (int j = 0; j < diff.Length; j++)
        {
            int tmp = j % 4;
            if (tmp == 0 || tmp == 3)
            {
                yDelta[j] = 0f;
                continue;
            }

            diff[j].Diff += WaveStep * diff[j].Weight;
            if (diff[j].Diff > WaveTop)
                diff[j].Weight = -1.0f;
            else if (diff[j].Diff < -WaveTop)
                diff[j].Weight = 1.0f;

            yDelta[j] = diff[j].Diff;
        }
    }
}
