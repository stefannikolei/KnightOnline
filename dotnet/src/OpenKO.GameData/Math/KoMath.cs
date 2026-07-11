using System.Numerics;

namespace OpenKO.GameData.Math;

/// <summary>
/// Port of the MathUtils helpers the servers rely on. The triangle intersection
/// replicates <c>_IntersectTriangle</c> (MathUtils/MathUtils.cpp) including its
/// operation order, epsilons and the backface-culling determinant checks, so
/// collision decisions match the C++.
/// </summary>
public static class KoMath
{
    /// <summary>
    /// _IntersectTriangle: returns true when the ray (orig, dir) hits triangle
    /// (v0, v1, v2) front-face; outputs barycentric u/v, distance t and the
    /// collision point.
    /// </summary>
    public static bool IntersectTriangle(
        in Vector3 orig, in Vector3 dir,
        in Vector3 v0, in Vector3 v1, in Vector3 v2,
        out float t, out float u, out float v, out Vector3 collision)
    {
        t = u = v = 0;
        collision = default;

        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;

        // Backface check (added "By Ecli666" in the original).
        Vector3 pVec = Vector3.Cross(edge1, edge2);
        float det = Vector3.Dot(pVec, dir);
        if (det > -0.0001f)
            return false;

        pVec = Vector3.Cross(dir, edge2);

        // If determinant is near zero, ray lies in plane of triangle.
        det = Vector3.Dot(edge1, pVec);
        if (det < 0.0001f)
            return false;

        Vector3 tVec = orig - v0;

        u = Vector3.Dot(tVec, pVec);
        if (u < 0.0f || u > det)
            return false;

        Vector3 qVec = Vector3.Cross(tVec, edge1);

        v = Vector3.Dot(dir, qVec);
        if (v < 0.0f || u + v > det)
            return false;

        t = Vector3.Dot(edge2, qVec);

        float invDet = 1.0f / det;
        t *= invDet;
        u *= invDet;
        v *= invDet;

        collision = orig + dir * t;

        // Behind the ray origin.
        if (t < 0.0f)
            return false;

        return true;
    }

    public static bool IntersectTriangle(
        in Vector3 orig, in Vector3 dir,
        in Vector3 v0, in Vector3 v1, in Vector3 v2)
        => IntersectTriangle(orig, dir, v0, v1, v2, out _, out _, out _, out _);

    /// <summary>__Vector3::Magnitude.</summary>
    public static float Magnitude(in Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    /// <summary>__Vector3::Normalize (no-op on zero-length vectors, like the C++).</summary>
    public static Vector3 Normalized(in Vector3 v)
    {
        float magnitude = Magnitude(v);
        if (magnitude == 0.0f)
            return v;

        return new Vector3(v.X / magnitude, v.Y / magnitude, v.Z / magnitude);
    }
}
