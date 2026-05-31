namespace OpenKO.Numerics;

/// <summary>Port of the C++ <c>__Vector4</c> (MathUtils/Vector4).</summary>
public struct Vector4
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public Vector4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public void Zero() => X = Y = Z = W = 0;

    public void Set(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Transforms a 3D point by a matrix, producing the full 4D (homogeneous) result.</summary>
    public void Transform(Vector3 v, in Matrix44 m)
    {
        X = m[0, 0] * v.X + m[1, 0] * v.Y + m[2, 0] * v.Z + m[3, 0];
        Y = m[0, 1] * v.X + m[1, 1] * v.Y + m[2, 1] * v.Z + m[3, 1];
        Z = m[0, 2] * v.X + m[1, 2] * v.Y + m[2, 2] * v.Z + m[3, 2];
        W = m[0, 3] * v.X + m[1, 3] * v.Y + m[2, 3] * v.Z + m[3, 3];
    }

    public static Vector4 operator +(Vector4 a, Vector4 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vector4 operator -(Vector4 a, Vector4 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vector4 operator *(Vector4 v, float f) => new(v.X * f, v.Y * f, v.Z * f, v.W * f);

    public static Vector4 operator /(Vector4 v, float f)
    {
        float inv = 1.0f / f;
        return new Vector4(v.X * inv, v.Y * inv, v.Z * inv, v.W * inv);
    }
}
