using System.Numerics;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Pure ports of the CN3SPart RF_BOARD_Y / RF_WINDY math (N3Shape.cpp
/// Tick, lines 109-171) — kept verbatim including the atan-based yaw split
/// and the parent-rotation compensation via axis/angle.
/// </summary>
public static class BillboardMath
{
    /// <summary>
    /// The RF_BOARD_Y part matrix: yaw toward the camera, parent rotation
    /// compensated, then the parent transform with the pivot position.
    /// </summary>
    public static Matrix4x4 BoardY(
        Vector3 pivot, in Matrix4x4 parentMatrix, Quaternion parentRotation, Vector3 cameraEye)
    {
        Vector3 pos = Vector3.Transform(pivot, parentMatrix);
        Vector3 dir = cameraEye - pos;

        float yaw = dir.X > 0f
            ? -MathF.Atan(dir.Z / dir.X) - MathF.PI * 0.5f
            : -MathF.Atan(dir.Z / dir.X) + MathF.PI * 0.5f;
        Matrix4x4 m = Matrix4x4.CreateRotationY(yaw);

        // qRot.AxisAngle: axis = (x,y,z) raw, angle = 2*acos(w); rotate back.
        float angle = 2f * MathF.Acos(Math.Clamp(parentRotation.W, -1f, 1f));
        if (angle != 0f)
        {
            var axis = new Vector3(parentRotation.X, parentRotation.Y, parentRotation.Z);
            if (axis.LengthSquared() > 0f)
            {
                Quaternion inverse = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), -angle);
                m *= Matrix4x4.CreateFromQuaternion(inverse);
            }
        }

        m *= parentMatrix;
        m.Translation = pos;
        return m;
    }

    /// <summary>__Matrix44::Rotation(x,y,z) — the RotX·RotY·RotZ composition.</summary>
    public static Matrix4x4 RotationXyz(Vector3 angles)
        => Matrix4x4.CreateRotationX(angles.X)
         * Matrix4x4.CreateRotationY(angles.Y)
         * Matrix4x4.CreateRotationZ(angles.Z);
}

/// <summary>
/// The RF_WINDY state machine (per part): a random target wind factor is
/// picked every few seconds and the current factor eases toward it; while
/// easing, the part gets a small XYZ rotation scaled by the factor.
/// </summary>
public sealed class WindyState(Random random)
{
    private float _timeToSetWind;
    private float _factorToReach;

    public float FactorCur { get; private set; }

    /// <summary>
    /// One tick; returns the new part matrix, or null when the matrix stays
    /// untouched this frame (factor already reached — C++ behavior).
    /// </summary>
    public Matrix4x4? Tick(float secPerFrame, Vector3 pivot, in Matrix4x4 parentMatrix)
    {
        _timeToSetWind -= secPerFrame;
        if (_timeToSetWind <= 0f)
        {
            _factorToReach = random.Next(100) / 100f;
            _timeToSetWind = 3f * (random.Next(100) / 100f);
            return null;
        }

        if (_factorToReach == FactorCur)
            return null;

        float factor = secPerFrame * MathF.Abs(_factorToReach - FactorCur);
        if (FactorCur < _factorToReach)
            FactorCur += factor;
        if (FactorCur > _factorToReach)
            FactorCur -= factor;
        if (MathF.Abs(_factorToReach - FactorCur) < factor)
            FactorCur = _factorToReach;

        Vector3 pos = Vector3.Transform(pivot, parentMatrix);
        Matrix4x4 m = BillboardMath.RotationXyz(new Vector3(0.05f, 0.02f, 0.05f) * FactorCur);
        m *= parentMatrix;
        m.Translation = pos;
        return m;
    }
}
