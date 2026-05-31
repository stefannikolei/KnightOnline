using OpenKO.N3;
using OpenKO.Numerics;
using Xunit;

namespace OpenKO.Tests;

public class N3IMeshBuildTests
{
    /// <summary>Exposes the protected setters so a mesh can be assembled in-memory for testing.</summary>
    private sealed class TestMesh : N3IMesh
    {
        public void SetData(
            int faceCount,
            VertexXyzNormal[] vertices,
            ushort[] vertexIndices,
            float[] uvs,
            ushort[] uvIndices)
        {
            FaceCount = faceCount;
            Vertices = vertices;
            VertexIndices = vertexIndices;
            UvCount = uvs.Length / 2;
            Uvs = uvs;
            UvIndices = uvIndices;
        }
    }

    [Fact]
    public void BuildVertexListExpandsIndicesAndUvs()
    {
        var mesh = new TestMesh();
        mesh.SetData(
            faceCount: 1,
            vertices: new[]
            {
                new VertexXyzNormal(new Vector3(0, 0, 0), new Vector3(0, 0, 1)),
                new VertexXyzNormal(new Vector3(2, 0, 0), new Vector3(0, 0, 1)),
                new VertexXyzNormal(new Vector3(0, 3, 0), new Vector3(0, 0, 1)),
            },
            vertexIndices: new ushort[] { 0, 1, 2 },
            uvs: new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f },
            uvIndices: new ushort[] { 2, 0, 1 });

        VertexT1[] verts = mesh.BuildVertexList();

        Assert.Equal(3, verts.Length);

        // Corner 0: vertex index 0, uv index 2 => uv (0.5, 0.6)
        Assert.Equal(0f, verts[0].Position.X);
        Assert.Equal(0.5f, verts[0].U);
        Assert.Equal(0.6f, verts[0].V);

        // Corner 1: vertex index 1, uv index 0 => uv (0.1, 0.2)
        Assert.Equal(2f, verts[1].Position.X);
        Assert.Equal(0.1f, verts[1].U);
        Assert.Equal(0.2f, verts[1].V);

        // Corner 2: vertex index 2, uv index 1 => uv (0.3, 0.4)
        Assert.Equal(3f, verts[2].Position.Y);
        Assert.Equal(0.3f, verts[2].U);
        Assert.Equal(0.4f, verts[2].V);

        // Normals carried through.
        Assert.Equal(1f, verts[0].Normal.Z);
    }

    [Fact]
    public void BuildVertexListWithoutUvsEmitsZeroUvs()
    {
        var mesh = new TestMesh();
        mesh.SetData(
            faceCount: 1,
            vertices: new[]
            {
                new VertexXyzNormal(new Vector3(0, 0, 0), new Vector3(0, 1, 0)),
                new VertexXyzNormal(new Vector3(1, 0, 0), new Vector3(0, 1, 0)),
                new VertexXyzNormal(new Vector3(0, 1, 0), new Vector3(0, 1, 0)),
            },
            vertexIndices: new ushort[] { 0, 1, 2 },
            uvs: Array.Empty<float>(),
            uvIndices: Array.Empty<ushort>());

        VertexT1[] verts = mesh.BuildVertexList();

        Assert.Equal(3, verts.Length);
        Assert.All(verts, v =>
        {
            Assert.Equal(0f, v.U);
            Assert.Equal(0f, v.V);
        });
    }

    [Fact]
    public void BuildVertexListOnEmptyMeshReturnsEmpty()
    {
        Assert.Empty(new N3IMesh().BuildVertexList());
    }
}
