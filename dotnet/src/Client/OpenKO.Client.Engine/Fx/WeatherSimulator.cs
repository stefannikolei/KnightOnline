using System.Numerics;

namespace OpenKO.Client.Engine.Fx;

/// <summary>e_WeatherType (shared/packets.h) — the WIZ_WEATHER type byte.</summary>
public enum WeatherType
{
    None = 0,
    Fine = 1,
    Rain = 2,
    Snow = 3,
}

/// <summary>
/// A rain streak: a <see cref="Tail"/> point and the <see cref="Head"/> a fixed
/// distance up the velocity vector (the two ends of a <c>D3DPT_LINELIST</c>
/// segment in <c>CN3GERain</c>).
/// </summary>
public struct WeatherRainParticle
{
    public Vector3 Tail;
    public Vector3 Head;
}

/// <summary>
/// A snowflake: its wandering centre (<see cref="Pos"/>), the three triangle
/// corner offsets and the swirl angle (<see cref="Radian"/>). The rendered
/// triangle corners are <see cref="V1"/>/<see cref="V2"/>/<see cref="V3"/>,
/// recomputed each <see cref="WeatherSimulator.Update"/> from a unit-circle
/// wobble about <see cref="Pos"/> (the <c>__SnowParticle</c> of <c>CN3GESnow</c>).
/// </summary>
public struct WeatherSnowParticle
{
    public Vector3 Pos;
    public Vector3 Offset1;
    public Vector3 Offset2;
    public Vector3 Offset3;
    public float Radian;
    public float Radius;

    public Vector3 V1;
    public Vector3 V2;
    public Vector3 V3;
}

/// <summary>
/// Pure port of the global weather effects <c>CN3GERain</c> and <c>CN3GESnow</c>:
/// a camera-centred particle field of fast downward rain streaks or slow drifting
/// snow flakes. The field is a box of <see cref="Width"/> × <see cref="Height"/> ×
/// <see cref="Width"/>; particles that fall out of the vertical band recentre
/// around the camera's Y (and, for the horizontal edges, wrap within the box), so
/// the field follows the camera. The XZ of the exposed particles are field-local
/// (centred on origin) — the device <c>WeatherRenderer</c> places the box at the
/// camera XZ via its world matrix, exactly like the C++ <c>m_Matrix</c>.
/// <para>
/// Determinism: every jitter draws from the seedable <see cref="FxRandom"/> (the
/// same MSVC <c>rand()</c> LCG the particle sim uses), so a fixed seed yields a
/// bit-reproducible field with no wall-clock or <c>System.Random</c>. Headless —
/// no GraphicsDevice.
/// </para>
/// </summary>
public sealed class WeatherSimulator
{
    /// <summary>Rain streak tail colour (0x00bfbfbf — transparent grey).</summary>
    public const uint RainTailColor = 0x00bfbfbfu;

    /// <summary>Rain streak head colour (0x80bfbfbf — half-alpha grey).</summary>
    public const uint RainHeadColor = 0x80bfbfbfu;

    private static readonly float Sqrt3 = MathF.Sqrt(3.0f);

    private readonly List<WeatherRainParticle> _rain = [];
    private readonly List<WeatherSnowParticle> _snow = [];

    private FxRandom _rng;

    public WeatherSimulator(uint seed = 0x1234u) => _rng = new FxRandom(seed);

    public WeatherType Type { get; private set; } = WeatherType.None;

    /// <summary>The WIZ_WEATHER intensity (0..100) the field was created with.</summary>
    public int Amount { get; private set; }

    /// <summary>m_fWidth / m_fCellSize — the box footprint (X and Z).</summary>
    public float Width { get; private set; }

    /// <summary>m_fHeight — the box height.</summary>
    public float Height { get; private set; }

    /// <summary>m_fRainLength (rain) — the streak length.</summary>
    public float RainLength { get; private set; }

    /// <summary>m_fSnowSize (snow) — the flake triangle size.</summary>
    public float SnowSize { get; private set; }

    /// <summary>m_vVelocity — the per-second drift (down + a little sideways).</summary>
    public Vector3 Velocity { get; private set; }

    public IReadOnlyList<WeatherRainParticle> RainParticles => _rain;

    public IReadOnlyList<WeatherSnowParticle> SnowParticles => _snow;

    /// <summary>True when the field carries live particles to advance/draw.</summary>
    public bool Active => Type is WeatherType.Rain or WeatherType.Snow && (_rain.Count > 0 || _snow.Count > 0);

    /// <summary>
    /// CN3SkyMng::SetWeather — (re)create the field from the wire's type + intensity
    /// percentage, using the shipped defaults (20×20×20 box, density = pct·0.03,
    /// the rain/snow velocities and streak/flake sizes scaled by the percentage).
    /// Fine/None clears the field.
    /// </summary>
    public void Create(WeatherType type, int amountPercent)
    {
        Type = type;
        Amount = amountPercent;

        if (type is not (WeatherType.Rain or WeatherType.Snow))
        {
            Clear();
            return;
        }

        float pct = Math.Clamp(amountPercent, 0, 100) / 100.0f;
        const float cellSize = 20.0f;
        const float height = 20.0f;
        float density = pct * 0.03f;

        if (type == WeatherType.Rain)
        {
            // vVelocity(3*((50-rand%100)/50), -(10 + 8*pct), 0); rainLength 0.4 + 0.6*pct.
            float horz = 3.0f * ((50 - _rng.NextMod(100)) / 50.0f);
            var velocity = new Vector3(horz, -(10.0f + (8.0f * pct)), 0f);
            CreateRain(density, cellSize, height, 0.4f + (0.6f * pct), velocity);
        }
        else
        {
            // fHorz = 3*pct + 3*((50-rand%100)/50); vVelocity(fHorz, -(2 + 2*pct), 0); snowSize 0.1 + 0.1*pct.
            float horz = (3.0f * pct) + (3.0f * ((50 - _rng.NextMod(100)) / 50.0f));
            var velocity = new Vector3(horz, -(2.0f + (2.0f * pct)), 0f);
            CreateSnow(density, cellSize, height, 0.1f + (0.1f * pct), velocity);
        }
    }

    /// <summary>CN3GERain::Create — explicit field parameters (used by tests).</summary>
    public void CreateRain(float density, float width, float height, float rainLength, Vector3 velocity)
    {
        Type = WeatherType.Rain;
        Width = width;
        Height = height;
        RainLength = rainLength;
        Velocity = velocity;
        _rain.Clear();
        _snow.Clear();

        int count = (int)(width * width * height * density);
        Vector3 add = SafeNormalize(velocity) * rainLength;
        for (int i = 0; i < count; i++)
        {
            var tail = new Vector3(
                width * RandCentered(), height * RandCentered(), width * RandCentered());
            _rain.Add(new WeatherRainParticle { Tail = tail, Head = tail + add });
        }
    }

    /// <summary>CN3GESnow::Create — explicit field parameters (used by tests).</summary>
    public void CreateSnow(float density, float width, float height, float snowSize, Vector3 velocity)
    {
        Type = WeatherType.Snow;
        Width = width;
        Height = height;
        SnowSize = snowSize;
        Velocity = velocity;
        _rain.Clear();
        _snow.Clear();

        int count = (int)(width * width * height * density);
        for (int i = 0; i < count; i++)
        {
            var p = new WeatherSnowParticle
            {
                Pos = new Vector3(width * RandCentered(), height * RandCentered(), width * RandCentered()),
                Radius = RandUnit(),
                Radian = 2.0f * MathF.PI * RandUnit(),
            };

            // Create uses the isosceles-triangle offsets (the shipped path); the
            // Update-on-wrap path below rebuilds them with the sqrt3 variant.
            float triRadian = MathF.PI * RandUnit();
            p.Offset1 = new Vector3(0f, snowSize / 2.0f, 0f);
            p.Offset2 = new Vector3(MathF.Cos(triRadian) * snowSize / 2.0f, -snowSize / 2.0f, MathF.Sin(triRadian) * snowSize / 2.0f);
            p.Offset3 = new Vector3(-MathF.Cos(triRadian) * snowSize / 2.0f, -snowSize / 2.0f, -MathF.Sin(triRadian) * snowSize / 2.0f);
            ComputeSnowVerts(ref p);
            _snow.Add(p);
        }
    }

    public void Clear()
    {
        _rain.Clear();
        _snow.Clear();
        if (Type is not (WeatherType.Rain or WeatherType.Snow))
        {
            Width = Height = RainLength = SnowSize = 0f;
            Velocity = Vector3.Zero;
        }
    }

    /// <summary>
    /// CN3GERain::Tick / CN3GESnow::Tick — advance the field by one frame and
    /// recentre any particle that fell out of the vertical band around
    /// <paramref name="cameraPos"/>.Y (rain re-randomises XZ on the vertical wrap;
    /// both wrap XZ within the box on the horizontal edges).
    /// </summary>
    public void Update(float secPerFrame, Vector3 cameraPos)
    {
        if (Type == WeatherType.Rain)
            UpdateRain(secPerFrame, cameraPos.Y);
        else if (Type == WeatherType.Snow)
            UpdateSnow(secPerFrame, cameraPos.Y);
    }

    private void UpdateRain(float secPerFrame, float cameraY)
    {
        if (_rain.Count == 0)
            return;

        Vector3 add = Velocity * secPerFrame;
        Vector3 addLength = SafeNormalize(Velocity) * RainLength;
        float halfW = Width / 2.0f;
        float halfH = Height / 2.0f;

        for (int i = 0; i < _rain.Count; i++)
        {
            WeatherRainParticle p = _rain[i];
            Vector3 tail = p.Tail + add;

            float diff = tail.Y - (cameraY - halfH);
            if (diff < 0f)
            {
                tail.Y -= ((int)(diff / Height) - 1) * Height;
                tail.X = Width * RandCentered();
                tail.Z = Width * RandCentered();
            }
            else
            {
                diff = tail.Y - (cameraY + halfH);
                if (diff > 0f)
                    tail.Y -= ((int)(diff / Height) + 1) * Height;
                WrapHorizontal(ref tail, halfW);
            }

            p.Tail = tail;
            p.Head = tail + addLength;
            _rain[i] = p;
        }
    }

    private void UpdateSnow(float secPerFrame, float cameraY)
    {
        if (_snow.Count == 0)
            return;

        Vector3 add = Velocity * secPerFrame;
        float addRadian = MathF.PI * secPerFrame * 0.1f;
        float halfW = Width / 2.0f;
        float halfH = Height / 2.0f;

        for (int i = 0; i < _snow.Count; i++)
        {
            WeatherSnowParticle p = _snow[i];
            p.Pos += add;

            float diff = p.Pos.Y - (cameraY - halfH);
            if (diff < 0f)
            {
                p.Pos.Y -= ((int)(diff / Height) - 1) * Height;
                p.Pos.X = Width * RandCentered();
                p.Pos.Z = Width * RandCentered();

                p.Radius = RandUnit();
                p.Radian = 2.0f * MathF.PI * RandUnit();

                float triRadian = MathF.PI * RandUnit();
                p.Offset1 = new Vector3(0f, Sqrt3 * SnowSize / 3.0f, 0f);
                p.Offset2 = new Vector3(MathF.Cos(triRadian) * SnowSize / 2.0f, -Sqrt3 * SnowSize / 6.0f, MathF.Sin(triRadian) * SnowSize / 2.0f);
                p.Offset3 = new Vector3(-MathF.Cos(triRadian) * SnowSize / 2.0f, -Sqrt3 * SnowSize / 6.0f, -MathF.Sin(triRadian) * SnowSize / 2.0f);
            }
            else
            {
                diff = p.Pos.Y - (cameraY + halfH);
                if (diff > 0f)
                    p.Pos.Y -= ((int)(diff / Height) + 1) * Height;
                Vector3 pos = p.Pos;
                WrapHorizontal(ref pos, halfW);
                p.Pos = pos;
            }

            p.Radian += addRadian;
            ComputeSnowVerts(ref p);
            _snow[i] = p;
        }
    }

    private static void ComputeSnowVerts(ref WeatherSnowParticle p)
    {
        var wobble = new Vector3(MathF.Cos(p.Radian), 0f, MathF.Sin(p.Radian)) + p.Pos;
        p.V1 = wobble + p.Offset1;
        p.V2 = wobble + p.Offset2;
        p.V3 = wobble + p.Offset3;
    }

    private static void WrapHorizontal(ref Vector3 v, float halfW)
    {
        float width = halfW * 2.0f;
        float diff = v.X - halfW;
        if (diff > 0f)
            v.X -= ((int)(diff / width) + 1) * width;
        diff = v.X + halfW;
        if (diff < 0f)
            v.X -= ((int)(diff / width) - 1) * width;
        diff = v.Z - halfW;
        if (diff > 0f)
            v.Z -= ((int)(diff / width) + 1) * width;
        diff = v.Z + halfW;
        if (diff < 0f)
            v.Z -= ((int)(diff / width) - 1) * width;
    }

    /// <summary>The C++ <c>(rand()%10000-5000)/10000.f</c> — a value in [-0.5, 0.5).</summary>
    private float RandCentered() => (_rng.NextMod(10000) - 5000) / 10000.0f;

    /// <summary>The C++ <c>(rand()%10000)/10000.f</c> — a value in [0, 1).</summary>
    private float RandUnit() => _rng.NextMod(10000) / 10000.0f;

    private static Vector3 SafeNormalize(Vector3 v)
    {
        float len = v.Length();
        return len > 0f ? v / len : v;
    }
}
