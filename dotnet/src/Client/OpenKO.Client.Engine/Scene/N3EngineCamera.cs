using System.Numerics;

namespace OpenKO.Client.Engine.Scene;

/// <summary>
/// Port of <c>CN3Camera</c>'s math (Client/N3Base/N3Camera.cpp): left-handed
/// look-at + perspective projection (LookAtLH/PerspectiveFovLH via the
/// System.Numerics *LeftHanded factories, net10) and the frustum planes.
/// The device layer applies View/Projection to effects; everything here is
/// pure and headless-testable.
/// </summary>
public sealed class N3EngineCamera
{
    /// <summary>CGameEng in-game FOV (70°, in radians).</summary>
    public const float GameFov = 70f * MathF.PI / 180f;

    /// <summary>The char-select camera FOV (0.96 rad, GameProcCharacterSelect).</summary>
    public const float CharSelectFov = 0.96f;

    public Vector3 Eye { get; set; } = new(15f, 5f, -15f);

    public Vector3 At { get; set; } = Vector3.Zero;

    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>Vertical field of view in radians (C++ default 55°).</summary>
    public float Fov { get; set; } = 55f * MathF.PI / 180f;

    public float NearPlane { get; set; } = 0.7f;

    public float FarPlane { get; set; } = 512f;

    public float Aspect { get; set; } = 4f / 3f;

    public Matrix4x4 View { get; private set; } = Matrix4x4.Identity;

    public Matrix4x4 Projection { get; private set; } = Matrix4x4.Identity;

    public Matrix4x4 ViewProjection { get; private set; } = Matrix4x4.Identity;

    public Frustum Frustum { get; private set; } = new();

    /// <summary>CN3Camera::Tick — recomputes matrices and frustum planes.</summary>
    public void Update()
    {
        View = Matrix4x4.CreateLookAtLeftHanded(Eye, At, Up);
        Projection = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(Fov, Aspect, NearPlane, FarPlane);
        ViewProjection = View * Projection;
        Frustum = Frustum.FromViewProjection(ViewProjection);
    }

    /// <summary>The C++ SetLOD input: camera distance × FOV.</summary>
    public float LodValue(Vector3 worldPosition)
        => (Eye - worldPosition).Length() * Fov;
}
