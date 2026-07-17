using System.Numerics;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// The small quaternion/vector helpers the particle emitter uses to align a
/// spray direction with the bundle/emitter/particle axes
/// (N3FXPartParticles::RotateQuaternion and the <c>vDir *= RotMtx</c> chains).
/// Kept bit-faithful to the C++, including the degenerate anti-parallel case
/// where <c>__Quaternion::RotationAxis</c> with a zero axis yields
/// <c>w = cos(angle/2)</c> and the caller negates the vector.
/// </summary>
public static class FxMath
{
    /// <summary>
    /// Builds the transform that rotates <paramref name="src"/> onto
    /// <paramref name="dest"/>, reproducing the N3 idiom:
    /// <code>
    /// if (RotateQuaternion(src, dest, &amp;Qt)) v *= (Matrix)Qt;
    /// else if (Qt.w != 1.0f)                    v *= -1.0f;
    /// </code>
    /// The returned matrix applied with <see cref="Vector3.Transform(Vector3, Matrix4x4)"/>
    /// produces exactly that vector: a real rotation when the axis is non-zero, a
    /// negation for anti-parallel inputs, or identity for parallel inputs.
    /// </summary>
    public static Matrix4x4 RotateBetween(Vector3 src, Vector3 dest)
    {
        src = SafeNormalize(src);
        dest = SafeNormalize(dest);

        Vector3 axis = Vector3.Cross(src, dest);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(src, dest), -1f, 1f));

        if (axis.X == 0f && axis.Y == 0f && axis.Z == 0f)
        {
            // RotateQuaternion returned false. The C++ then checks Qt.w != 1:
            // Qt = RotationAxis(0, angle) => w = cos(angle/2). Parallel dirs give
            // angle 0 (w == 1, identity); anti-parallel give angle PI (w == 0),
            // which triggers the v *= -1 flip.
            float w = MathF.Cos(angle * 0.5f);
            return w != 1.0f ? NegateMatrix : Matrix4x4.Identity;
        }

        return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
    }

    /// <summary>A pure rotation about Z (N3 <c>__Matrix44::RotationZ</c>).</summary>
    public static Matrix4x4 RotationZ(float radians) => Matrix4x4.CreateRotationZ(radians);

    /// <summary>Axis-angle rotation matrix (N3 <c>__Quaternion::RotationAxis</c> → matrix).</summary>
    public static Matrix4x4 RotationAxis(Vector3 axis, float angle)
    {
        if (axis.X == 0f && axis.Y == 0f && axis.Z == 0f)
            return Matrix4x4.Identity;
        return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
    }

    /// <summary>Normalize, returning the input unchanged when its length is zero.</summary>
    public static Vector3 SafeNormalize(Vector3 v)
    {
        float len = v.Length();
        return len > 0f ? v / len : v;
    }

    /// <summary>The matrix whose transform negates a vector (v *= -1).</summary>
    private static readonly Matrix4x4 NegateMatrix = new(
        -1f, 0f, 0f, 0f,
        0f, -1f, 0f, 0f,
        0f, 0f, -1f, 0f,
        0f, 0f, 0f, 1f);
}
