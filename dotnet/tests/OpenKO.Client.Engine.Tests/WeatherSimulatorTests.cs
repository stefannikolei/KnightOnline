using System.Numerics;
using OpenKO.Client.Engine.Fx;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Slice-9.10c pins: the deterministic weather field (CN3GERain / CN3GESnow).</summary>
public class WeatherSimulatorTests
{
    private const float Dt = 0.1f;

    [Fact]
    public void CreateRain_ProducesDeterministicStreakCountAndHeadsOffset()
    {
        var a = new WeatherSimulator(seed: 0x1234u);
        var b = new WeatherSimulator(seed: 0x1234u);

        // volume = 10*10*10 = 1000, density 0.05 => 50 streaks.
        var velocity = new Vector3(0f, -10f, 0f);
        a.CreateRain(density: 0.05f, width: 10f, height: 10f, rainLength: 0.5f, velocity);
        b.CreateRain(density: 0.05f, width: 10f, height: 10f, rainLength: 0.5f, velocity);

        Assert.Equal(50, a.RainParticles.Count);

        // Same seed => bit-identical field.
        for (int i = 0; i < a.RainParticles.Count; i++)
        {
            Assert.Equal(a.RainParticles[i].Tail, b.RainParticles[i].Tail);
            Assert.Equal(a.RainParticles[i].Head, b.RainParticles[i].Head);
        }

        // Head is tail + normalize(velocity)*rainLength => 0.5 straight down.
        WeatherRainParticle p = a.RainParticles[0];
        Assert.Equal(p.Tail.Y - 0.5f, p.Head.Y, 4);
        Assert.Equal(p.Tail.X, p.Head.X, 4);
        Assert.Equal(p.Tail.Z, p.Head.Z, 4);
    }

    [Fact]
    public void CreateRain_KeepsTailsInsideTheBox()
    {
        var sim = new WeatherSimulator(seed: 7u);
        sim.CreateRain(density: 0.05f, width: 10f, height: 10f, rainLength: 0.5f, new Vector3(0f, -10f, 0f));

        foreach (WeatherRainParticle p in sim.RainParticles)
        {
            Assert.InRange(p.Tail.X, -5f, 5f);
            Assert.InRange(p.Tail.Y, -5f, 5f);
            Assert.InRange(p.Tail.Z, -5f, 5f);
        }
    }

    [Fact]
    public void UpdateRain_FollowsTheCameraByRecentringTailsIntoTheVerticalBand()
    {
        var sim = new WeatherSimulator(seed: 3u);
        // Straight-down rain, height band 10 (±5 around the camera Y).
        sim.CreateRain(density: 0.05f, width: 10f, height: 10f, rainLength: 0.5f, new Vector3(0f, -10f, 0f));

        // Camera high above the initial field: every tail must wrap back into
        // [camY-5, camY+5] within a couple of frames.
        var camera = new Vector3(0f, 100f, 0f);
        for (int i = 0; i < 20; i++)
            sim.Update(Dt, camera);

        foreach (WeatherRainParticle p in sim.RainParticles)
            Assert.InRange(p.Tail.Y, 95f - 0.001f, 105f + 0.001f);
    }

    [Fact]
    public void UpdateRain_MovesTailsDownByVelocityTimesDt()
    {
        var sim = new WeatherSimulator(seed: 11u);
        sim.CreateRain(density: 0.05f, width: 10f, height: 10f, rainLength: 0.5f, new Vector3(0f, -10f, 0f));

        WeatherRainParticle before = sim.RainParticles[0];
        // Camera centred on the box so no wrap on the first small step.
        sim.Update(Dt, Vector3.Zero);
        WeatherRainParticle after = sim.RainParticles[0];

        // -10 * 0.1 = -1.0 down, no horizontal drift.
        Assert.Equal(before.Tail.Y - 1.0f, after.Tail.Y, 4);
        Assert.Equal(before.Tail.X, after.Tail.X, 4);
    }

    [Fact]
    public void CreateSnow_ProducesDeterministicFlakesWithTriangleVerts()
    {
        var a = new WeatherSimulator(seed: 0x1234u);
        var b = new WeatherSimulator(seed: 0x1234u);

        a.CreateSnow(density: 0.05f, width: 10f, height: 10f, snowSize: 0.2f, new Vector3(0f, -2f, 0f));
        b.CreateSnow(density: 0.05f, width: 10f, height: 10f, snowSize: 0.2f, new Vector3(0f, -2f, 0f));

        Assert.Equal(50, a.SnowParticles.Count);
        Assert.Equal(WeatherType.Snow, a.Type);

        for (int i = 0; i < a.SnowParticles.Count; i++)
            Assert.Equal(a.SnowParticles[i].Pos, b.SnowParticles[i].Pos);

        // The three triangle corners are the wobble centre + the three offsets.
        WeatherSnowParticle p = a.SnowParticles[0];
        var wobble = new Vector3(MathF.Cos(p.Radian), 0f, MathF.Sin(p.Radian)) + p.Pos;
        Assert.Equal(wobble + p.Offset1, p.V1);
        Assert.Equal(wobble + p.Offset2, p.V2);
        Assert.Equal(wobble + p.Offset3, p.V3);
    }

    [Fact]
    public void UpdateSnow_FollowsCameraAndAdvancesTheSwirl()
    {
        var sim = new WeatherSimulator(seed: 5u);
        sim.CreateSnow(density: 0.05f, width: 10f, height: 10f, snowSize: 0.2f, new Vector3(0.5f, -2f, 0f));

        float radianBefore = sim.SnowParticles[0].Radian;
        var camera = new Vector3(0f, 50f, 0f);
        for (int i = 0; i < 40; i++)
            sim.Update(Dt, camera);

        foreach (WeatherSnowParticle p in sim.SnowParticles)
            Assert.InRange(p.Pos.Y, 45f - 0.001f, 55f + 0.001f);

        // The swirl angle advanced (PI*dt*0.1 per frame).
        Assert.NotEqual(radianBefore, sim.SnowParticles[0].Radian);
    }

    [Fact]
    public void Create_FineClearsTheField()
    {
        var sim = new WeatherSimulator();
        sim.CreateRain(density: 0.05f, width: 10f, height: 10f, rainLength: 0.5f, new Vector3(0f, -10f, 0f));
        Assert.True(sim.Active);

        sim.Create(WeatherType.Fine, 0);
        Assert.False(sim.Active);
        Assert.Empty(sim.RainParticles);
    }

    [Fact]
    public void Create_FromWirePercentSizesTheField()
    {
        var sim = new WeatherSimulator(seed: 9u);
        // pct 1.0 => density 0.03, box 20*20*20 = 8000 => 240 streaks.
        sim.Create(WeatherType.Rain, 100);
        Assert.Equal(WeatherType.Rain, sim.Type);
        Assert.Equal(240, sim.RainParticles.Count);
    }
}
