namespace OpenKO.Numerics;

/// <summary>Port of the C++ <c>__Vector2</c> (MathUtils/Vector2).</summary>
public struct Vector2
{
    public float X;
    public float Y;

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public void Zero() => X = Y = 0;

    public void Set(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 v, float f) => new(v.X * f, v.Y * f);

    public static Vector2 operator /(Vector2 v, float f)
    {
        float inv = 1.0f / f;
        return new Vector2(v.X * inv, v.Y * inv);
    }
}
