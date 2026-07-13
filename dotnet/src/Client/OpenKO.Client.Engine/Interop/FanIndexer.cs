namespace OpenKO.Client.Engine.Interop;

/// <summary>
/// MonoGame has no D3DPT_TRIANGLEFAN. The C++ draws terrain tiles, sky fans
/// and UI quads as fans; this generates the equivalent triangle-list indices
/// (0, i, i+1) so vertex data can stay byte-identical.
/// </summary>
public static class FanIndexer
{
    /// <summary>Indices for one fan of <paramref name="vertexCount"/> vertices.</summary>
    public static short[] Build(int vertexCount)
    {
        if (vertexCount < 3)
            return [];

        var indices = new short[(vertexCount - 2) * 3];
        for (int i = 0; i < vertexCount - 2; i++)
        {
            indices[i * 3] = 0;
            indices[i * 3 + 1] = (short)(i + 1);
            indices[i * 3 + 2] = (short)(i + 2);
        }

        return indices;
    }

    /// <summary>
    /// Appends fan indices for a fan whose vertices start at
    /// <paramref name="baseVertex"/> (for batching many quads/fans).
    /// </summary>
    public static void Append(List<short> indices, int baseVertex, int vertexCount)
    {
        for (int i = 0; i < vertexCount - 2; i++)
        {
            indices.Add((short)baseVertex);
            indices.Add((short)(baseVertex + i + 1));
            indices.Add((short)(baseVertex + i + 2));
        }
    }
}
