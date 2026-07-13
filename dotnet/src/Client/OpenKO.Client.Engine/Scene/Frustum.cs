using System.Numerics;

namespace OpenKO.Client.Engine.Scene;

/// <summary>
/// The six view-frustum planes, extracted from a row-vector view·projection
/// matrix (Gribb/Hartmann, D3D 0..1 depth) — the same data __CameraData
/// keeps in fFrustum. Plane (Normal, D) with distance = dot(N, p) + D;
/// a point is inside when distance ≥ 0 for all planes.
/// </summary>
public sealed class Frustum
{
    private readonly Plane[] _planes = new Plane[6];

    public IReadOnlyList<Plane> Planes => _planes;

    public static Frustum FromViewProjection(in Matrix4x4 m)
    {
        var f = new Frustum();
        // Row-vector convention: plane coefficients from matrix COLUMNS.
        f._planes[0] = Plane.Normalize(new Plane(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)); // left
        f._planes[1] = Plane.Normalize(new Plane(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)); // right
        f._planes[2] = Plane.Normalize(new Plane(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)); // bottom
        f._planes[3] = Plane.Normalize(new Plane(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)); // top
        f._planes[4] = Plane.Normalize(new Plane(m.M13, m.M23, m.M33, m.M43));                                 // near (z >= 0)
        f._planes[5] = Plane.Normalize(new Plane(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43)); // far
        return f;
    }

    /// <summary>
    /// __CameraData::IsOutOfFrustum semantics: true when the sphere lies
    /// completely outside any plane.
    /// </summary>
    public bool IsOutOfFrustum(Vector3 center, float radius)
    {
        foreach (ref readonly Plane plane in _planes.AsSpan())
        {
            if (Plane.DotCoordinate(plane, center) < -radius)
                return true;
        }

        return false;
    }
}
