using OpenKO.Client.Assets;

namespace OpenKO.Client.Game.World;

/// <summary>
/// Client-side terrain height query — a verbatim port of
/// <c>CN3Terrain::GetHeight</c> (Client/WarFare/N3Terrain.cpp): the tile the
/// point falls in is split into two triangles by the <c>(ix+iz)%2</c> parity
/// (a "/" or "\" diagonal), and the height is barycentrically interpolated
/// within the containing triangle. Returns <see cref="OutOfRange"/> outside the
/// map, exactly like the C++ (-FLT_MAX).
/// </summary>
public static class TerrainCollision
{
    private const float TileSize = 4.0f; // TILE_SIZE

    /// <summary>The C++ -FLT_MAX sentinel for an out-of-range query.</summary>
    public const float OutOfRange = -float.MaxValue;

    public static float GetHeight(N3Terrain terrain, float x, float z)
    {
        int mapSize = terrain.MapSize;
        int ix = (int)x / (int)TileSize;
        int iz = (int)z / (int)TileSize;

        if (ix < 0 || ix > mapSize - 2)
            return OutOfRange;
        if (iz < 0 || iz > mapSize - 2)
            return OutOfRange;

        float dX = (x - (ix * TileSize)) / TileSize;
        float dZ = (z - (iz * TileSize)) / TileSize;

        float H(int cx, int cz) => terrain.MapData[cx * mapSize + cz].Height;

        float h1, h2, h3, h12, h13;

        if ((ix + iz) % 2 == 0) // "/" diagonal
        {
            h1 = H(ix, iz);
            h3 = H(ix + 1, iz + 1);
            if (dZ > dX) // upper triangle
            {
                h2 = H(ix, iz + 1);
                h12 = h1 + (h2 - h1) * dZ;
                h13 = h1 + (h3 - h1) * dZ;
                return h12 + ((h13 - h12) * (dX / dZ));
            }
            else // lower triangle
            {
                if (dX == 0.0f)
                    return h1;
                h2 = H(ix + 1, iz);
                h12 = h1 + (h2 - h1) * dX;
                h13 = h1 + (h3 - h1) * dX;
                return h12 + ((h13 - h12) * (dZ / dX));
            }
        }

        // "\" diagonal (odd parity)
        h1 = H(ix + 1, iz);
        h3 = H(ix, iz + 1);
        if (dX + dZ > 1.0f) // upper triangle
        {
            if (dZ == 0.0f)
                return h1;
            h2 = H(ix + 1, iz + 1);
            h12 = h1 + (h2 - h1) * dZ;
            h13 = h1 + (h3 - h1) * dZ;
            return h12 + ((h13 - h12) * ((1.0f - dX) / dZ));
        }
        else // lower triangle
        {
            if (dX == 1.0f)
                return h1;
            h2 = H(ix, iz);
            h12 = h2 + (h1 - h2) * dX;
            h13 = h3 + (h1 - h3) * dX;
            return h12 + ((h13 - h12) * (dZ / (1.0f - dX)));
        }
    }
}
