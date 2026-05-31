using OpenKO.Numerics;
using Xunit;

namespace OpenKO.Tests;

public class NumericsTests
{
    private const float Eps = 1e-5f;

    [Fact]
    public void Vector3CrossProduct()
    {
        Vector3 c = default;
        c.Cross(new Vector3(1, 0, 0), new Vector3(0, 1, 0));
        Assert.Equal(0, c.X, Eps);
        Assert.Equal(0, c.Y, Eps);
        Assert.Equal(1, c.Z, Eps);
    }

    [Fact]
    public void Vector3NormalizeMakesUnitLength()
    {
        var v = new Vector3(3, 4, 0);
        v.Normalize();
        Assert.Equal(1.0f, v.Magnitude(), Eps);
    }

    [Fact]
    public void MatrixIdentityTimesItselfIsIdentity()
    {
        Matrix44 a = default;
        a.Identity();
        Matrix44 r = a * a;
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
                Assert.Equal(i == j ? 1.0f : 0.0f, r[i, j], Eps);
        }
    }

    [Fact]
    public void MatrixInverseOfRotationIsTranspose()
    {
        Matrix44 rot = default;
        rot.RotationZ(0.7f);
        Matrix44 inv = rot.Inverse();
        Matrix44 product = rot * inv;

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
                Assert.Equal(i == j ? 1.0f : 0.0f, product[i, j], 1e-4f);
        }
    }

    [Fact]
    public void QuaternionToMatrixToQuaternionRoundTrips()
    {
        var q = new Quaternion();
        q.RotationYawPitchRoll(0.3f, -0.4f, 0.1f);

        var m = new Matrix44(q);
        var q2 = new Quaternion(m);

        // q and q2 may differ by sign (double cover); compare magnitudes of components.
        Assert.Equal(MathF.Abs(q.X), MathF.Abs(q2.X), 1e-4f);
        Assert.Equal(MathF.Abs(q.Y), MathF.Abs(q2.Y), 1e-4f);
        Assert.Equal(MathF.Abs(q.Z), MathF.Abs(q2.Z), 1e-4f);
        Assert.Equal(MathF.Abs(q.W), MathF.Abs(q2.W), 1e-4f);
    }
}
