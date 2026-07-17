using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Slice-9.10a pins: the N3FX* effect-bundle loader + data structures.</summary>
public class N3FxBundleTests
{
    private static (T Loaded, long Position, long Length) Roundtrip<T>(T original) where T : N3BaseFile, new()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true))
            original.Save(writer);

        stream.Position = 0;
        var loaded = new T();
        using var reader = new BinaryReader(stream, Encoding.Latin1);
        loaded.Load(reader);
        return (loaded, stream.Position, stream.Length);
    }

    private static (T Loaded, long Position, long Length) RoundtripPart<T>(T original) where T : N3FXPartBase, new()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true))
            original.Save(writer);

        stream.Position = 0;
        var loaded = new T();
        using var reader = new BinaryReader(stream, Encoding.Latin1);
        loaded.Load(reader);
        return (loaded, stream.Position, stream.Length);
    }

    // ---------------------------------------------------------------- enums + constants

    [Fact]
    public void Enums_MatchCppValues()
    {
        Assert.Equal(0, (int)FxPartType.None);
        Assert.Equal(1, (int)FxPartType.Particle);
        Assert.Equal(2, (int)FxPartType.Board);
        Assert.Equal(3, (int)FxPartType.Mesh);
        Assert.Equal(4, (int)FxPartType.BottomBoard);

        Assert.Equal(0u, (uint)FxPartParticleEmitType.Normal);
        Assert.Equal(1u, (uint)FxPartParticleEmitType.Spread);
        Assert.Equal(2u, (uint)FxPartParticleEmitType.Gather);

        Assert.Equal(0xffffffffu, (uint)FxBundleAct.MoveNone);
        Assert.Equal(2, (int)FxPartState.Live);
        Assert.Equal(2, (int)FxBundleState.Live);
    }

    [Fact]
    public void Constants_MatchCpp()
    {
        Assert.Equal(8, N3FxDef.MaxFxPartV0);
        Assert.Equal(16, N3FxDef.MaxFxPartV1Orig);
        Assert.Equal(26, N3FxDef.MaxFxPartV1);
        Assert.Equal(26, N3FxDef.MaxFxPart);
        Assert.Equal(4, N3FxDef.NumVertexParticle);
        Assert.Equal(10, N3FxDef.NumVertexBottom);
        Assert.Equal(100, N3FxDef.NumKeyColor);
        Assert.Equal(260, N3FxDef.MaxPath);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 8)]
    [InlineData(1, 26)]
    [InlineData(2, 26)]
    public void GetPartCountForVersion_MatchesCpp(int version, int expected)
    {
        var bundle = new N3FXBundle { Version = version };
        Assert.Equal(expected, bundle.GetPartCountForVersion());
    }

    [Fact]
    public void FxbInfo_IsExactly268Bytes()
    {
        var info = new FxbInfo { Name = "gfx\\test.fxb", Joint = 5, IsLooping = true };
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true))
            info.Save(writer);

        // char[260] + int joint + BOOL IsLooping.
        Assert.Equal(260 + 4 + 4, stream.Length);
    }

    // ---------------------------------------------------------------- part factory helpers

    private static N3FXPartParticles MakeParticles(int version = 11, int baseVersion = 2)
    {
        var part = new N3FXPartParticles
        {
            Version = version,
            BaseVersion = baseVersion,
            Type = FxPartType.Particle,
            Life = 3.5f,
            Velocity = new Vector3(1, 2, 3),
            Acceleration = new Vector3(0, -9.8f, 0),
            RotVelocity = new Vector3(0.1f, 0.2f, 0.3f),
            OnGround = true,
            Pos = new Vector3(5, 6, 7),
            NumTex = 4,
            TexFps = 24f,
            TexName = @"fx\spark",
            RenderFlag = 0x1 | 0x80,
            SrcBlend = 5,
            DestBlend = 2,
            FadeIn = 0.25f,
            FadeOut = 0.5f,
            NumParticle = 128,
            ParticleSizeMin = 0.5f,
            ParticleSizeMax = 1.5f,
            ParticleLifeMin = 1f,
            ParticleLifeMax = 2f,
            MinCreateRange = new Vector3(-1, -1, -1),
            MaxCreateRange = new Vector3(1, 1, 1),
            CreateDelay = 0.02f,
            NumCreate = 3,
            PtEmitDir = new Vector3(0, 1, 0),
            PtVelocity = 4f,
            PtAccel = 0.5f,
            PtRotVelocity = 0.7f,
            PtGravity = 9.8f,
            AnimKey = false,
            TexRotateVelocity = 1.1f,
            ScaleVelX = 0.3f,
            ScaleVelY = 0.4f,
            DistanceNumFix = true,
            ParticleYAxisFix = true,
            ParticleNotRotate = true,
            ParticleNotRotateAxis = new Vector3(0, 1, 0),
            PtRangeMin = 2f,
            PtRangeMax = 8f,
        };
        return part;
    }

    // ---------------------------------------------------------------- part round trips

    [Fact]
    public void Particle_V11_RoundTripsAllFields()
    {
        N3FXPartParticles original = MakeParticles();
        original.EmitType = FxPartParticleEmitType.Gather;
        original.EmitCondition = new ParticleEmitCondition { GatherPoint = new Vector3(9, 8, 7) };

        (N3FXPartParticles loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(11, loaded.Version);
        Assert.Equal(FxPartType.Particle, loaded.Type);
        Assert.Equal(128, loaded.NumParticle);
        Assert.Equal(0.5f, loaded.ParticleSizeMin);
        Assert.Equal(1.5f, loaded.ParticleSizeMax);
        Assert.Equal(FxPartParticleEmitType.Gather, loaded.EmitType);
        Assert.Equal(new Vector3(9, 8, 7), loaded.EmitCondition.GatherPoint);
        Assert.Equal(@"fx\spark", loaded.TexName);
        Assert.True(loaded.DistanceNumFix);
        Assert.True(loaded.ParticleNotRotate);
        Assert.Equal(new Vector3(0, 1, 0), loaded.ParticleNotRotateAxis);
        Assert.Equal(8f, loaded.PtRangeMax);
        Assert.Equal(0x1u | 0x80u, loaded.RenderFlag);
        Assert.True(loaded.Alpha); // derived from RF_ALPHABLENDING
    }

    [Fact]
    public void Particle_Spread_SerializesEmitAngle()
    {
        N3FXPartParticles original = MakeParticles();
        original.EmitType = FxPartParticleEmitType.Spread;
        original.EmitCondition = new ParticleEmitCondition { EmitAngle = 45f };

        (N3FXPartParticles loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(FxPartParticleEmitType.Spread, loaded.EmitType);
        Assert.Equal(45f, loaded.EmitCondition.EmitAngle);
    }

    [Fact]
    public void Particle_MaxColorKeys_RoundTrips()
    {
        N3FXPartParticles original = MakeParticles();
        original.ChangeColor = true;
        original.ChangeColorKeyCount = N3FxDef.NumKeyColor;
        for (int i = 0; i < N3FxDef.NumKeyColor; i++)
            original.ChangeColors[i] = (uint)(0xff000000 | i);

        (N3FXPartParticles loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.True(loaded.ChangeColor);
        Assert.Equal(N3FxDef.NumKeyColor, loaded.ChangeColorKeyCount);
        for (int i = 0; i < N3FxDef.NumKeyColor; i++)
            Assert.Equal((uint)(0xff000000 | i), loaded.ChangeColors[i]);
    }

    [Fact]
    public void Particle_AnimKey_SerializesShapeNameAndFps()
    {
        N3FXPartParticles original = MakeParticles();
        original.AnimKey = true;
        original.MeshFps = 15f;
        original.ShapeFileName = @"fx\swirl.n3fxshape";

        (N3FXPartParticles loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.True(loaded.AnimKey);
        Assert.Equal(15f, loaded.MeshFps);
        Assert.Equal(@"fx\swirl.n3fxshape", loaded.ShapeFileName);
    }

    [Fact]
    public void Particle_V3_ReadsSingleParticleSize()
    {
        // Version < 4 stores one particle size for both min and max.
        N3FXPartParticles original = MakeParticles(version: 3);
        original.ParticleSizeMin = 0.9f;
        original.ParticleSizeMax = 0.9f;

        (N3FXPartParticles loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(0.9f, loaded.ParticleSizeMin);
        Assert.Equal(0.9f, loaded.ParticleSizeMax);
        // version 3 < 6, so the v6+ fields are absent.
        Assert.False(loaded.DistanceNumFix);
    }

    [Fact]
    public void BillBoard_V9_RoundTrips()
    {
        var original = new N3FXPartBillBoard
        {
            Version = 9,
            BaseVersion = 2,
            Type = FxPartType.Board,
            Life = 1f,
            Num = 3,
            SizeX = 2f,
            SizeY = 4f,
            TexLoop = true,
            Radius = 6f,
            RotateOnlyY = true,
            ScaleVelX = 0.1f,
            ScaleVelY = 0.2f,
            ScaleAccelX = 0.3f,
            ScaleAccelY = 0.4f,
            RotationMatrix = Matrix4x4.CreateRotationZ(0.5f),
            OnScreen = true,
            RotationRate = true,
        };

        (N3FXPartBillBoard loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(3, loaded.Num);
        Assert.Equal(2f, loaded.SizeX);
        Assert.Equal(4f, loaded.SizeY);
        Assert.True(loaded.TexLoop);
        Assert.Equal(6f, loaded.Radius);
        Assert.True(loaded.RotateOnlyY);
        Assert.Equal(0.4f, loaded.ScaleAccelY);
        Assert.Equal(original.RotationMatrix, loaded.RotationMatrix);
        Assert.True(loaded.OnScreen);
    }

    [Fact]
    public void BillBoard_V2_OmitsVersionGatedFields()
    {
        var original = new N3FXPartBillBoard
        {
            Version = 2,
            BaseVersion = 2,
            Type = FxPartType.Board,
            Num = 1,
            SizeX = 1f,
            SizeY = 1f,
            Radius = 3f,
        };

        (N3FXPartBillBoard loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(3f, loaded.Radius);
        Assert.False(loaded.RotateOnlyY); // v2 < 3
        Assert.Equal(Matrix4x4.Identity, loaded.RotationMatrix); // v2 < 5
    }

    [Fact]
    public void BottomBoard_V3_RoundTrips()
    {
        var original = new N3FXPartBottomBoard
        {
            Version = 3,
            BaseVersion = 2,
            Type = FxPartType.BottomBoard,
            SizeX = 2f,
            SizeZ = 3f,
            ScaleVelX = 0.5f,
            ScaleVelZ = 0.6f,
            TexLoop = true,
            Gap = 0.1f,
            NewUv = true,
            HdrUv = true,
        };

        (N3FXPartBottomBoard loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(2f, loaded.SizeX);
        Assert.Equal(3f, loaded.SizeZ);
        Assert.Equal(0.6f, loaded.ScaleVelZ);
        Assert.True(loaded.TexLoop);
        Assert.Equal(0.1f, loaded.Gap);
        Assert.True(loaded.NewUv);
        Assert.True(loaded.HdrUv);
    }

    [Fact]
    public void Mesh_V9_RoundTrips()
    {
        var original = new N3FXPartMesh
        {
            Version = 9,
            BaseVersion = 2,
            Type = FxPartType.Mesh,
            ShapeFileName = @"fx\blade.n3fxshape",
            TextureMoveDir = 3,
            TexU = 0.5f,
            TexV = -0.25f,
            ScaleVelocity = new Vector3(1, 2, 3),
            TexLoop = true,
            ScaleAcceleration = new Vector3(0.1f, 0.2f, 0.3f),
            MeshFps = 12f,
            UnitScale = new Vector3(2, 2, 2),
            ShapeLoop = true,
            ViewFix = true,
            UseFadeShowLife = true,
        };

        (N3FXPartMesh loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(@"fx\blade.n3fxshape", loaded.ShapeFileName);
        Assert.Equal((byte)3, loaded.TextureMoveDir);
        Assert.Equal(0.5f, loaded.TexU);
        Assert.Equal(new Vector3(1, 2, 3), loaded.ScaleVelocity);
        Assert.True(loaded.TexLoop);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), loaded.ScaleAcceleration);
        Assert.Equal(12f, loaded.MeshFps);
        Assert.Equal(new Vector3(2, 2, 2), loaded.UnitScale);
        Assert.True(loaded.UseFadeShowLife);
    }

    // ---------------------------------------------------------------- base header versions

    [Fact]
    public void PartBase_VersionLessThan2_ReadsBoolAlphaAndNoRenderFlag()
    {
        var original = new N3FXPartBillBoard
        {
            Version = 1,
            BaseVersion = 1, // < 2: BOOL alpha + blends, no render flag
            Type = FxPartType.Board,
            Alpha = false,
            SrcBlend = 5,
            DestBlend = 6,
            FadeIn = 0.1f,
            FadeOut = 0.2f,
            Num = 1,
            SizeX = 1f,
            SizeY = 1f,
        };

        (N3FXPartBillBoard loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.False(loaded.Alpha);
        Assert.Equal(5u, loaded.SrcBlend);
        Assert.Equal(6u, loaded.DestBlend);
        Assert.Equal(0.1f, loaded.FadeIn);
    }

    [Fact]
    public void PartBase_Version3And4Extras_RoundTrip()
    {
        var original = new N3FXPartBottomBoard
        {
            Version = 3,
            BaseVersion = 4, // >= 3 idk ints + >= 4 shape header block
            Type = FxPartType.BottomBoard,
            BaseVersion3Unknown0 = 111,
            BaseVersion3Unknown1 = 222,
            RenderFlag = 0x1,
            SizeX = 1f,
            SizeZ = 1f,
        };
        original.BaseVersion4ShapeHeaderName[0] = (byte)'X';

        (N3FXPartBottomBoard loaded, long pos, long len) = RoundtripPart(original);

        Assert.Equal(pos, len);
        Assert.Equal(111, loaded.BaseVersion3Unknown0);
        Assert.Equal(222, loaded.BaseVersion3Unknown1);
        Assert.Equal((byte)'X', loaded.BaseVersion4ShapeHeaderName[0]);
        Assert.Equal(260, loaded.BaseVersion4ShapeHeaderName.Length);
    }

    // ---------------------------------------------------------------- bundle

    private static N3FXBundle MakeV2Bundle()
    {
        var bundle = new N3FXBundle
        {
            Version = 2,
            Life0 = 4f,
            Velocity = 1.5f,
            DependScale = true,
            Static = true,
        };

        bundle.Parts[0] = new N3FXBundlePart { StartTime = 0f, Part = MakeParticles() };
        bundle.Parts[1] = new N3FXBundlePart
        {
            StartTime = 0.5f,
            Part = new N3FXPartBillBoard { Version = 9, BaseVersion = 2, Type = FxPartType.Board, Num = 2, SizeX = 1, SizeY = 1 },
        };
        bundle.Parts[3] = new N3FXBundlePart
        {
            StartTime = 1f,
            Part = new N3FXPartMesh { Version = 9, BaseVersion = 2, Type = FxPartType.Mesh, ShapeFileName = "m.n3fxshape", UnitScale = Vector3.One },
        };
        bundle.Parts[7] = new N3FXBundlePart
        {
            StartTime = 1.5f,
            Part = new N3FXPartBottomBoard { Version = 3, BaseVersion = 2, Type = FxPartType.BottomBoard, SizeX = 1, SizeZ = 1 },
        };
        return bundle;
    }

    [Fact]
    public void Bundle_V2_FourPartTypesAndEmptySlots_RoundTrip()
    {
        N3FXBundle original = MakeV2Bundle();

        (N3FXBundle loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len); // v2 reads m_bStatic → exact
        Assert.Equal(2, loaded.Version);
        Assert.Equal(4f, loaded.Life0);
        Assert.Equal(1.5f, loaded.Velocity);
        Assert.True(loaded.DependScale);
        Assert.True(loaded.Static);

        Assert.Equal(N3FxDef.MaxFxPart, loaded.Parts.Length);
        Assert.IsType<N3FXPartParticles>(loaded.Parts[0]!.Part);
        Assert.IsType<N3FXPartBillBoard>(loaded.Parts[1]!.Part);
        Assert.Null(loaded.Parts[2]);
        Assert.IsType<N3FXPartMesh>(loaded.Parts[3]!.Part);
        Assert.IsType<N3FXPartBottomBoard>(loaded.Parts[7]!.Part);

        Assert.Equal(0.5f, loaded.Parts[1]!.StartTime);
        Assert.Equal("m.n3fxshape", ((N3FXPartMesh)loaded.Parts[3]!.Part).ShapeFileName);
        // All remaining slots empty.
        for (int i = 8; i < N3FxDef.MaxFxPart; i++)
            Assert.Null(loaded.Parts[i]);
    }

    [Fact]
    public void Bundle_Life0_ClampedToTen()
    {
        var original = new N3FXBundle { Version = 2, Life0 = 999f };
        (N3FXBundle loaded, _, _) = Roundtrip(original);
        Assert.Equal(10f, loaded.Life0);
    }

    [Fact]
    public void Bundle_V0_HasNoStaticByte_TrailingByteRemains()
    {
        // Version 0 < 2, so Load does not read m_bStatic although Save always
        // writes it — exactly one trailing byte, matching the C++.
        var original = new N3FXBundle { Version = 0, Life0 = 1f, Velocity = 2f, DependScale = true };
        original.Parts[0] = new N3FXBundlePart { StartTime = 0f, Part = MakeParticles() };

        (N3FXBundle loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(1, len - pos); // the unread m_bStatic byte
        Assert.Equal(0, loaded.Version);
        Assert.IsType<N3FXPartParticles>(loaded.Parts[0]!.Part);
    }

    [Fact]
    public void Bundle_V1_ReadsTwentySixSlots()
    {
        var original = new N3FXBundle { Version = 1, Life0 = 1f };
        original.Parts[25] = new N3FXBundlePart
        {
            StartTime = 2f,
            Part = new N3FXPartBottomBoard { Version = 3, BaseVersion = 2, Type = FxPartType.BottomBoard, SizeX = 1, SizeZ = 1 },
        };

        (N3FXBundle loaded, long pos, long len) = Roundtrip(original);

        // v1 < 2, so m_bStatic is unread — one trailing byte.
        Assert.Equal(1, len - pos);
        Assert.IsType<N3FXPartBottomBoard>(loaded.Parts[25]!.Part);
    }

    [Fact]
    public void Bundle_V3_CapturesUnsupportedTail_AndRoundTrips()
    {
        // Version 3 (> SUPPORTED_BUNDLE_VERSION) appends bytes the client ignores;
        // the port captures them verbatim so the file fully round-trips.
        var original = new N3FXBundle
        {
            Version = 3,
            Life0 = 2f,
            Static = true,
            UnsupportedVersionTail = [0, 0, 128, 63, 0xFF],
        };
        original.Parts[0] = new N3FXBundlePart { StartTime = 0f, Part = MakeParticles(version: 7) };

        (N3FXBundle loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(3, loaded.Version);
        Assert.True(loaded.Static);
        Assert.Equal(new byte[] { 0, 0, 128, 63, 0xFF }, loaded.UnsupportedVersionTail);
        Assert.IsType<N3FXPartParticles>(loaded.Parts[0]!.Part);
    }

    [Fact]
    public void Plug_ScaleAndReserved_RoundTrip()
    {
        var original = new N3FXPlug { Name = "p" };
        original.Parts.Add(new N3FXPlugPart
        {
            Name = string.Empty,
            FxbFileName = @"fx\x.fxb",
            RefIndex = 2,
            Scale = 1f,
            Reserved = -1,
        });

        (N3FXPlug loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(1f, loaded.Parts[0].Scale);
        Assert.Equal(-1, loaded.Parts[0].Reserved);
    }

    [Fact]
    public void Bundle_EmptyName_HasNoNameHeader()
    {
        // The bundle has no [len][name] header at all: the first field is version.
        var original = new N3FXBundle { Version = 2 };
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true))
            original.Save(writer);

        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.Latin1);
        Assert.Equal(2, reader.ReadInt32()); // version is the very first thing on disk
    }

    // ---------------------------------------------------------------- group

    [Fact]
    public void Group_RoundTrips()
    {
        var original = new N3FXGroup { Version = 1 };
        original.Bundles.Add(new FxbInfo { Name = @"fx\a.fxb", Joint = 1, IsLooping = true });
        original.Bundles.Add(new FxbInfo { Name = @"fx\b.fxb", Joint = -1, IsLooping = false });

        (N3FXGroup loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(1, loaded.Version);
        Assert.Equal(2, loaded.Bundles.Count);
        Assert.Equal(@"fx\a.fxb", loaded.Bundles[0].Name);
        Assert.Equal(1, loaded.Bundles[0].Joint);
        Assert.True(loaded.Bundles[0].IsLooping);
        Assert.Equal(-1, loaded.Bundles[1].Joint);
        Assert.False(loaded.Bundles[1].IsLooping);
    }

    [Fact]
    public void Group_ZeroBundles_RoundTrips()
    {
        var original = new N3FXGroup { Version = 1 };
        (N3FXGroup loaded, long pos, long len) = Roundtrip(original);
        Assert.Equal(pos, len);
        Assert.Empty(loaded.Bundles);
    }

    // ---------------------------------------------------------------- shape

    [Fact]
    public void Shape_RoundTrips()
    {
        var original = new N3FXShape
        {
            Name = "swirl",
            Position = new Vector3(1, 2, 3),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(2, 2, 2),
            CollisionMeshFileName = "col.n3vmesh",
            ClimbMeshFileName = string.Empty,
        };

        var part = new N3FXShapePart
        {
            Pivot = new Vector3(0.5f, 0, 0),
            MeshFileName = "part0.n3fxpmesh",
            Material = new N3Material { Diffuse = new N3ColorValue { R = 1f, A = 1f }, RenderFlags = 0x1 },
            TexFps = 15f,
        };
        part.TexNames.Add("t0000.dxt");
        part.TexNames.Add("t0001.dxt");
        original.Parts.Add(part);
        original.Attributes[0] = 7;
        original.Attributes[4] = 42;

        (N3FXShape loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal("swirl", loaded.Name);
        Assert.Equal(new Vector3(2, 2, 2), loaded.Scale);
        Assert.Equal("col.n3vmesh", loaded.CollisionMeshFileName);
        Assert.Single(loaded.Parts);
        Assert.Equal("part0.n3fxpmesh", loaded.Parts[0].MeshFileName);
        Assert.Equal(15f, loaded.Parts[0].TexFps);
        Assert.Equal(2, loaded.Parts[0].TexNames.Count);
        Assert.Equal("t0001.dxt", loaded.Parts[0].TexNames[1]);
        Assert.Equal(1f, loaded.Parts[0].Material.Diffuse.R);
        Assert.Equal(7u, loaded.Attributes[0]);
        Assert.Equal(42u, loaded.Attributes[4]);
    }

    // ---------------------------------------------------------------- FX PMesh + instance

    private static N3FXPMesh MakeFxPMesh()
    {
        var mesh = new N3FXPMesh { Name = "fxmesh" };
        N3VertexT1[] verts =
        [
            new() { Position = Vector3.Zero, Normal = Vector3.UnitY, Tu = 0f, Tv = 0f },
            new() { Position = Vector3.UnitX, Normal = Vector3.UnitY, Tu = 1f, Tv = 0f },
            new() { Position = Vector3.UnitZ, Normal = Vector3.UnitY, Tu = 0f, Tv = 1f },
        ];
        ushort[] indices = [0, 1, 2];
        mesh.Initialize(
            verts, indices, minNumVertices: 3, minNumIndices: 3,
            collapses: [], allIndexChanges: [], lodCtrlValues: [new N3PMesh.LodCtrlValue(10f, 3)]);
        return mesh;
    }

    [Fact]
    public void FxPMesh_RoundTrips_AndBuildsColorVertices()
    {
        N3FXPMesh original = MakeFxPMesh();

        (N3FXPMesh loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal(3, loaded.MaxNumVertices);
        Assert.Equal(3, loaded.MaxNumIndices);
        Assert.Single(loaded.LodCtrlValues);

        N3VertexXyzColorT1[] colors = loaded.ColorVertices();
        Assert.Equal(3, colors.Length);
        Assert.Equal(Vector3.UnitX, colors[1].Position);
        Assert.Equal(1f, colors[1].Tu);
        Assert.Equal(0xffffffffu, colors[0].Color);
    }

    [Fact]
    public void FxPMeshInstance_Create_CopiesBuffersAtMinLod()
    {
        N3FXPMesh mesh = MakeFxPMesh();
        var instance = new N3FXPMeshInstance();
        Assert.True(instance.Create(mesh));

        Assert.Equal(3, instance.ColorVertices.Length);
        Assert.Equal(3, instance.Indices.Length);
        Assert.Equal(mesh.MinNumVertices, instance.NumVertices);
        Assert.Equal(mesh.MinNumIndices, instance.NumIndices);

        instance.SetColor(0xff00ff00);
        Assert.Equal(0xff00ff00u, instance.ColorVertices[0].Color);
    }

    [Fact]
    public void FxPMeshInstance_CreateNull_ReturnsFalse()
    {
        var instance = new N3FXPMeshInstance();
        Assert.False(instance.Create(null));
        Assert.Empty(instance.ColorVertices);
    }

    // ---------------------------------------------------------------- FX plug

    [Fact]
    public void Plug_RoundTrips()
    {
        var original = new N3FXPlug { Name = "plug" };
        original.Parts.Add(new N3FXPlugPart
        {
            Name = "p0",
            FxbFileName = @"fx\hit.fxb",
            RefIndex = 3,
            OffsetPos = new Vector3(0, 1, 0),
            OffsetDir = new Vector3(0, 0, 1),
        });
        original.Parts.Add(new N3FXPlugPart
        {
            Name = "p1",
            FxbFileName = string.Empty, // length-0 name: no bundle
            RefIndex = -1,
            OffsetPos = Vector3.Zero,
            OffsetDir = new Vector3(0, 0, 1),
        });

        (N3FXPlug loaded, long pos, long len) = Roundtrip(original);

        Assert.Equal(pos, len);
        Assert.Equal("plug", loaded.Name);
        Assert.Equal(2, loaded.Parts.Count);
        Assert.Equal(@"fx\hit.fxb", loaded.Parts[0].FxbFileName);
        Assert.Equal(3, loaded.Parts[0].RefIndex);
        Assert.Equal(new Vector3(0, 1, 0), loaded.Parts[0].OffsetPos);
        Assert.Equal(string.Empty, loaded.Parts[1].FxbFileName);
        Assert.Equal(-1, loaded.Parts[1].RefIndex);
    }

    [Fact]
    public void Plug_ZeroParts_RoundTrips()
    {
        var original = new N3FXPlug { Name = string.Empty };
        (N3FXPlug loaded, long pos, long len) = Roundtrip(original);
        Assert.Equal(pos, len);
        Assert.Empty(loaded.Parts);
    }
}
