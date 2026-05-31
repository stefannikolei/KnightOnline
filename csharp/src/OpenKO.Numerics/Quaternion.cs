namespace OpenKO.Numerics;

/// <summary>Port of the C++ <c>__Quaternion</c> (MathUtils/Quaternion).</summary>
public struct Quaternion
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public Quaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public Quaternion(in Matrix44 mtx)
    {
        SetFromMatrix(mtx);
    }

    public void Identity()
    {
        X = Y = Z = 0;
        W = 1.0f;
    }

    public void Set(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public void RotationAxis(Vector3 v, float radian)
    {
        Vector3 temp = v;
        temp.Normalize();

        float s = MathF.Sin(radian / 2.0f);
        X = s * temp.X;
        Y = s * temp.Y;
        Z = s * temp.Z;
        W = MathF.Cos(radian / 2.0f);
    }

    public void RotationAxis(float x, float y, float z, float radian)
        => RotationAxis(new Vector3(x, y, z), radian);

    public void SetFromMatrix(in Matrix44 mtx)
    {
        float trace = mtx[0, 0] + mtx[1, 1] + mtx[2, 2] + 1.0f;
        if (trace > 1.0f)
        {
            float s = 2.0f * MathF.Sqrt(trace);
            X = (mtx[1, 2] - mtx[2, 1]) / s;
            Y = (mtx[2, 0] - mtx[0, 2]) / s;
            Z = (mtx[0, 1] - mtx[1, 0]) / s;
            W = 0.25f * s;
        }
        else
        {
            int maxi = 0;
            for (int i = 1; i < 3; i++)
            {
                if (mtx[i, i] > mtx[maxi, maxi])
                    maxi = i;
            }

            switch (maxi)
            {
                case 0:
                {
                    float s = 2.0f * MathF.Sqrt(1.0f + mtx[0, 0] - mtx[1, 1] - mtx[2, 2]);
                    X = 0.25f * s;
                    Y = (mtx[0, 1] + mtx[1, 0]) / s;
                    Z = (mtx[0, 2] + mtx[2, 0]) / s;
                    W = (mtx[1, 2] - mtx[2, 1]) / s;
                    break;
                }

                case 1:
                {
                    float s = 2.0f * MathF.Sqrt(1.0f + mtx[1, 1] - mtx[0, 0] - mtx[2, 2]);
                    X = (mtx[0, 1] + mtx[1, 0]) / s;
                    Y = 0.25f * s;
                    Z = (mtx[1, 2] + mtx[2, 1]) / s;
                    W = (mtx[2, 0] - mtx[0, 2]) / s;
                    break;
                }

                case 2:
                {
                    float s = 2.0f * MathF.Sqrt(1.0f + mtx[2, 2] - mtx[0, 0] - mtx[1, 1]);
                    X = (mtx[0, 2] + mtx[2, 0]) / s;
                    Y = (mtx[1, 2] + mtx[2, 1]) / s;
                    Z = 0.25f * s;
                    W = (mtx[0, 1] - mtx[1, 0]) / s;
                    break;
                }
            }
        }
    }

    public readonly void AxisAngle(out Vector3 axis, out float radian)
    {
        axis = new Vector3(X, Y, Z);
        radian = 2.0f * MathF.Acos(W);
    }

    public void Slerp(Quaternion q1, Quaternion q2, float delta)
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

        X = temp * q1.X + delta * q2.X;
        Y = temp * q1.Y + delta * q2.Y;
        Z = temp * q1.Z + delta * q2.Z;
        W = temp * q1.W + delta * q2.W;
    }

    public void RotationYawPitchRoll(float yaw, float pitch, float roll)
    {
        float syaw = MathF.Sin(yaw / 2.0f), cyaw = MathF.Cos(yaw / 2.0f);
        float spitch = MathF.Sin(pitch / 2.0f), cpitch = MathF.Cos(pitch / 2.0f);
        float sroll = MathF.Sin(roll / 2.0f), croll = MathF.Cos(roll / 2.0f);

        X = syaw * cpitch * sroll + cyaw * spitch * croll;
        Y = syaw * cpitch * croll - cyaw * spitch * sroll;
        Z = cyaw * cpitch * sroll - syaw * spitch * croll;
        W = cyaw * cpitch * croll + syaw * spitch * sroll;
    }

    public static Quaternion operator *(Quaternion a, Quaternion q) => new(
        q.W * a.X + q.X * a.W + q.Y * a.Z - q.Z * a.Y,
        q.W * a.Y - q.X * a.Z + q.Y * a.W + q.Z * a.X,
        q.W * a.Z + q.X * a.Y - q.Y * a.X + q.Z * a.W,
        q.W * a.W - q.X * a.X - q.Y * a.Y - q.Z * a.Z);
}
