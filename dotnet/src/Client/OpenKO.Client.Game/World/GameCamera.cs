using System.Numerics;

namespace OpenKO.Client.Game.World;

/// <summary>
/// The third-person game camera (CGameEng zoom + follow): orbits the followed
/// target at a clamped distance with yaw/pitch, producing the eye/at the engine
/// camera consumes. Pure math (no GraphicsDevice) so it is headless-testable.
/// </summary>
public sealed class GameCamera
{
    public const float MinDistance = 3.0f;
    public const float MaxDistance = 30.0f;
    private const float PitchLimit = 1.4f; // ~80°, avoid gimbal at straight-down

    /// <summary>The followed point (usually the player position + eye height).</summary>
    public Vector3 Target { get; set; }

    public float Distance { get; private set; } = 12.0f;

    /// <summary>Horizontal orbit angle (radians).</summary>
    public float Yaw { get; set; }

    /// <summary>Vertical orbit angle (radians), clamped away from the poles.</summary>
    public float Pitch { get; private set; } = 0.5f;

    /// <summary>Mouse-wheel zoom (positive = closer).</summary>
    public void Zoom(float delta) => Distance = Math.Clamp(Distance - delta, MinDistance, MaxDistance);

    /// <summary>Orbit the camera; pitch is clamped to stay off the poles.</summary>
    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -PitchLimit, PitchLimit);
    }

    /// <summary>The camera eye, orbiting the target on a sphere of radius <see cref="Distance"/>.</summary>
    public Vector3 Eye
    {
        get
        {
            float cosPitch = MathF.Cos(Pitch);
            return Target + new Vector3(
                Distance * cosPitch * MathF.Sin(Yaw),
                Distance * MathF.Sin(Pitch),
                Distance * cosPitch * MathF.Cos(Yaw));
        }
    }

    /// <summary>The camera look-at point (the followed target).</summary>
    public Vector3 At => Target;
}
