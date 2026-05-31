namespace OpenKO.Numerics;

/// <summary>Port of the C++ <c>__Vector3</c> (MathUtils/Vector3).</summary>
public struct Vector3
{
    public float X;
    public float Y;
    public float Z;

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public void Normalize()
    {
        float n = MathF.Sqrt(X * X + Y * Y + Z * Z);
        if (n == 0)
            return;

        X /= n;
        Y /= n;
        Z /= n;
    }

    /// <summary>Sets this vector to the normalized form of <paramref name="vec"/>.</summary>
    public void Normalize(Vector3 vec)
    {
        float n = MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);
        if (n == 0)
        {
            X = Y = Z = 0;
            return;
        }

        X = vec.X / n;
        Y = vec.Y / n;
        Z = vec.Z / n;
    }

    public readonly float Magnitude() => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public readonly float Dot(Vector3 vec) => X * vec.X + Y * vec.Y + Z * vec.Z;

    /// <summary>Sets this vector to the cross product of <paramref name="v1"/> and <paramref name="v2"/>.</summary>
    public void Cross(Vector3 v1, Vector3 v2)
    {
        X = v1.Y * v2.Z - v1.Z * v2.Y;
        Y = v1.Z * v2.X - v1.X * v2.Z;
        Z = v1.X * v2.Y - v1.Y * v2.X;
    }

    public void Absolute()
    {
        if (X < 0) X = -X;
        if (Y < 0) Y = -Y;
        if (Z < 0) Z = -Z;
    }

    public void Zero() => X = Y = Z = 0;

    public void Set(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Vector * matrix (treats the vector as a point with implicit w = 1).</summary>
    public static Vector3 operator *(Vector3 v, in Matrix44 mtx) => new(
        v.X * mtx[0, 0] + v.Y * mtx[1, 0] + v.Z * mtx[2, 0] + mtx[3, 0],
        v.X * mtx[0, 1] + v.Y * mtx[1, 1] + v.Z * mtx[2, 1] + mtx[3, 1],
        v.X * mtx[0, 2] + v.Y * mtx[1, 2] + v.Z * mtx[2, 2] + mtx[3, 2]);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator *(Vector3 a, Vector3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vector3 operator /(Vector3 a, Vector3 b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

    public static Vector3 operator +(Vector3 v, float f) => new(v.X + f, v.Y + f, v.Z + f);
    public static Vector3 operator -(Vector3 v, float f) => new(v.X - f, v.Y - f, v.Z - f);
    public static Vector3 operator *(Vector3 v, float f) => new(v.X * f, v.Y * f, v.Z * f);
    public static Vector3 operator /(Vector3 v, float f) => new(v.X / f, v.Y / f, v.Z / f);

    public static bool operator ==(Vector3 a, Vector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

    public readonly override bool Equals(object? obj) => obj is Vector3 v && this == v;
    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public readonly override string ToString() => $"({X}, {Y}, {Z})";
}
