using System.Numerics;

namespace OpenKO.GameData.Math;

/// <summary>
/// Port of the __Quaternion helpers (MathUtils/Quaternion.inl) the animation
/// system samples with. NOT the System.Numerics implementations — the C++
/// formulas are kept verbatim (incl. the 0.001 small-angle cutoff and the
/// missing normalization) so keyframe interpolation matches bit-for-bit
/// within float math.
/// </summary>
public static class KoQuaternion
{
    /// <summary>__Quaternion::Slerp.</summary>
    public static Quaternion Slerp(in Quaternion q1, in Quaternion q2, float delta)
    {
        float temp = 1.0f - delta;
        float dot = q1.X * q2.X + q1.Y * q2.Y + q1.Z * q2.Z + q1.W * q2.W;

        if (dot < 0.0f)
        {
            delta = -delta;
            dot = -dot;
        }

        if (1.0f - dot > 0.001f)
        {
            float theta = MathF.Acos(dot);

            temp = MathF.Sin(theta * temp) / MathF.Sin(theta);
            delta = MathF.Sin(theta * delta) / MathF.Sin(theta);
        }

        return new Quaternion(
            temp * q1.X + delta * q2.X,
            temp * q1.Y + delta * q2.Y,
            temp * q1.Z + delta * q2.Z,
            temp * q1.W + delta * q2.W);
    }

    /// <summary>__Quaternion::RotationYawPitchRoll.</summary>
    public static Quaternion RotationYawPitchRoll(float yaw, float pitch, float roll)
    {
        float syaw = MathF.Sin(yaw / 2.0f);
        float cyaw = MathF.Cos(yaw / 2.0f);
        float spitch = MathF.Sin(pitch / 2.0f);
        float cpitch = MathF.Cos(pitch / 2.0f);
        float sroll = MathF.Sin(roll / 2.0f);
        float croll = MathF.Cos(roll / 2.0f);

        return new Quaternion(
            syaw * cpitch * sroll + cyaw * spitch * croll,
            syaw * cpitch * croll - cyaw * spitch * sroll,
            cyaw * cpitch * sroll - syaw * spitch * croll,
            cyaw * cpitch * croll + syaw * spitch * sroll);
    }

    /// <summary>__Quaternion::operator* (D3D order: out = q * this in the C++ layout).</summary>
    public static Quaternion Multiply(in Quaternion a, in Quaternion b)
    {
        return new Quaternion(
            b.W * a.X + b.X * a.W + b.Y * a.Z - b.Z * a.Y,
            b.W * a.Y - b.X * a.Z + b.Y * a.W + b.Z * a.X,
            b.W * a.Z + b.X * a.Y - b.Y * a.X + b.Z * a.W,
            b.W * a.W - b.X * a.X - b.Y * a.Y - b.Z * a.Z);
    }
}
