using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3Transform</c> (Client/N3Base/N3Transform.cpp): position /
/// rotation / scale with their keyframe channels and the composed local
/// matrix. __Matrix44 is row-major with row-vector convention — identical to
/// System.Numerics, so the matrix composition maps 1:1.
/// </summary>
public class N3Transform : N3BaseFile
{
    public Vector3 Position { get; set; }

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Vector3 Scale { get; set; } = Vector3.One;

    public N3AnimKey KeyPos { get; } = new();

    public N3AnimKey KeyRot { get; } = new();

    public N3AnimKey KeyScale { get; } = new();

    /// <summary>Whole animation length in 30fps frames (max over the channels).</summary>
    public float TotalFrames { get; protected set; }

    public Matrix4x4 Matrix { get; protected set; } = Matrix4x4.Identity;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        Position = reader.ReadVector3();
        Rotation = reader.ReadQuaternion();
        Scale = reader.ReadVector3();

        KeyPos.Load(reader);
        KeyRot.Load(reader);
        KeyScale.Load(reader);

        TotalFrames = 0f;
        TotalFrames = MathF.Max(TotalFrames, KeyPos.Count * KeyPos.SamplingRate / 30f);
        TotalFrames = MathF.Max(TotalFrames, KeyRot.Count * KeyRot.SamplingRate / 30f);
        TotalFrames = MathF.Max(TotalFrames, KeyScale.Count * KeyScale.SamplingRate / 30f);

        ReCalcMatrix();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(Position);
        writer.Write(Rotation);
        writer.Write(Scale);

        KeyPos.Save(writer);
        KeyRot.Save(writer);
        KeyScale.Save(writer);
    }

    /// <summary>
    /// CN3Transform::ReCalcMatrix — scale, then rotation (only when w != 0,
    /// a C++ guard against zeroed quaternions), then the translation row.
    /// </summary>
    public virtual void ReCalcMatrix()
    {
        Matrix4x4 m = Matrix4x4.CreateScale(Scale);
        if (Rotation.W != 0f)
            m *= Matrix4x4.CreateFromQuaternion(Rotation);
        m.Translation = Position;
        Matrix = m;
    }

    /// <summary>CN3Transform::TickAnimationKey — samples all channels at the given frame.</summary>
    public virtual bool TickAnimationKey(float frame)
    {
        if (KeyPos.Count <= 0 && KeyRot.Count <= 0 && KeyScale.Count <= 0)
            return false;

        bool needReCalc = false;
        Vector3 pos = Position;
        if (KeyPos.TryGetVector3(frame, ref pos))
        {
            Position = pos;
            needReCalc = true;
        }

        Quaternion rot = Rotation;
        if (KeyRot.TryGetQuaternion(frame, ref rot))
        {
            Rotation = rot;
            needReCalc = true;
        }

        Vector3 scale = Scale;
        if (KeyScale.TryGetVector3(frame, ref scale))
        {
            Scale = scale;
            needReCalc = true;
        }

        return needReCalc;
    }
}
