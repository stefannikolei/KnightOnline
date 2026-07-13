using System.Numerics;
using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-5.3 pins: the four mesh readers and the PMesh LOD walk.</summary>
public class N3MeshTests
{
    private static (T Loaded, long Position, long Length) Roundtrip<T>(T original) where T : N3BaseFile, new()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        stream.Position = 0;
        var loaded = new T();
        using var reader = new BinaryReader(stream);
        loaded.Load(reader);
        return (loaded, stream.Position, stream.Length);
    }

    private static N3VertexT1 Vertex(float x, float y, float z) => new()
    {
        Position = new Vector3(x, y, z),
        Normal = Vector3.UnitY,
        Tu = x,
        Tv = z,
    };

    private static N3PMesh MakePMesh()
    {
        var mesh = new N3PMesh { Name = "pmesh_fixture" };
        mesh.Initialize(
            vertices: [Vertex(0, 0, 0), Vertex(1, 0, 0), Vertex(0, 0, 1), Vertex(1, 2, 1)],
            indices: [0, 1, 2, 0, 1, 2],
            minNumVertices: 3,
            minNumIndices: 3,
            collapses:
            [
                new N3PMesh.EdgeCollapse
                {
                    NumIndicesToLose = 3,
                    NumIndicesToChange = 1,
                    NumVerticesToLose = 1,
                    IndexChangesOffset = 0,
                    CollapseTo = 0,
                    ShouldCollapse = false,
                },
            ],
            allIndexChanges: [3],
            lodCtrlValues: [new N3PMesh.LodCtrlValue(10f, 4), new N3PMesh.LodCtrlValue(100f, 3)]);
        return mesh;
    }

    [Fact]
    public void PMesh_RoundTrips_WithExactRecordSizes()
    {
        N3PMesh original = MakePMesh();
        (N3PMesh loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        // [4+13 name][6*4 header][4*32 vertices][6*2 indices][24 collapse
        // record][1*4 index changes][4 + 2*8 LOD values] — pins the 24-byte
        // on-disk __EdgeCollapse (five ints + padded bool).
        Assert.Equal(17 + 24 + 128 + 12 + 24 + 4 + 20, len);

        Assert.Equal(4, loaded.MaxNumVertices);
        Assert.Equal(6, loaded.MaxNumIndices);
        Assert.Equal(3, loaded.MinNumVertices);
        Assert.Equal(3, loaded.MinNumIndices);
        Assert.Equal(1, loaded.NumCollapses);
        Assert.Equal(2, loaded.Collapses.Length); // incl. the zeroed sentinel
        Assert.Equal(3, loaded.Collapses[0].NumIndicesToLose);
        Assert.False(loaded.Collapses[0].ShouldCollapse);
        Assert.Equal(0, loaded.Collapses[1].NumIndicesToLose); // sentinel
        Assert.Equal([3], loaded.AllIndexChanges);
        Assert.Equal(new N3PMesh.LodCtrlValue(10f, 4), loaded.LodCtrlValues[0]);

        // FindMinMax ran on load.
        Assert.Equal(new Vector3(0, 0, 0), loaded.Min);
        Assert.Equal(new Vector3(1, 2, 1), loaded.Max);
        Assert.Equal(new Vector3(1, 2, 1).Length() * 0.5f, loaded.Radius, 5);
    }

    [Fact]
    public void PMesh_NegativeIndexChangesOffset_ClampedToZeroOnLoad()
    {
        N3PMesh original = MakePMesh();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        // Patch the collapse record's iIndexChanges (offset: 17 name + 24
        // header + 128 vertices + 12 indices + 3 ints into the record).
        stream.Position = 17 + 24 + 128 + 12 + 12;
        stream.Write(BitConverter.GetBytes(-1));

        stream.Position = 0;
        var loaded = new N3PMesh();
        loaded.Load(new BinaryReader(stream));

        // The C++ load repairs broken meshes: negative offsets become 0.
        Assert.Equal(0, loaded.Collapses[0].IndexChangesOffset);
    }

    [Fact]
    public void PMeshInstance_SplitAndCollapse_WalkTheRecords()
    {
        var instance = new N3PMeshInstance(MakePMesh());

        // Starts at the lowest LOD.
        Assert.Equal(3, instance.NumVertices);
        Assert.Equal(3, instance.NumIndices);

        instance.SetLodByNumVertices(4);
        Assert.Equal(4, instance.NumVertices);
        Assert.Equal(6, instance.NumIndices);
        // The split rewired index slot 3 to the newly added vertex.
        Assert.Equal([0, 1, 2, 3, 1, 2], instance.Indices);

        instance.SetLodByNumVertices(3);
        Assert.Equal(3, instance.NumVertices);
        Assert.Equal(3, instance.NumIndices);
        Assert.Equal([0, 1, 2, 0, 1, 2], instance.Indices); // collapsed back to CollapseTo

        // SetLod: nearer than the first LOD distance -> full detail.
        instance.SetLod(5f);
        Assert.Equal(4, instance.NumVertices);
        // Farther than the last -> minimum.
        instance.SetLod(200f);
        Assert.Equal(3, instance.NumVertices);
    }

    [Fact]
    public void VMesh_RoundTrips()
    {
        var original = new N3VMesh { Name = "col_mesh" };
        original.Initialize(
            [new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 4, 0)],
            [0, 1, 2]);

        (N3VMesh loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(original.Vertices, loaded.Vertices);
        Assert.Equal(original.Indices, loaded.Indices);
        Assert.Equal(new Vector3(2, 4, 0).Length() * 0.5f, loaded.Radius, 5);
    }

    [Fact]
    public void IMesh_RoundTrips_WithUvs()
    {
        var original = new N3IMesh { Name = "part_mesh" };
        original.Initialize(
            faceCount: 1,
            vertices:
            [
                new N3VertexXyzNormal { Position = new Vector3(0, 0, 0), Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = new Vector3(1, 0, 0), Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = new Vector3(0, 0, 1), Normal = Vector3.UnitY },
            ],
            vertexIndices: [0, 1, 2],
            uvs: [0f, 0f, 1f, 0f, 0f, 1f],
            uvIndices: [0, 1, 2]);

        (N3IMesh loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(1, loaded.FaceCount);
        Assert.Equal(3, loaded.VertexCount);
        Assert.Equal(3, loaded.UvCount);
        Assert.Equal(original.Uvs, loaded.Uvs);

        N3VertexT1[] flat = loaded.BuildVertexList();
        Assert.Equal(3, flat.Length);
        Assert.Equal(new Vector3(1, 0, 0), flat[1].Position);
        Assert.Equal(1f, flat[1].Tu);
        Assert.Equal(0f, flat[1].Tv);
    }

    [Fact]
    public void IMesh_DegenerateHeader_SkipsUvBlockLikeCpp()
    {
        // faceCount 0 -> Release(); because m_nUVC is then 0 the C++ never
        // reads the UV block even though the header claims uvCount = 5.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            writer.Write(0);  // name length
            writer.Write(0);  // faceCount
            writer.Write(3);  // vertexCount
            writer.Write(5);  // uvCount (claimed but never read)
        }

        stream.Position = 0;
        var mesh = new N3IMesh();
        mesh.Load(new BinaryReader(stream));

        Assert.Equal(0, mesh.FaceCount);
        Assert.Equal(0, mesh.UvCount);
        Assert.Equal(stream.Length, stream.Position); // nothing further read
    }

    [Fact]
    public void Mesh_HasNoNameHeader()
    {
        var original = new N3Mesh { Name = "ignored - CN3Mesh::Load skips the name header" };
        original.Initialize([Vertex(1, 2, 3)], [0]);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        // [4 vc][32 vertex][4 ic][2 index] — no [len][name] prefix.
        Assert.Equal(4 + 32 + 4 + 2, stream.Length);

        stream.Position = 0;
        var loaded = new N3Mesh();
        loaded.Load(new BinaryReader(stream));
        Assert.Equal(string.Empty, loaded.Name);
        Assert.Equal(original.Vertices, loaded.Vertices);
        Assert.Equal(new Vector3(1, 2, 3), loaded.Min);
    }
}
