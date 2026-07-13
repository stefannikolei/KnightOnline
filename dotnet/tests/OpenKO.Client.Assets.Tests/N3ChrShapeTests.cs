using System.Numerics;
using System.Runtime.InteropServices;
using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-5.5 pins: character parts/plugs/chr and shapes.</summary>
public class N3ChrShapeTests
{
    [Fact]
    public void Material_MatchesCppSize()
    {
        // sizeof(__Material) = D3DMATERIAL9 (68) + 6 uints = 92.
        Assert.Equal(92, Marshal.SizeOf<N3Material>());
    }

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

    [Fact]
    public void CPart_RoundTrips()
    {
        var original = new N3CPart
        {
            Name = "part",
            Reserved = 7,
            Material = new N3Material { Diffuse = new N3ColorValue { R = 1f, A = 0.5f }, RenderFlags = 0x1 },
            TexFileName = @"chr\ka_body.dxt",
            SkinsFileName = @"chr\ka_body.n3cskins",
        };

        (N3CPart loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(7u, loaded.Reserved);
        Assert.Equal(1f, loaded.Material.Diffuse.R);
        Assert.Equal(0.5f, loaded.Material.Diffuse.A);
        Assert.Equal(0x1u, loaded.Material.RenderFlags);
        Assert.Equal(@"chr\ka_body.dxt", loaded.TexFileName);
        Assert.Equal(@"chr\ka_body.n3cskins", loaded.SkinsFileName);
    }

    [Fact]
    public void CPartSkins_LoadsExactlyFourLods()
    {
        var original = new N3CPartSkins { Name = "skins" };
        original.Skins[0].Initialize(
            faceCount: 1,
            vertices:
            [
                new N3VertexXyzNormal { Position = Vector3.Zero, Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = Vector3.UnitX, Normal = Vector3.UnitY },
                new N3VertexXyzNormal { Position = Vector3.UnitZ, Normal = Vector3.UnitY },
            ],
            vertexIndices: [0, 1, 2],
            uvs: [],
            uvIndices: []);
        original.Skins[0].InitializeSkin(
        [
            new N3SkinVertex { Origin = Vector3.Zero, Joints = [1] },
            new N3SkinVertex { Origin = Vector3.UnitX, Joints = [1] },
            new N3SkinVertex { Origin = Vector3.UnitZ, Joints = [2, 3], Weights = [0.5f, 0.5f] },
        ]);

        (N3CPartSkins loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(3, loaded.Skins[0].VertexCount);
        Assert.Equal(0, loaded.Skins[1].VertexCount); // empty LODs still serialized
        Assert.Equal([2, 3], loaded.Skins[0].SkinVertices[2].Joints);
    }

    [Fact]
    public void CPlug_RoundTrips_WithTraceAndEmbeddedPMesh()
    {
        var fx = new N3PMesh { Name = "fxmesh" };
        fx.Initialize(
            vertices:
            [
                new N3VertexT1 { Position = Vector3.Zero, Normal = Vector3.UnitY },
                new N3VertexT1 { Position = Vector3.UnitX, Normal = Vector3.UnitY },
                new N3VertexT1 { Position = Vector3.UnitZ, Normal = Vector3.UnitY },
            ],
            indices: [0, 1, 2],
            minNumVertices: 3,
            minNumIndices: 3,
            collapses: [],
            allIndexChanges: [],
            lodCtrlValues: []);

        var original = new N3CPlug
        {
            Name = "plug",
            PlugType = N3PlugType.Normal,
            JointIndex = 12,
            Position = new Vector3(0, 0.1f, 0),
            Scale = Vector3.One,
            PMeshFileName = @"item\sword.n3pmesh",
            TexFileName = @"item\sword.dxt",
            TraceStep = 8,
            TraceColor = 0xFFFF0000,
            Trace0 = 0.1f,
            Trace1 = 0.9f,
            FxPMesh = fx,
        };

        (N3CPlug loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(12, loaded.JointIndex);
        Assert.Equal(8, loaded.TraceStep);
        Assert.Equal(0xFFFF0000, loaded.TraceColor);
        Assert.NotNull(loaded.FxPMesh);
        Assert.Equal(3, loaded.FxPMesh!.MaxNumVertices);

        // Without trace/mesh the optional blocks vanish.
        original.TraceStep = 0;
        original.FxPMesh = null;
        (loaded, pos, len) = Roundtrip(original);
        Assert.Equal(pos, len);
        Assert.Equal(0, loaded.TraceStep);
        Assert.Null(loaded.FxPMesh);
    }

    [Fact]
    public void PlugType_UnknownValue_ClampedToNormal()
    {
        var original = new N3CPlugCloak { Name = "cloak", PlugType = N3PlugType.Cloak };
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        // Patch the plug type to 999 (> PLUGTYPE_MAX): offset = name (4+5) + 4.
        stream.Position = 9;
        stream.Write(BitConverter.GetBytes(999));

        stream.Position = 0;
        var loaded = new N3CPlugCloak();
        loaded.Load(new BinaryReader(stream));
        Assert.Equal(N3PlugType.Normal, loaded.PlugType); // C++ clamp

        Assert.Equal(N3PlugType.Normal, N3CPlugBase.GetPlugTypeByFileName("item\\x.n3cplug"));
        Assert.Equal(N3PlugType.Cloak, N3CPlugBase.GetPlugTypeByFileName("item\\x.n3cloak"));
        Assert.Equal(N3PlugType.Undefined, N3CPlugBase.GetPlugTypeByFileName("item\\x.dxt"));
    }

    [Fact]
    public void Chr_RoundTrips_AllReferenceNames()
    {
        var original = new N3Chr
        {
            Name = "chr_ka_m",
            Position = new Vector3(1, 0, 1),
            CollisionMeshFileName = @"chr\col.n3vmesh",
            JointFileName = @"chr\ka_bone.n3joint",
            AniCtrlFileName = @"chr\ka.n3anim",
            FxPlugFileName = string.Empty,
        };
        original.PartFileNames.AddRange([@"chr\ka_body.n3cpart", string.Empty, @"chr\ka_head.n3cpart"]);
        original.PlugFileNames.Add(@"item\sword.n3cplug");
        original.JointPartStarts[0] = 0;
        original.JointPartEnds[0] = 20;
        original.JointPartStarts[1] = 21;
        original.JointPartEnds[1] = 40;

        (N3Chr loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(@"chr\ka_bone.n3joint", loaded.JointFileName);
        Assert.Equal(3, loaded.PartFileNames.Count);
        Assert.Equal(string.Empty, loaded.PartFileNames[1]); // empty slot preserved
        Assert.Equal(@"item\sword.n3cplug", Assert.Single(loaded.PlugFileNames));
        Assert.Equal(@"chr\ka.n3anim", loaded.AniCtrlFileName);
        Assert.Equal(20, loaded.JointPartEnds[0]);
        Assert.Equal(21, loaded.JointPartStarts[1]);
        Assert.Equal(@"chr\col.n3vmesh", loaded.CollisionMeshFileName);
    }

    [Fact]
    public void Shape_RoundTrips_PartsAndGameMetadata()
    {
        var original = new N3Shape
        {
            Name = "gate",
            Belong = 1,
            EventId = 42,
            EventType = 3,
            NpcId = 21000,
            NpcStatus = 1,
        };
        var part = new N3SPart
        {
            Pivot = new Vector3(0, 2, 0),
            MeshFileName = @"object\gate.n3pmesh",
            TexFps = 15f,
        };
        part.TexFileNames.AddRange([@"object\gate0.dxt", string.Empty, @"object\gate1.dxt"]);
        original.Parts.Add(part);

        (N3Shape loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        N3SPart p = Assert.Single(loaded.Parts);
        Assert.Equal(new Vector3(0, 2, 0), p.Pivot);
        Assert.Equal(@"object\gate.n3pmesh", p.MeshFileName);
        Assert.Equal(15f, p.TexFps);
        Assert.Equal(3, p.TexFileNames.Count);
        Assert.Equal(string.Empty, p.TexFileNames[1]);
        Assert.Equal(42, loaded.EventId);
        Assert.Equal(21000, loaded.NpcId);
    }
}
