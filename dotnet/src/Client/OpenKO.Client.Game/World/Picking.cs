using System.Numerics;

namespace OpenKO.Client.Game.World;

/// <summary>A world-space ray (the CGameProcMain mouse pick ray).</summary>
public readonly record struct PickRay(Vector3 Origin, Vector3 Direction);

/// <summary>
/// Screen-to-world ray casting and ray/sphere intersection for click targeting
/// (the C++ CN3Base picking helpers). Left-handed like the engine camera: the
/// projected depth runs 0 (near) → 1 (far), so the near/far unprojection uses
/// those clip-space z values. Pure — no device or MonoGame types.
/// </summary>
public static class Picking
{
    /// <summary>
    /// Unprojects a screen point into a world ray through the given left-handed
    /// view and projection. Screen origin is top-left; y is flipped into clip space.
    /// </summary>
    public static PickRay ScreenPointToRay(
        Matrix4x4 view, Matrix4x4 projection, float screenX, float screenY, float viewportW, float viewportH)
    {
        float ndcX = 2f * screenX / viewportW - 1f;
        float ndcY = 1f - 2f * screenY / viewportH;

        Matrix4x4.Invert(view * projection, out Matrix4x4 invViewProj);
        Vector3 near = UnprojectPoint(new Vector3(ndcX, ndcY, 0f), invViewProj);
        Vector3 far = UnprojectPoint(new Vector3(ndcX, ndcY, 1f), invViewProj);

        Vector3 dir = far - near;
        float len = dir.Length();
        return new PickRay(near, len > 0f ? dir / len : new Vector3(0f, 0f, 1f));
    }

    /// <summary>
    /// The forward distance to the first intersection of the ray with the sphere,
    /// or null when it misses (or only intersects behind the origin).
    /// </summary>
    public static float? RaySphere(PickRay ray, Vector3 center, float radius)
    {
        Vector3 m = ray.Origin - center;
        float b = Vector3.Dot(m, ray.Direction);
        float c = Vector3.Dot(m, m) - radius * radius;

        // Origin outside the sphere and pointing away → miss.
        if (c > 0f && b > 0f)
            return null;

        float discriminant = b * b - c;
        if (discriminant < 0f)
            return null;

        float t = -b - MathF.Sqrt(discriminant);
        return t < 0f ? 0f : t; // origin inside the sphere → hit at 0
    }

    private static Vector3 UnprojectPoint(Vector3 clip, Matrix4x4 invViewProj)
    {
        Vector4 p = Vector4.Transform(new Vector4(clip, 1f), invViewProj);
        if (MathF.Abs(p.W) > 1e-8f)
            p /= p.W;
        return new Vector3(p.X, p.Y, p.Z);
    }
}

/// <summary>
/// Picks the nearest region entity (player or NPC) under a screen point — the
/// CGameProcMain::PickUP / PickUPC click-target flow. Entities are approximated
/// by a body-height bounding sphere centred half the sphere's height above the
/// ground position, matching how the C++ picks against the character bounds.
/// </summary>
public static class WorldPicker
{
    /// <summary>Body bounding radius (metres) used for the pick sphere.</summary>
    public const float BodyRadius = 1.0f;

    /// <summary>The id of a picked entity, tagged player vs NPC.</summary>
    public readonly record struct Pick(short Id, bool IsNpc, float Distance);

    public static Pick? PickNearest(PickRay ray, WorldEntities world, float radius = BodyRadius)
    {
        Pick? best = null;

        foreach ((short id, RemotePlayer p) in world.Players)
        {
            if (p.IsDead)
                continue;
            float? t = Picking.RaySphere(ray, Center(p.X, p.Y, p.Z, radius), radius);
            if (t is { } d && (best is null || d < best.Value.Distance))
                best = new Pick(id, IsNpc: false, d);
        }

        foreach ((short id, NpcEntity n) in world.Npcs)
        {
            if (n.IsDead)
                continue;
            float? t = Picking.RaySphere(ray, Center(n.X, n.Y, n.Z, radius), radius);
            if (t is { } d && (best is null || d < best.Value.Distance))
                best = new Pick(id, IsNpc: true, d);
        }

        return best;
    }

    private static Vector3 Center(float x, float y, float z, float radius) =>
        new(x, y + radius, z); // lift the sphere onto the body from the ground point
}
