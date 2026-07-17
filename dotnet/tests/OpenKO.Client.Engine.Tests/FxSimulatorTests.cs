using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Fx;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Slice-9.10b pins: the deterministic particle/board/bundle simulation.</summary>
public class FxSimulatorTests
{
    private const float Dt = 0.1f;

    private static N3FXPartParticles StraightEmitter()
    {
        // A spread emitter that fires straight down +Z with no jitter: emit angle 0,
        // zero create range, so every particle spawns at the origin with velocity
        // (0,0,ptVelocity) regardless of the RNG stream.
        return new N3FXPartParticles
        {
            Life = 0f, // infinite emitter
            NumParticle = 10,
            NumCreate = 2,
            CreateDelay = 0.01f,
            ParticleLifeMin = 1f,
            ParticleLifeMax = 1f,
            ParticleSizeMin = 2f,
            ParticleSizeMax = 2f,
            EmitType = FxPartParticleEmitType.Spread,
            EmitCondition = new ParticleEmitCondition { EmitAngle = 0f },
            PtEmitDir = new Vector3(0f, 0f, 1f),
            PtVelocity = 10f,
            PtGravity = 0f,
            PtRotVelocity = 0f,
            NumTex = 1,
            TexFps = 30f,
            FadeIn = 0f,
            FadeOut = 0f,
            MinCreateRange = Vector3.Zero,
            MaxCreateRange = Vector3.Zero,
        };
    }

    [Fact]
    public void Particles_EmitOnStartAndAgeDeterministically()
    {
        var sim = new FxParticleSimulator(StraightEmitter());
        sim.Start();

        FxBundleContext bundle = FxBundleContext.Default;

        sim.Tick(Dt, bundle);
        Assert.Equal(2, sim.AliveCount); // one NumCreate batch

        // First frame: every fresh particle sits at the spawn point.
        foreach (FxRuntimeParticle p in sim.AliveParticles)
        {
            Assert.Equal(new Vector3(0f, 0f, 0f), p.WorldPos);
            Assert.Equal(2f, p.Size); // size range min==max
        }

        sim.Tick(Dt, bundle);
        Assert.Equal(4, sim.AliveCount);

        // The first batch has advanced one step along +Z: localPos = velocity*dt = 1.0.
        FxRuntimeParticle oldest = sim.AliveParticles[0];
        Assert.Equal(1.0f, oldest.WorldPos.Z, 4);
        Assert.Equal(FxColor.White, oldest.Color); // past fade-in, before fade-out
    }

    [Fact]
    public void Particles_PoolIsBoundedAndRecycles()
    {
        var sim = new FxParticleSimulator(StraightEmitter());
        sim.Start();
        FxBundleContext bundle = FxBundleContext.Default;

        // 2 spawned per frame, pool of 10 -> saturates at 10.
        for (int i = 0; i < 20; i++)
            sim.Tick(Dt, bundle);

        Assert.True(sim.AliveCount <= 10);

        // Each particle lives 1.0s (life 1, no fade) = ~10 frames, so with a full
        // pool the count holds near the cap.
        Assert.True(sim.AliveCount >= 8);
    }

    [Fact]
    public void Particles_AreDeterministicAcrossRuns()
    {
        // Same seed -> identical positions/colours; this is what the CI relies on.
        var a = new FxParticleSimulator(StraightEmitter(), seed: 42);
        var b = new FxParticleSimulator(StraightEmitter(), seed: 42);
        a.Start();
        b.Start();
        FxBundleContext bundle = FxBundleContext.Default;

        for (int i = 0; i < 15; i++)
        {
            a.Tick(Dt, bundle);
            b.Tick(Dt, bundle);
        }

        Assert.Equal(a.AliveCount, b.AliveCount);
        for (int i = 0; i < a.AliveCount; i++)
        {
            Assert.Equal(a.AliveParticles[i].WorldPos, b.AliveParticles[i].WorldPos);
            Assert.Equal(a.AliveParticles[i].Color, b.AliveParticles[i].Color);
        }
    }

    [Fact]
    public void ParticleVertexBuilder_MakesCameraFacingQuad()
    {
        var p = new FxRuntimeParticle
        {
            WorldPos = new Vector3(0f, 0f, 0f),
            Size = 2f,
            VertexCurrLife = 0f,
            Color = FxColor.White,
        };

        var verts = new N3VertexXyzColorT1[FxParticleVertexBuilder.VerticesPerParticle];
        FxParticleVertexBuilder.BuildParticle(
            p, Matrix4x4.Identity, texRotateVelocity: 0f, scaleVelX: 0f, scaleVelY: 0f, verts, 0);

        // unit * size, no rotation, no offset.
        Assert.Equal(new Vector3(-1f, 1f, 0f), verts[0].Position);
        Assert.Equal(new Vector3(1f, 1f, 0f), verts[1].Position);
        Assert.Equal(new Vector3(1f, -1f, 0f), verts[2].Position);
        Assert.Equal(new Vector3(-1f, -1f, 0f), verts[3].Position);

        Assert.Equal(0f, verts[0].Tu);
        Assert.Equal(0f, verts[0].Tv);
        Assert.Equal(1f, verts[2].Tu);
        Assert.Equal(1f, verts[2].Tv);
        Assert.Equal(FxColor.White, verts[0].Color);
    }

    [Fact]
    public void ParticleVertexBuilder_TranslatesByWorldPosAndScale()
    {
        var p = new FxRuntimeParticle
        {
            WorldPos = new Vector3(10f, 5f, -3f),
            Size = 4f,
            VertexCurrLife = 0f,
            Color = FxColor.White,
        };

        N3VertexXyzColorT1[] verts = FxParticleVertexBuilder.Build(
            [p], Matrix4x4.Identity, 0f, 0f, 0f);

        // Corner 0 = (-0.5,0.5,0)*4 + worldPos.
        Assert.Equal(new Vector3(10f - 2f, 5f + 2f, -3f), verts[0].Position);
        Assert.Equal(4, verts.Length);
    }

    [Fact]
    public void Billboard_TicksTextureFrameAndFade()
    {
        var desc = new N3FXPartBillBoard
        {
            Life = 10f,
            FadeIn = 0f,
            FadeOut = 0f,
            Num = 1,
            SizeX = 1f,
            SizeY = 1f,
            TexLoop = false,
            NumTex = 4,
            TexFps = 30f,
        };
        var sim = new FxBillboardSimulator(desc);
        sim.Start();

        Assert.True(sim.Tick(0.1f));
        Assert.Equal(3, sim.TexIndex); // (int)(0.1*30)
        Assert.Equal(FxColor.White, sim.CurrColor); // fadeIn 0 -> opaque immediately past 0
    }

    [Fact]
    public void BottomBoard_BuildsTenVertexFanSnappedToGround()
    {
        var desc = new N3FXPartBottomBoard
        {
            Life = 10f,
            FadeIn = 0f,
            FadeOut = 0f,
            SizeX = 2f,
            SizeZ = 2f,
            Gap = 0f,
        };
        // A non-flat ground so the recompute is observable.
        var sim = new FxBottomBoardSimulator(desc, groundHeight: (_, _) => 5f);
        sim.Start();

        Assert.True(sim.Tick(0.1f, FxBundleContext.Default));
        Assert.Equal(10, sim.Vertices.Count);

        // Every fan vertex is projected onto the ground (+gap 0).
        foreach (N3VertexXyzColorT1 v in sim.Vertices)
            Assert.Equal(5f, v.Position.Y, 4);

        Assert.Equal(FxColor.White, sim.Vertices[0].Color);

        short[] idx = FxBottomBoardSimulator.FanIndices();
        Assert.Equal(24, idx.Length); // 8 triangles
    }

    [Fact]
    public void Bundle_ActivatesPartsByStartTimeAndDiesWhenAllDead()
    {
        var bundle = new N3FXBundle();
        bundle.Parts[0] = new N3FXBundlePart
        {
            StartTime = 0f,
            Part = new N3FXPartBillBoard { Type = FxPartType.Board, Life = 1f, FadeIn = 0f, FadeOut = 0f, NumTex = 1 },
        };
        bundle.Parts[1] = new N3FXBundlePart
        {
            StartTime = 0.5f,
            Part = new N3FXPartBillBoard { Type = FxPartType.Board, Life = 1f, FadeIn = 0f, FadeOut = 0f, NumTex = 1 },
        };

        var sim = new FxBundleSimulator(bundle);
        sim.Trigger();
        Assert.Equal(FxBundleState.Live, sim.State);

        // First frame (life 0.1): part 0 starts, part 1 still waiting.
        sim.Tick(Dt);
        Assert.NotEqual(FxPartLifeState.Ready, sim.Parts[0].Part.State);
        Assert.Equal(FxPartLifeState.Ready, sim.Parts[1].Part.State);

        // By life ~0.6, part 1 has started too.
        for (int i = 0; i < 5; i++)
            sim.Tick(Dt);
        Assert.NotEqual(FxPartLifeState.Ready, sim.Parts[1].Part.State);

        // Run long enough for both boards to finish; the bundle then retires.
        bool alive = true;
        for (int i = 0; i < 40 && alive; i++)
            alive = sim.Tick(Dt);

        Assert.Equal(FxBundleState.Dead, sim.State);
    }

    [Fact]
    public void ParticleColorKey_DrivesColourOverLife()
    {
        var desc = StraightEmitter();
        desc.ChangeColor = true;
        for (int i = 0; i < desc.ChangeColors.Length; i++)
            desc.ChangeColors[i] = 0xff000000u | (uint)i; // distinct per key

        var sim = new FxParticleSimulator(desc);
        sim.Start();
        FxBundleContext bundle = FxBundleContext.Default;

        sim.Tick(Dt, bundle); // spawn + first age; currLife was 0 at colour time -> key 0
        FxRuntimeParticle p = sim.AliveParticles[0];
        Assert.Equal(0xff000000u, p.Color);

        // After several frames the bucket index climbs (life 1, so idx = currLife*100).
        for (int i = 0; i < 3; i++)
            sim.Tick(Dt, bundle);
        FxRuntimeParticle q = sim.AliveParticles[0];
        Assert.True((q.Color & 0xffu) >= 30u); // ~0.3s in -> key ~30
    }
}
