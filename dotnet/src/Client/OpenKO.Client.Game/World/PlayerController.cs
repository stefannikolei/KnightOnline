using System.Numerics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Game.World;

/// <summary>
/// The local player's movement (CGameProcMain player control): advances the
/// position along a world-space direction at the run speed, snaps to the terrain
/// surface and faces the travel direction. The camera-relative input → world
/// direction mapping lives in the client; this core is pure and testable.
/// </summary>
public sealed class PlayerController
{
    /// <summary>Run speed in metres/second (the C++ default player speed).</summary>
    public float RunSpeed { get; set; } = 8.0f;

    public Vector3 Position { get; set; }

    /// <summary>Facing yaw in radians (0 = +Z), from the travel direction.</summary>
    public float Facing { get; private set; }

    /// <summary>True while the last <see cref="MoveBy"/> produced motion.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>
    /// Moves along <paramref name="worldDirection"/> (need not be normalised) for
    /// <paramref name="deltaSeconds"/>, clamping Y to the terrain surface when in
    /// range. Returns true if the player moved.
    /// </summary>
    public bool MoveBy(Vector3 worldDirection, float deltaSeconds, N3Terrain? terrain)
    {
        worldDirection.Y = 0f;
        if (worldDirection.LengthSquared() < 1e-6f || deltaSeconds <= 0f)
        {
            IsMoving = false;
            return false;
        }

        Vector3 dir = Vector3.Normalize(worldDirection);
        Vector3 next = Position + dir * RunSpeed * deltaSeconds;

        if (terrain != null)
        {
            float h = TerrainCollision.GetHeight(terrain, next.X, next.Z);
            if (h > TerrainCollision.OutOfRange + 1f)
                next.Y = h;
        }

        Position = next;
        Facing = MathF.Atan2(dir.X, dir.Z);
        IsMoving = true;
        return true;
    }
}
