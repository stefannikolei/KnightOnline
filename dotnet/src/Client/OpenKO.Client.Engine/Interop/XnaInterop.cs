using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace OpenKO.Client.Engine.Interop;

/// <summary>
/// System.Numerics ↔ XNA type conversion at the device boundary. Both sides
/// are row-major with row-vector convention, so matrices map element-wise —
/// all engine math stays System.Numerics; MonoGame types appear only here.
/// </summary>
public static class XnaInterop
{
    public static XnaVector2 ToXna(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    public static XnaVector3 ToXna(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector3 ToNumerics(this XnaVector3 v) => new(v.X, v.Y, v.Z);

    public static XnaMatrix ToXna(this System.Numerics.Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    public static System.Numerics.Matrix4x4 ToNumerics(this XnaMatrix m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
