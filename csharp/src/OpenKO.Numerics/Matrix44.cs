using System.Runtime.CompilerServices;

namespace OpenKO.Numerics;

/// <summary>Backing storage for <see cref="Matrix44"/> — 16 contiguous floats (row-major).</summary>
[InlineArray(16)]
internal struct Matrix44Storage
{
    private float _element0;
}

/// <summary>
/// Port of the C++ <c>__Matrix44</c> (MathUtils/Matrix44). Row-major 4x4, left-handed conventions,
/// matching the original DirectX-style layout. Indexed as <c>m[row, col]</c>.
/// </summary>
public struct Matrix44
{
    private Matrix44Storage _m;

    public float this[int row, int col]
    {
        readonly get => _m[(row << 2) + col];
        set => _m[(row << 2) + col] = value;
    }

    public Matrix44(in Quaternion qt)
    {
        SetFromQuaternion(qt);
    }

    public void Zero()
    {
        for (int i = 0; i < 16; i++)
            _m[i] = 0.0f;
    }

    public void Identity()
    {
        Zero();
        this[0, 0] = this[1, 1] = this[2, 2] = this[3, 3] = 1.0f;
    }

    public readonly Matrix44 Inverse()
    {
        Matrix44 result = default;
        BuildInverse(ref result);
        return result;
    }

    public readonly void BuildInverse(ref Matrix44 outMtx)
    {
        Span<float> t = stackalloc float[3];
        Span<float> v = stackalloc float[16];

        t[0] = this[2, 2] * this[3, 3] - this[2, 3] * this[3, 2];
        t[1] = this[1, 2] * this[3, 3] - this[1, 3] * this[3, 2];
        t[2] = this[1, 2] * this[2, 3] - this[1, 3] * this[2, 2];
        v[0] = this[1, 1] * t[0] - this[2, 1] * t[1] + this[3, 1] * t[2];
        v[4] = -this[1, 0] * t[0] + this[2, 0] * t[1] - this[3, 0] * t[2];

        t[0] = this[1, 0] * this[2, 1] - this[2, 0] * this[1, 1];
        t[1] = this[1, 0] * this[3, 1] - this[3, 0] * this[1, 1];
        t[2] = this[2, 0] * this[3, 1] - this[3, 0] * this[2, 1];
        v[8] = this[3, 3] * t[0] - this[2, 3] * t[1] + this[1, 3] * t[2];
        v[12] = -this[3, 2] * t[0] + this[2, 2] * t[1] - this[1, 2] * t[2];

        float det = this[0, 0] * v[0] + this[0, 1] * v[4] + this[0, 2] * v[8] + this[0, 3] * v[12];
        if (det == 0.0f)
        {
            outMtx.Identity();
            return;
        }

        t[0] = this[2, 2] * this[3, 3] - this[2, 3] * this[3, 2];
        t[1] = this[0, 2] * this[3, 3] - this[0, 3] * this[3, 2];
        t[2] = this[0, 2] * this[2, 3] - this[0, 3] * this[2, 2];
        v[1] = -this[0, 1] * t[0] + this[2, 1] * t[1] - this[3, 1] * t[2];
        v[5] = this[0, 0] * t[0] - this[2, 0] * t[1] + this[3, 0] * t[2];

        t[0] = this[0, 0] * this[2, 1] - this[2, 0] * this[0, 1];
        t[1] = this[3, 0] * this[0, 1] - this[0, 0] * this[3, 1];
        t[2] = this[2, 0] * this[3, 1] - this[3, 0] * this[2, 1];
        v[9] = -this[3, 3] * t[0] - this[2, 3] * t[1] - this[0, 3] * t[2];
        v[13] = this[3, 2] * t[0] + this[2, 2] * t[1] + this[0, 2] * t[2];

        t[0] = this[1, 2] * this[3, 3] - this[1, 3] * this[3, 2];
        t[1] = this[0, 2] * this[3, 3] - this[0, 3] * this[3, 2];
        t[2] = this[0, 2] * this[1, 3] - this[0, 3] * this[1, 2];
        v[2] = this[0, 1] * t[0] - this[1, 1] * t[1] + this[3, 1] * t[2];
        v[6] = -this[0, 0] * t[0] + this[1, 0] * t[1] - this[3, 0] * t[2];

        t[0] = this[0, 0] * this[1, 1] - this[1, 0] * this[0, 1];
        t[1] = this[3, 0] * this[0, 1] - this[0, 0] * this[3, 1];
        t[2] = this[1, 0] * this[3, 1] - this[3, 0] * this[1, 1];
        v[10] = this[3, 3] * t[0] + this[1, 3] * t[1] + this[0, 3] * t[2];
        v[14] = -this[3, 2] * t[0] - this[1, 2] * t[1] - this[0, 2] * t[2];

        t[0] = this[1, 2] * this[2, 3] - this[1, 3] * this[2, 2];
        t[1] = this[0, 2] * this[2, 3] - this[0, 3] * this[2, 2];
        t[2] = this[0, 2] * this[1, 3] - this[0, 3] * this[1, 2];
        v[3] = -this[0, 1] * t[0] + this[1, 1] * t[1] - this[2, 1] * t[2];
        v[7] = this[0, 0] * t[0] - this[1, 0] * t[1] + this[2, 0] * t[2];

        v[11]
            = -this[0, 0] * (this[1, 1] * this[2, 3] - this[1, 3] * this[2, 1])
            + this[1, 0] * (this[0, 1] * this[2, 3] - this[0, 3] * this[2, 1])
            - this[2, 0] * (this[0, 1] * this[1, 3] - this[0, 3] * this[1, 1]);

        v[15]
            = this[0, 0] * (this[1, 1] * this[2, 2] - this[1, 2] * this[2, 1])
            - this[1, 0] * (this[0, 1] * this[2, 2] - this[0, 2] * this[2, 1])
            + this[2, 0] * (this[0, 1] * this[1, 2] - this[0, 2] * this[1, 1]);

        float invDet = 1.0f / det;
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
                outMtx[i, j] = v[4 * i + j] * invDet;
        }
    }

    public readonly Vector3 Pos() => new(this[3, 0], this[3, 1], this[3, 2]);

    public void PosSet(float x, float y, float z)
    {
        this[3, 0] = x;
        this[3, 1] = y;
        this[3, 2] = z;
    }

    public void PosSet(Vector3 v) => PosSet(v.X, v.Y, v.Z);

    public void RotationX(float delta)
    {
        Identity();
        this[1, 1] = MathF.Cos(delta);
        this[1, 2] = MathF.Sin(delta);
        this[2, 1] = -this[1, 2];
        this[2, 2] = this[1, 1];
    }

    public void RotationY(float delta)
    {
        Identity();
        this[0, 0] = MathF.Cos(delta);
        this[0, 2] = -MathF.Sin(delta);
        this[2, 0] = -this[0, 2];
        this[2, 2] = this[0, 0];
    }

    public void RotationZ(float delta)
    {
        Identity();
        this[0, 0] = MathF.Cos(delta);
        this[0, 1] = MathF.Sin(delta);
        this[1, 0] = -this[0, 1];
        this[1, 1] = this[0, 0];
    }

    public void Rotation(float fx, float fy, float fz)
    {
        float sx = MathF.Sin(fx), cx = MathF.Cos(fx);
        float sy = MathF.Sin(fy), cy = MathF.Cos(fy);
        float sz = MathF.Sin(fz), cz = MathF.Cos(fz);

        this[0, 0] = cy * cz;
        this[0, 1] = cy * sz;
        this[0, 2] = -sy;
        this[0, 3] = 0;

        this[1, 0] = sx * sy * cz - cx * sz;
        this[1, 1] = sx * sy * sz + cx * cz;
        this[1, 2] = sx * cy;
        this[1, 3] = 0;

        this[2, 0] = cx * sy * cz + sx * sz;
        this[2, 1] = cx * sy * sz - sx * cz;
        this[2, 2] = cx * cy;
        this[2, 3] = 0;

        this[3, 0] = this[3, 1] = this[3, 2] = 0;
        this[3, 3] = 1;
    }

    public void Rotation(Vector3 v) => Rotation(v.X, v.Y, v.Z);

    public void Scale(float sx, float sy, float sz)
    {
        Identity();
        this[0, 0] = sx;
        this[1, 1] = sy;
        this[2, 2] = sz;
    }

    public void Scale(Vector3 v) => Scale(v.X, v.Y, v.Z);

    public void LookAtLH(Vector3 eye, Vector3 at, Vector3 up)
    {
        Vector3 vec = at - eye;
        vec.Normalize();

        Vector3 right = default;
        right.Cross(up, vec);
        Vector3 upn = default;
        upn.Cross(vec, right);

        right.Normalize();
        upn.Normalize();

        this[0, 0] = right.X;
        this[1, 0] = right.Y;
        this[2, 0] = right.Z;
        this[3, 0] = -right.Dot(eye);
        this[0, 1] = upn.X;
        this[1, 1] = upn.Y;
        this[2, 1] = upn.Z;
        this[3, 1] = -upn.Dot(eye);
        this[0, 2] = vec.X;
        this[1, 2] = vec.Y;
        this[2, 2] = vec.Z;
        this[3, 2] = -vec.Dot(eye);
        this[0, 3] = 0.0f;
        this[1, 3] = 0.0f;
        this[2, 3] = 0.0f;
        this[3, 3] = 1.0f;
    }

    public void OrthoLH(float w, float h, float zn, float zf)
    {
        Identity();
        this[0, 0] = 2.0f / w;
        this[1, 1] = 2.0f / h;
        this[2, 2] = 1.0f / (zf - zn);
        this[3, 2] = zn / (zn - zf);
    }

    public void PerspectiveFovLH(float fovy, float aspect, float zn, float zf)
    {
        Identity();
        this[0, 0] = 1.0f / (aspect * MathF.Tan(fovy / 2.0f));
        this[1, 1] = 1.0f / MathF.Tan(fovy / 2.0f);
        this[2, 2] = zf / (zf - zn);
        this[2, 3] = 1.0f;
        this[3, 2] = (zf * zn) / (zn - zf);
        this[3, 3] = 0.0f;
    }

    public void SetFromQuaternion(in Quaternion qt)
    {
        this[0, 0] = 1.0f - 2.0f * (qt.Y * qt.Y + qt.Z * qt.Z);
        this[0, 1] = 2.0f * (qt.X * qt.Y + qt.Z * qt.W);
        this[0, 2] = 2.0f * (qt.X * qt.Z - qt.Y * qt.W);
        this[0, 3] = 0.0f;
        this[1, 0] = 2.0f * (qt.X * qt.Y - qt.Z * qt.W);
        this[1, 1] = 1.0f - 2.0f * (qt.X * qt.X + qt.Z * qt.Z);
        this[1, 2] = 2.0f * (qt.Y * qt.Z + qt.X * qt.W);
        this[1, 3] = 0.0f;
        this[2, 0] = 2.0f * (qt.X * qt.Z + qt.Y * qt.W);
        this[2, 1] = 2.0f * (qt.Y * qt.Z - qt.X * qt.W);
        this[2, 2] = 1.0f - 2.0f * (qt.X * qt.X + qt.Y * qt.Y);
        this[2, 3] = 0.0f;
        this[3, 0] = 0.0f;
        this[3, 1] = 0.0f;
        this[3, 2] = 0.0f;
        this[3, 3] = 1.0f;
    }

    public static Matrix44 operator *(in Matrix44 a, in Matrix44 b)
    {
        Matrix44 r = default;
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                r[i, j] = a[i, 0] * b[0, j]
                        + a[i, 1] * b[1, j]
                        + a[i, 2] * b[2, j]
                        + a[i, 3] * b[3, j];
            }
        }

        return r;
    }

    public static Matrix44 operator *(in Matrix44 a, in Quaternion qRot)
    {
        Matrix44 m = new(qRot);
        return a * m;
    }
}
