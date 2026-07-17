using System.Numerics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Pure port of <c>CN3FXPartBottomBoard</c> (N3FXPartBottomBoard.cpp) — a
/// ground-hugging <c>NUM_VERTEX_BOTTOM=10</c> triangle-fan decal. <see cref="Tick"/>
/// reproduces the C++ Tick exactly: fade colour + texture frame, position drift,
/// a Y-axis spin, animated X/Z size, then it rewrites all ten vertices, snapping
/// each to the terrain height (+gap) whenever its X/Z moved. The result is a
/// finished vertex array the device layer draws as a fan.
/// </summary>
public sealed class FxBottomBoardSimulator : IFxPart
{
    bool IFxPart.Advance(float secPerFrame, FxBundleContext bundle, float? cameraDistance)
        => Tick(secPerFrame, bundle);

    /// <summary>NUM_VERTEX_BOTTOM.</summary>
    public const int VertexCount = N3FxDef.NumVertexBottom;

    // m_vUnit (the fan template, CN3FXPartBottomBoard::CreateVB).
    private static readonly Vector3[] UnitTemplate =
    [
        new(0f, 0.1f, 0f),      // 0 center
        new(-0.5f, 0.1f, -0.5f), // 1 (== 9)
        new(-0.5f, 0.1f, 0f),   // 2
        new(-0.5f, 0.1f, 0.5f), // 3
        new(0f, 0.1f, 0.5f),    // 4
        new(0.5f, 0.1f, 0.5f),  // 5
        new(0.5f, 0.1f, 0f),    // 6
        new(0.5f, 0.1f, -0.5f), // 7
        new(0f, 0.1f, -0.5f),   // 8
        new(-0.5f, 0.1f, -0.5f), // 9
    ];

    // The UVs from CreateVB (persist across ticks; Tick only rewrites positions).
    private static readonly Vector2[] Uvs =
    [
        new(0.5f, 0.5f), // 0
        new(0f, 1f),     // 1
        new(0f, 0.5f),   // 2
        new(0f, 0f),     // 3
        new(0.5f, 0f),   // 4
        new(1f, 0f),     // 5
        new(1f, 0.5f),   // 6
        new(1f, 1f),     // 7
        new(0.5f, 1f),   // 8
        new(0f, 1f),     // 9
    ];

    private readonly N3FXPartBottomBoard _desc;
    private readonly FxPartState _state;
    private readonly N3VertexXyzColorT1[] _vertices = new N3VertexXyzColorT1[VertexCount];

    private Vector3 _currVelocity;
    private Vector3 _currPos;
    private float _currScaleVelX;
    private float _currScaleVelZ;
    private float _currSizeX;
    private float _currSizeZ;

    public FxBottomBoardSimulator(N3FXPartBottomBoard desc, Func<float, float, float>? groundHeight = null)
    {
        _desc = desc;
        GroundHeight = groundHeight ?? (static (_, _) => 0.01f);
        _state = new FxPartState(desc.Life, desc.FadeIn, IsDead);
        Init();
    }

    /// <summary>The loaded bottom-board description this simulator runs.</summary>
    public N3FXPartBottomBoard Descriptor => _desc;

    /// <summary>GetGroundHeight — terrain sampler (base returns 0.01).</summary>
    public Func<float, float, float> GroundHeight { get; }

    public uint CurrColor { get; private set; } = FxColor.White;

    public int TexIndex { get; private set; }

    public FxPartLifeState State => _state.State;

    /// <summary>The finished fan (10 vertices); valid after a Tick returns true.</summary>
    public IReadOnlyList<N3VertexXyzColorT1> Vertices => _vertices;

    public void Start() => _state.Start();

    public void Rearm() => _state.Rearm();

    public void Stop() => _state.Stop();

    /// <summary>CN3FXPartBottomBoard::Init + CreateVB seed.</summary>
    public void Init()
    {
        TexIndex = 0;
        CurrColor = FxColor.White;
        _currPos = _desc.Pos;
        _currVelocity = _desc.Velocity;
        _currScaleVelX = _desc.ScaleVelX;
        _currScaleVelZ = _desc.ScaleVelZ;
        _currSizeX = _desc.SizeX;
        _currSizeZ = _desc.SizeZ;

        // Seed the vertex Y from the ground (CreateVB); positions/UVs get rewritten.
        for (int i = 0; i < VertexCount; i++)
        {
            _vertices[i] = new N3VertexXyzColorT1
            {
                Position = new Vector3(UnitTemplate[i].X, GroundHeight(UnitTemplate[i].X, UnitTemplate[i].Z), UnitTemplate[i].Z),
                Color = FxColor.White,
                Tu = Uvs[i].X,
                Tv = Uvs[i].Y,
            };
        }
    }

    private bool IsDead() => _state.CurrLife >= _desc.FadeIn + _desc.Life + _desc.FadeOut;

    /// <summary>CN3FXPartBottomBoard::Tick — full port, rewrites the fan in place.</summary>
    public bool Tick(float secPerFrame, FxBundleContext bundle)
    {
        if (!_state.Tick(secPerFrame))
            return false;

        float currLife = _state.CurrLife;

        TexIndex = _desc.NumTex > 0
            ? (_desc.TexLoop ? (int)(currLife * _desc.TexFps) % _desc.NumTex : (int)(currLife * _desc.TexFps))
            : 0;

        CurrColor = FxColor.BoardFade(
            currLife, _desc.FadeIn, _desc.Life, _desc.FadeOut, _state.State == FxPartLifeState.Dying);

        _currVelocity += _desc.Acceleration * secPerFrame;
        _currPos += _currVelocity * secPerFrame;

        Matrix4x4 mtxRot = Matrix4x4.CreateRotationY(currLife * _desc.RotVelocity.Y);

        // m_fScaleAccel* is never loaded (stays 0), so the size velocity is constant.
        _currSizeX += _currScaleVelX * secPerFrame;
        _currSizeZ += _currScaleVelZ * secPerFrame;
        if (_currSizeX < 0f)
            _currSizeX = 0f;
        if (_currSizeZ < 0f)
            _currSizeZ = 0f;

        for (int i = 0; i < VertexCount; i++)
        {
            N3VertexXyzColorT1 prev = _vertices[i];
            float scaleX = _currSizeX * (bundle.DependScale ? bundle.TargetScale : 1f);
            float scaleZ = _currSizeZ * (bundle.DependScale ? bundle.TargetScale : 1f);

            var p = new Vector3(UnitTemplate[i].X * scaleX, prev.Position.Y, UnitTemplate[i].Z * scaleZ);
            p = Vector3.Transform(p, mtxRot);
            p.X += bundle.Pos.X + _currPos.X;
            p.Z += bundle.Pos.Z + _currPos.Z;

            float y = (prev.Position.X != p.X || prev.Position.Z != p.Z)
                ? GroundHeight(p.X, p.Z) + _desc.Gap
                : prev.Position.Y;

            _vertices[i] = new N3VertexXyzColorT1
            {
                Position = new Vector3(p.X, y, p.Z),
                Color = CurrColor,
                Tu = prev.Tu,
                Tv = prev.Tv,
            };
        }

        return true;
    }

    /// <summary>Triangle-list indices for the 10-vertex fan (8 triangles).</summary>
    public static short[] FanIndices()
    {
        var indices = new short[8 * 3];
        for (int t = 0; t < 8; t++)
        {
            indices[(t * 3) + 0] = 0;
            indices[(t * 3) + 1] = (short)(t + 1);
            indices[(t * 3) + 2] = (short)(t + 2);
        }

        return indices;
    }
}
