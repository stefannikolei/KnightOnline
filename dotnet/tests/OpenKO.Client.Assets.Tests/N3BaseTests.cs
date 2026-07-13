using System.Numerics;
using System.Runtime.InteropServices;
using OpenKO.Client.Assets;
using OpenKO.GameData.Math;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-5.1 pins: vertex layouts, the N3 file header and the math port.</summary>
public class N3BaseTests
{
    [Theory]
    [InlineData(typeof(N3VertexColor), 16)]              // sizeof(__VertexColor)
    [InlineData(typeof(N3VertexParticle), 20)]           // sizeof(__VertexParticle)
    [InlineData(typeof(N3VertexTransformedColor), 20)]   // sizeof(__VertexTransformedColor)
    [InlineData(typeof(N3VertexT1), 32)]                 // sizeof(__VertexT1)
    [InlineData(typeof(N3VertexT2), 40)]                 // sizeof(__VertexT2)
    [InlineData(typeof(N3VertexTransformed), 28)]        // sizeof(__VertexTransformed)
    [InlineData(typeof(N3VertexTransformedT2), 36)]      // sizeof(__VertexTransformedT2)
    [InlineData(typeof(N3VertexXyzT1), 20)]              // sizeof(__VertexXyzT1)
    [InlineData(typeof(N3VertexXyzT2), 28)]              // sizeof(__VertexXyzT2)
    [InlineData(typeof(N3VertexXyzNormal), 24)]          // sizeof(__VertexXyzNormal)
    [InlineData(typeof(N3VertexXyzColor), 16)]           // sizeof(__VertexXyzColor)
    [InlineData(typeof(N3VertexXyzColorT1), 24)]         // sizeof(__VertexXyzColorT1)
    [InlineData(typeof(N3VertexXyzColorT2), 32)]         // sizeof(__VertexXyzColorT2)
    [InlineData(typeof(N3VertexXyzColorSpecularT1), 28)] // sizeof(__VertexXyzColorSpecularT1)
    [InlineData(typeof(N3VertexXyzNormalColor), 28)]     // sizeof(__VertexXyzNormalColor)
    public void VertexStructs_MatchCppSizes(Type type, int expectedSize)
    {
        Assert.Equal(expectedSize, Marshal.SizeOf(type));
    }

    private sealed class HeaderOnlyFile : N3BaseFile
    {
    }

    [Fact]
    public void NameHeader_RoundTrips()
    {
        var file = new HeaderOnlyFile { Name = "item_kaul.n3pmesh" };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            file.Save(writer);
        }

        stream.Position = 0;
        var loaded = new HeaderOnlyFile();
        using var reader = new BinaryReader(stream);
        loaded.Load(reader);

        Assert.Equal("item_kaul.n3pmesh", loaded.Name);
        // [int32 len][bytes] — 4 + 17.
        Assert.Equal(4 + 17, stream.Length);
    }

    [Fact]
    public void NameHeader_ZeroOrNegativeLength_YieldsEmptyName()
    {
        // The C++ treats nL <= 0 as "no name" and reads nothing further.
        var negative = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }; // -1
        using var reader = new BinaryReader(new MemoryStream(negative));
        var file = new HeaderOnlyFile { Name = "prefilled" };
        file.Load(reader);
        Assert.Equal(string.Empty, file.Name);
    }

    [Fact]
    public void LoadFromFile_SetsVersionAndReadsHeader()
    {
        string path = Path.Combine(Path.GetTempPath(), $"n3-{Guid.NewGuid():N}.bin");
        try
        {
            var original = new HeaderOnlyFile { Name = "fixture" };
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                original.Save(writer);
            }

            var loaded = new HeaderOnlyFile();
            loaded.LoadFromFile(path, N3FormatVersion.V1298);

            Assert.Equal("fixture", loaded.Name);
            Assert.Equal(N3FormatVersion.V1298, loaded.FileFormatVersion);
            Assert.Equal(path, loaded.FileName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void N3String_And_StructuredReads_RoundTrip()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            writer.WriteN3String("chr_ka_m");
            writer.Write(new Vector3(1.5f, -2.25f, 3.75f));
            writer.Write(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
            writer.Write(Matrix4x4.CreateTranslation(10f, 20f, 30f));
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.Equal("chr_ka_m", reader.ReadN3String());
        Assert.Equal(new Vector3(1.5f, -2.25f, 3.75f), reader.ReadVector3());
        Assert.Equal(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), reader.ReadQuaternion());
        Matrix4x4 m = reader.ReadMatrix4x4();
        Assert.Equal(10f, m.M41);
        Assert.Equal(30f, m.M43);
    }

    // ---- KoQuaternion pins ----

    [Fact]
    public void Slerp_Endpoints_ReturnInputs()
    {
        var a = Quaternion.Normalize(new Quaternion(0.2f, 0.4f, 0.1f, 0.88f));
        var b = Quaternion.Normalize(new Quaternion(-0.3f, 0.1f, 0.5f, 0.8f));

        Quaternion at0 = KoQuaternion.Slerp(a, b, 0f);
        Quaternion at1 = KoQuaternion.Slerp(a, b, 1f);

        Assert.Equal(a.X, at0.X, 5);
        Assert.Equal(a.W, at0.W, 5);
        Assert.Equal(b.X, at1.X, 5);
        Assert.Equal(b.W, at1.W, 5);
    }

    [Fact]
    public void Slerp_HalfwayBetweenAxisRotations_MatchesHandComputedValue()
    {
        // 0° and 90° about Y: halfway must be 45° about Y.
        var a = new Quaternion(0f, 0f, 0f, 1f);
        var b = new Quaternion(0f, MathF.Sin(MathF.PI / 4f), 0f, MathF.Cos(MathF.PI / 4f));

        Quaternion mid = KoQuaternion.Slerp(a, b, 0.5f);

        float expected = MathF.Sin(MathF.PI / 8f);
        Assert.Equal(expected, mid.Y, 5);
        Assert.Equal(MathF.Cos(MathF.PI / 8f), mid.W, 5);
    }

    [Fact]
    public void Slerp_NegativeDot_TakesShortestPath()
    {
        // q and -q represent the same rotation; the C++ flips the sign.
        var a = new Quaternion(0f, 0f, 0f, 1f);
        var b = new Quaternion(0f, 0f, 0f, -1f);

        Quaternion mid = KoQuaternion.Slerp(a, b, 0.5f);

        // dot=-1 → flipped to 1.0 → small-angle branch: pure lerp of a and -b.
        Assert.Equal(1f, mid.W, 5);
    }

    [Fact]
    public void Slerp_SmallAngle_UsesLerpBranch()
    {
        // dot > 0.999 → the C++ skips the trig and lerps linearly (no normalize).
        var a = new Quaternion(0f, 0f, 0f, 1f);
        var b = Quaternion.Normalize(new Quaternion(0.01f, 0f, 0f, 1f));

        Quaternion mid = KoQuaternion.Slerp(a, b, 0.5f);

        Assert.Equal((a.X + b.X) / 2f, mid.X, 6);
        Assert.Equal((a.W + b.W) / 2f, mid.W, 6);
    }

    [Fact]
    public void RotationYawPitchRoll_MatchesD3DXConvention()
    {
        // Yaw 90° must equal a 90° rotation about Y.
        Quaternion q = KoQuaternion.RotationYawPitchRoll(MathF.PI / 2f, 0f, 0f);
        Assert.Equal(MathF.Sin(MathF.PI / 4f), q.Y, 5);
        Assert.Equal(MathF.Cos(MathF.PI / 4f), q.W, 5);
        Assert.Equal(0f, q.X, 5);
        Assert.Equal(0f, q.Z, 5);
    }
}
