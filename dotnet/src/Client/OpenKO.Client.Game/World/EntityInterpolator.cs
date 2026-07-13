using System.Numerics;

namespace OpenKO.Client.Game.World;

/// <summary>
/// Smooths the visual position of remote entities between the authoritative
/// WIZ_MOVE targets the server streams: the render position advances toward the
/// target at the move speed, so other players/NPCs glide instead of teleporting
/// (the CGameProcMain interpolation). Pure and frame-rate independent.
/// </summary>
public static class EntityInterpolator
{
    /// <summary>
    /// Advances <paramref name="current"/> toward <paramref name="target"/> by at
    /// most <paramref name="speed"/> × <paramref name="deltaSeconds"/> metres,
    /// snapping to the target once within reach. <paramref name="arrived"/> is
    /// true when the target was reached this step.
    /// </summary>
    public static Vector3 MoveTowards(
        Vector3 current, Vector3 target, float speed, float deltaSeconds, out bool arrived)
    {
        Vector3 delta = target - current;
        float distance = delta.Length();
        float step = speed * deltaSeconds;

        if (distance <= step || distance == 0f)
        {
            arrived = true;
            return target;
        }

        arrived = false;
        return current + (delta / distance) * step;
    }
}
