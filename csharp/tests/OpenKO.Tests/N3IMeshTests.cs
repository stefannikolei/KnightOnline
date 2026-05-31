using OpenKO.IO;
using OpenKO.N3;
using OpenKO.Numerics;
using Xunit;

namespace OpenKO.Tests;

public class N3IMeshTests
{
    private static N3IMesh BuildTriangleMesh()
    {
        // A single triangle with UVs.
        var mesh = new TestMesh();
        mesh.SetData(
            name: "tri",
            faceCount: 1,
            vertices: new[]
            {
                new VertexXyzNormal(new Vector3(0, 0, 0), new Vector3(0, 0, 1)),
                new VertexXyzNormal(new Vector3(2, 0, 0), new Vector3(0, 0, 1)),
                new VertexXyzNormal(new Vector3(0, 3, 0), new Vector3(0, 0, 1)),
            },
            vertexIndices: new ushort[] { 0, 1, 2 },
            uvs: new[] { 0f, 0f, 1f, 0f, 0f, 1f },
            uvIndices: new ushort[] { 0, 1, 2 });
        return mesh;
    }

    [Fact]
    public void FindMinMaxComputesBounds()
    {
        var mesh = BuildTriangleMesh();
        mesh.FindMinMax();

        Assert.Equal(0, mesh.Min.X);
        Assert.Equal(0, mesh.Min.Y);
        Assert.Equal(2, mesh.Max.X);
        Assert.Equal(3, mesh.Max.Y);
    }

    [Fact]
    public void SaveAndLoadRoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openko_mesh_{Guid.NewGuid():N}.n3imesh");
        try
        {
            var src = BuildTriangleMesh();
            using (var writer = new FileWriter())
            {
                Assert.True(writer.Create(path));
                Assert.True(src.Save(writer));
            }

            var loaded = new N3IMesh();
            using (var reader = new FileReader())
            {
                Assert.True(reader.OpenExisting(path));
                Assert.True(loaded.Load(reader));
            }

            Assert.Equal("tri", loaded.Name);
            Assert.Equal(1, loaded.FaceCount);
            Assert.Equal(3, loaded.VertexCount);
            Assert.Equal(3, loaded.UvCount);
            Assert.Equal(new ushort[] { 0, 1, 2 }, loaded.VertexIndices);
            Assert.Equal(2f, loaded.Vertices[1].Position.X);
            Assert.Equal(1f, loaded.Vertices[0].Normal.Z);
            Assert.Equal(3f, loaded.Max.Y);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>Test subclass exposing protected setters to assemble a mesh in-memory.</summary>
    private sealed class TestMesh : N3IMesh
    {
        public void SetData(
            string name,
            int faceCount,
            VertexXyzNormal[] vertices,
            ushort[] vertexIndices,
            float[] uvs,
            ushort[] uvIndices)
        {
            Name = name;
            FaceCount = faceCount;
            Vertices = vertices;
            VertexIndices = vertexIndices;
            UvCount = uvs.Length / 2;
            Uvs = uvs;
            UvIndices = uvIndices;
        }
    }
}
