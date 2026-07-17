using System.Numerics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Pure port of <c>CN3FXPartBillBoard</c> (N3FXPartBillBoard.cpp) — a
/// camera-facing quad part. <see cref="Tick"/> advances the board's fade colour,
/// texture frame, position, and animated size, and pre-scales/rotates the unit
/// quad corners by the loaded rotation matrix (the C++ <c>m_vUnit</c> update).
/// <see cref="Build"/> reproduces <c>Render</c>: place the quad at the emitter,
/// offset it toward the camera by the board radius, spin it about Z, and make it
/// face the camera via the inverse-view rotation. The device draw is
/// <c>FxRenderer</c>.
/// </summary>
public sealed class FxBillboardSimulator : IFxPart
{
    bool IFxPart.Advance(float secPerFrame, FxBundleContext bundle, float? cameraDistance)
        => Tick(secPerFrame);

    private static readonly Vector3[] BaseUnit =
    [
        new(-0.5f, 0.5f, 0f),
        new(0.5f, 0.5f, 0f),
        new(0.5f, -0.5f, 0f),
        new(-0.5f, -0.5f, 0f),
    ];

    private static readonly Vector2[] Uvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private readonly N3FXPartBillBoard _desc;
    private readonly FxPartState _state;
    private readonly Vector3[] _unit = new Vector3[4];

    private Vector3 _currVelocity;
    private Vector3 _currPos;
    private float _currScaleVelX;
    private float _currScaleVelY;
    private float _currSizeX;
    private float _currSizeY;

    public FxBillboardSimulator(N3FXPartBillBoard desc)
    {
        _desc = desc;
        _state = new FxPartState(desc.Life, desc.FadeIn, IsDead);
        Init();
    }

    /// <summary>The loaded billboard description this simulator runs.</summary>
    public N3FXPartBillBoard Descriptor => _desc;

    /// <summary>m_dwCurrColor (D3DCOLOR).</summary>
    public uint CurrColor { get; private set; } = FxColor.White;

    public int TexIndex { get; private set; }

    public FxPartLifeState State => _state.State;

    public void Start() => _state.Start();

    public void Rearm() => _state.Rearm();

    public void Stop() => _state.Stop();

    /// <summary>CN3FXPartBillBoard::Init.</summary>
    public void Init()
    {
        TexIndex = 0;
        CurrColor = FxColor.White;
        _currPos = _desc.Pos;
        _currVelocity = _desc.Velocity;
        _currScaleVelX = _desc.ScaleVelX;
        _currScaleVelY = _desc.ScaleVelY;
        _currSizeX = _desc.SizeX;
        _currSizeY = _desc.SizeY;
    }

    // CN3FXPartBillBoard::IsDead — dead once the full fade window has elapsed.
    private bool IsDead() => _state.CurrLife >= _desc.FadeIn + _desc.Life + _desc.FadeOut;

    /// <summary>CN3FXPartBillBoard::Tick — colour/size/position state (no camera).</summary>
    public bool Tick(float secPerFrame)
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

        _currScaleVelX += _desc.ScaleAccelX * secPerFrame;
        _currScaleVelY += _desc.ScaleAccelY * secPerFrame;
        _currSizeX += _currScaleVelX * secPerFrame;
        _currSizeY += _currScaleVelY * secPerFrame;

        for (int i = 0; i < 4; i++)
        {
            var corner = new Vector3(BaseUnit[i].X * _currSizeX, BaseUnit[i].Y * _currSizeY, 0f);
            _unit[i] = Vector3.Transform(corner, _desc.RotationMatrix);
        }

        return true;
    }

    /// <summary>
    /// CN3FXPartBillBoard::Render vertex build (the default, !RotateOnlyY path):
    /// billboard toward the camera at the radius offset. Returns the world center
    /// (<c>vCalPos</c>) for alpha sorting.
    /// </summary>
    public Vector3 Build(
        in Matrix4x4 viewInverseRotation,
        Vector3 cameraEye,
        Vector3 cameraAt,
        float nearPlane,
        FxBundleContext bundle,
        Span<N3VertexXyzColorT1> dest,
        Func<float, float, float>? groundHeight = null)
    {
        Matrix4x4 mtxRotZ = FxMath.RotationZ(_state.CurrLife * _desc.RotVelocity.X);

        Vector3 absoluteCurr = RotateToAbsolute(_currPos, bundle.Dir);
        Vector3 radiusPos = cameraEye - (absoluteCurr + bundle.Pos);
        if (radiusPos.Length() <= _desc.Radius)
            radiusPos += (cameraAt - cameraEye) * (nearPlane + 0.1f);
        else
            radiusPos = Vector3.Normalize(radiusPos) * _desc.Radius;

        Vector3 calPos = absoluteCurr + bundle.Pos + radiusPos;

        for (int b = 0; b < _desc.Num; b++)
        {
            int idx = b * 4;
            for (int k = 0; k < 4; k++)
            {
                Vector3 corner = _unit[k];
                if (bundle.DependScale)
                    corner = new Vector3(corner.X * bundle.TargetScale, corner.Y * bundle.TargetScale, 0f);

                Vector3 pos = Vector3.Transform(Vector3.Transform(corner, mtxRotZ), viewInverseRotation) + calPos;
                dest[idx + k] = new N3VertexXyzColorT1
                {
                    Position = pos,
                    Color = CurrColor,
                    Tu = Uvs[k].X,
                    Tv = Uvs[k].Y,
                };
            }

            if (_desc.OnGround && groundHeight != null)
            {
                float ground = groundHeight(calPos.X, calPos.Z);
                float newY = ground - (calPos.Y - radiusPos.Y) + _unit[0].Y;
                for (int k = 0; k < 4; k++)
                {
                    N3VertexXyzColorT1 v = dest[idx + k];
                    v.Position.Y += newY;
                    dest[idx + k] = v;
                }
            }
        }

        return calPos;
    }

    /// <summary>CN3FXPartBillBoard::Rotate2AbsolutePos — rotate a relative pos into the bundle facing.</summary>
    private static Vector3 RotateToAbsolute(Vector3 relative, Vector3 bundleDir)
    {
        var axisZ = new Vector3(0f, 0f, 1f);
        Vector3 dirAxis = Vector3.Cross(axisZ, bundleDir);

        // The C++ quantizes the axis to 1e-4 before the zero test.
        dirAxis.X = (int)(dirAxis.X * 10000.0f) / 10000.0f;
        dirAxis.Y = (int)(dirAxis.Y * 10000.0f) / 10000.0f;
        dirAxis.Z = (int)(dirAxis.Z * 10000.0f) / 10000.0f;

        if (dirAxis is { X: 0f, Y: 0f, Z: 0f })
            dirAxis = new Vector3(0f, 1f, 0f);

        float ang = MathF.Acos(Math.Clamp(Vector3.Dot(axisZ, bundleDir), -1f, 1f));
        Matrix4x4 rot = FxMath.RotationAxis(dirAxis, ang);
        return Vector3.Transform(relative, rot);
    }
}
