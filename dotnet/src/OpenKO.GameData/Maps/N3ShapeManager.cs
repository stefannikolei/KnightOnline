using System.Numerics;
using OpenKO.GameData.Math;

namespace OpenKO.GameData.Maps;

/// <summary>
/// Port of <c>CN3ShapeMgr</c> (Server/shared-server/N3ShapeMgr.{h,cpp}):
/// server-side collision/height data loaded from the N3 map (.smd) files.
/// Grid: 16m main cells subdivided 4x4 into 4m sub cells, up to 4096m maps.
/// </summary>
public sealed class N3ShapeManager
{
    public const int CellMainDivide = 4;
    public const int CellSubSize = 4;
    public const int CellMainSize = CellMainDivide * CellSubSize; // 16m
    public const int MaxCellMain = 4096 / CellMainSize;           // 256
    public const int MaxCellSub = MaxCellMain * CellMainDivide;

    public sealed class CellSub
    {
        /// <summary>Collision-check polygon count.</summary>
        public int PolyCount;

        /// <summary>PolyCount * 3 vertex indices into <see cref="Collisions"/>.</summary>
        public uint[] VertexIndices = [];
    }

    public sealed class CellMain
    {
        public ushort[] ShapeIndices = [];
        public readonly CellSub[,] SubCells = new CellSub[CellMainDivide, CellMainDivide];

        public CellMain()
        {
            for (int x = 0; x < CellMainDivide; x++)
            {
                for (int z = 0; z < CellMainDivide; z++)
                    SubCells[x, z] = new CellSub();
            }
        }
    }

    private readonly CellMain?[,] _cells = new CellMain?[MaxCellMain, MaxCellMain];

    /// <summary>Collision triangle vertices (faceCount * 3).</summary>
    public Vector3[] Collisions { get; private set; } = [];

    public float MapWidth { get; private set; }

    public float MapLength { get; private set; }

    public int CollisionFaceCount { get; private set; }

    public bool Create(float mapWidth, float mapLength)
    {
        if (mapWidth <= 0.0f || mapWidth > MaxCellMain * CellMainSize
            || mapLength <= 0.0f || mapLength > MaxCellMain * CellMainSize)
            return false;

        MapWidth = mapWidth;
        MapLength = mapLength;
        return true;
    }

    public bool LoadCollisionData(BinaryReader reader)
    {
        MapWidth = reader.ReadSingle();
        MapLength = reader.ReadSingle();

        if (!Create(MapWidth, MapLength))
            return false;

        CollisionFaceCount = reader.ReadInt32();

        Collisions = new Vector3[CollisionFaceCount * 3];
        for (int i = 0; i < Collisions.Length; i++)
            Collisions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        int z = 0;
        for (float fz = 0.0f; fz < MapLength; fz += CellMainSize, z++)
        {
            int x = 0;
            for (float fx = 0.0f; fx < MapWidth; fx += CellMainSize, x++)
            {
                int exists = reader.ReadInt32();
                if (exists == 0)
                {
                    _cells[x, z] = null;
                    continue;
                }

                var cell = new CellMain();
                LoadCellMain(cell, reader);
                _cells[x, z] = cell;
            }
        }

        return true;
    }

    private static void LoadCellMain(CellMain cell, BinaryReader reader)
    {
        int shapeCount = reader.ReadInt32();
        if (shapeCount > 0)
        {
            cell.ShapeIndices = new ushort[shapeCount];
            for (int i = 0; i < shapeCount; i++)
                cell.ShapeIndices[i] = reader.ReadUInt16();
        }

        for (int z = 0; z < CellMainDivide; z++)
        {
            for (int x = 0; x < CellMainDivide; x++)
                LoadCellSub(cell.SubCells[x, z], reader);
        }
    }

    private static void LoadCellSub(CellSub cell, BinaryReader reader)
    {
        cell.PolyCount = reader.ReadInt32();
        if (cell.PolyCount > 0)
        {
            cell.VertexIndices = new uint[cell.PolyCount * 3];
            for (int i = 0; i < cell.VertexIndices.Length; i++)
                cell.VertexIndices[i] = reader.ReadUInt32();
        }
    }

    public CellSub? SubCell(float x, float z)
    {
        int cx = (int)(x / CellMainSize);
        int cz = (int)(z / CellMainSize);

        if (cx < 0 || cx >= MaxCellMain || cz < 0 || cz >= MaxCellMain)
            return null;

        CellMain? cell = _cells[cx, cz];
        if (cell is null)
            return null;

        int sx = ((int)x % CellMainSize) / CellSubSize;
        int sz = ((int)z % CellMainSize) / CellSubSize;

        return cell.SubCells[sx, sz];
    }

    /// <summary>GetHeight: highest collision surface at (x, z); float.MinValue when none.</summary>
    public float GetHeight(float x, float z, out Vector3 normal)
    {
        normal = default;

        CellSub? cell = SubCell(x, z);
        if (cell is null || cell.PolyCount <= 0)
            return float.MinValue;

        var origin = new Vector3(x, 5000.0f, z);
        var dir = new Vector3(0, -1, 0);

        float max = float.MinValue;
        for (int i = 0; i < cell.PolyCount; i++)
        {
            uint i0 = cell.VertexIndices[i * 3];
            uint i1 = cell.VertexIndices[i * 3 + 1];
            uint i2 = cell.VertexIndices[i * 3 + 2];

            if (!KoMath.IntersectTriangle(origin, dir, Collisions[i0], Collisions[i1], Collisions[i2],
                    out _, out _, out _, out Vector3 collision))
                continue;

            if (collision.Y > max)
            {
                max = collision.Y;
                normal = KoMath.Normalized(Vector3.Cross(
                    Collisions[i1] - Collisions[i0],
                    Collisions[i2] - Collisions[i0]));
            }
        }

        return max;
    }

    public float GetHeight(float x, float z) => GetHeight(x, z, out _);

    /// <summary>GetHeightNearstPos: height of the collision point closest to vPos.</summary>
    public float GetHeightNearest(in Vector3 pos, out Vector3 normal)
    {
        normal = default;

        CellSub? cell = SubCell(pos.X, pos.Z);
        if (cell is null || cell.PolyCount <= 0)
            return float.MinValue;

        var origin = pos with { Y = 5000.0f };
        var dir = new Vector3(0, -1, 0);

        float nearest = float.MaxValue;
        float height = float.MinValue;

        for (int i = 0; i < cell.PolyCount; i++)
        {
            uint i0 = cell.VertexIndices[i * 3];
            uint i1 = cell.VertexIndices[i * 3 + 1];
            uint i2 = cell.VertexIndices[i * 3 + 2];

            if (!KoMath.IntersectTriangle(origin, dir, Collisions[i0], Collisions[i1], Collisions[i2],
                    out _, out _, out _, out Vector3 collision))
                continue;

            float distance = KoMath.Magnitude(collision - pos);
            if (distance < nearest)
            {
                nearest = distance;
                height = collision.Y;
                normal = KoMath.Normalized(Vector3.Cross(
                    Collisions[i1] - Collisions[i0],
                    Collisions[i2] - Collisions[i0]));
            }
        }

        return height;
    }

    /// <summary>
    /// SubCellPathThru: all sub cells crossed by the segment (Cohen-Sutherland
    /// outcode test, replicated with the original's quirks — including the
    /// nXSub/nZSub guard that uses MAX_CELL_MAIN % CELL_MAIN_DIVIDE).
    /// </summary>
    public int SubCellPathThru(in Vector3 from, in Vector3 at, int maxSubCells, CellSub[] result)
    {
        int xx1, xx2, zz1, zz2;

        if (from.X < at.X)
        {
            xx1 = (int)(from.X / CellSubSize);
            xx2 = (int)(at.X / CellSubSize);
        }
        else
        {
            xx1 = (int)(at.X / CellSubSize);
            xx2 = (int)(from.X / CellSubSize);
        }

        if (from.Z < at.Z)
        {
            zz1 = (int)(from.Z / CellSubSize);
            zz2 = (int)(at.Z / CellSubSize);
        }
        else
        {
            zz1 = (int)(at.Z / CellSubSize);
            zz2 = (int)(from.Z / CellSubSize);
        }

        int count = 0;
        for (int z = zz1; z <= zz2; z++)
        {
            float zMin = z * CellSubSize;
            float zMax = (z + 1) * CellSubSize;

            for (int x = xx1; x <= xx2; x++)
            {
                float xMin = x * CellSubSize;
                float xMax = (x + 1) * CellSubSize;

                uint oc0 = 0, oc1 = 0;
                if (from.Z > zMax) oc0 |= 0xf000;
                if (from.Z < zMin) oc0 |= 0x0f00;
                if (from.X > xMax) oc0 |= 0x00f0;
                if (from.X < xMin) oc0 |= 0x000f;
                if (at.Z > zMax) oc1 |= 0xf000;
                if (at.Z < zMin) oc1 |= 0x0f00;
                if (at.X > xMax) oc1 |= 0x00f0;
                if (at.X < xMin) oc1 |= 0x000f;

                bool pathThru;
                if ((oc0 & oc1) != 0)
                {
                    pathThru = false;
                }
                else if (oc0 == 0 && oc1 == 0)
                {
                    pathThru = true;
                }
                else if (oc0 == 0 || oc1 == 0)
                {
                    pathThru = true;
                }
                else
                {
                    // Both outside but possibly crossing: intersect with the top edge.
                    float xCross = from.X + (zMax - from.Z) * (at.X - from.X) / (at.Z - from.Z);
                    pathThru = xCross >= xMin;
                }

                if (!pathThru)
                    continue;

                int mainX = x / CellMainDivide;
                int mainZ = z / CellMainDivide;

                if (mainX < 0 || mainX >= MaxCellMain || mainZ < 0 || mainZ >= MaxCellMain)
                    continue;

                CellMain? cell = _cells[mainX, mainZ];
                if (cell is null)
                    continue;

                int subX = x % CellMainDivide;
                int subZ = z % CellMainDivide;

                // Faithful to the C++ guard (MAX_CELL_MAIN % CELL_MAIN_DIVIDE == 0,
                // so this rejects everything except subX/subZ == 0 — see the NOTE
                // in the original about negative coordinates rounding to zero).
                if (subX < 0 || subX >= MaxCellMain % CellMainDivide
                    || subZ < 0 || subZ >= MaxCellMain % CellMainDivide)
                    continue;

                result[count++] = cell.SubCells[subX, subZ];
                if (count >= maxSubCells)
                    return maxSubCells;
            }
        }

        return count;
    }

    /// <summary>CheckCollision: does a move from pos along dir at speed hit collision geometry?</summary>
    public bool CheckCollision(
        in Vector3 pos, in Vector3 dir, float speedPerSec,
        out Vector3 collisionPoint, out Vector3 collisionNormal)
    {
        collisionPoint = default;
        collisionNormal = default;

        if (speedPerSec <= 0)
            return false;

        var cells = new CellSub[128];
        Vector3 posNext = pos + dir * speedPerSec;

        int cellCount = speedPerSec < 4.0f
            ? SubCellPathThru(pos, pos + dir * 4.0f, 128, cells)
            : SubCellPathThru(pos, posNext, 128, cells);

        if (cellCount <= 0)
            return false;

        float closest = float.MaxValue;
        for (int i = 0; i < cellCount; i++)
        {
            CellSub cell = cells[i];
            if (cell.PolyCount <= 0)
                continue;

            for (int j = 0; j < cell.PolyCount; j++)
            {
                uint i0 = cell.VertexIndices[j * 3];
                uint i1 = cell.VertexIndices[j * 3 + 1];
                uint i2 = cell.VertexIndices[j * 3 + 2];

                if (!KoMath.IntersectTriangle(pos, dir, Collisions[i0], Collisions[i1], Collisions[i2],
                        out _, out _, out _, out Vector3 colTmp))
                    continue;

                // Passed fully through already? (same double-check as the C++)
                if (KoMath.IntersectTriangle(posNext, dir, Collisions[i0], Collisions[i1], Collisions[i2]))
                    continue;

                float distance = KoMath.Magnitude(pos - colTmp);
                if (distance < closest)
                {
                    closest = distance;
                    collisionPoint = colTmp;
                    collisionNormal = KoMath.Normalized(Vector3.Cross(
                        Collisions[i1] - Collisions[i0],
                        Collisions[i2] - Collisions[i0]));
                }
            }
        }

        return closest != float.MaxValue;
    }

    public bool CheckCollision(in Vector3 pos, in Vector3 dir, float speedPerSec)
        => CheckCollision(pos, dir, speedPerSec, out _, out _);
}
