using System.Numerics;
using OpenKO.GameData.Math;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3Joint</c> (Client/N3Base/N3Joint.cpp) — the skeleton node:
/// a transform plus the orient quaternion channel and recursively loaded
/// children. .n3joint files are one root joint.
/// </summary>
public sealed class N3Joint : N3Transform
{
    public Quaternion Orient { get; set; } = Quaternion.Identity;

    public N3AnimKey KeyOrient { get; } = new();

    public N3Joint? Parent { get; private set; }

    public List<N3Joint> Children { get; } = [];

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        KeyOrient.Load(reader);

        int childCount = reader.ReadInt32();
        for (int i = 0; i < childCount; i++)
        {
            var child = new N3Joint();
            ChildAdd(child);
            child.Load(reader);
        }
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        KeyOrient.Save(writer);

        writer.Write(Children.Count);
        foreach (N3Joint child in Children)
            child.Save(writer);
    }

    public void ChildAdd(N3Joint child)
    {
        if (Children.Contains(child))
            return;

        Children.Add(child);
        child.Parent = this;
    }

    /// <summary>CN3Joint::NodeCount — this joint plus all descendants.</summary>
    public int NodeCount()
    {
        int count = 1;
        foreach (N3Joint child in Children)
            count += child.NodeCount();
        return count;
    }

    /// <summary>
    /// CN3Joint::FindPointerByID — depth-first pre-order index lookup
    /// (the C++ uses a static counter; same walk order here).
    /// </summary>
    public N3Joint? FindById(int id)
    {
        int counter = 0;
        return FindById(id, ref counter);
    }

    private N3Joint? FindById(int id, ref int counter)
    {
        if (id == counter)
            return this;

        counter++;
        foreach (N3Joint child in Children)
        {
            N3Joint? found = child.FindById(id, ref counter);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>CN3Joint::Tick — samples all channels and recomputes the hierarchy matrices.</summary>
    public void Tick(float frame)
    {
        Vector3 pos = Position;
        if (KeyPos.TryGetVector3(frame, ref pos))
            Position = pos;

        Quaternion rot = Rotation;
        if (KeyRot.TryGetQuaternion(frame, ref rot))
            Rotation = rot;

        Vector3 scale = Scale;
        if (KeyScale.TryGetVector3(frame, ref scale))
            Scale = scale;

        Quaternion orient = Orient;
        if (KeyOrient.TryGetQuaternion(frame, ref orient))
            Orient = orient;

        ReCalcMatrix();

        foreach (N3Joint child in Children)
            child.Tick(frame);
    }

    /// <summary>
    /// CN3Joint::ReCalcMatrixBlended — samples every channel at two frames
    /// and blends (linear for pos/scale with weight0, slerp with weight1 for
    /// the quaternions — the C++ passes fWeight1 to Slerp), then recomputes
    /// the matrix. Channels without keys keep their current values.
    /// </summary>
    public void ReCalcMatrixBlended(float frame0, float frame1, float weight0)
    {
        float weight1 = 1f - weight0;

        var v1 = Vector3.Zero;
        var v2 = Vector3.Zero;
        if (KeyPos.TryGetVector3(frame0, ref v1) && KeyPos.TryGetVector3(frame1, ref v2))
            Position = v1 * weight0 + v2 * weight1;

        var q1 = Quaternion.Identity;
        var q2 = Quaternion.Identity;
        if (KeyRot.TryGetQuaternion(frame0, ref q1) && KeyRot.TryGetQuaternion(frame1, ref q2))
            Rotation = KoQuaternion.Slerp(q1, q2, weight1);

        if (KeyScale.TryGetVector3(frame0, ref v1) && KeyScale.TryGetVector3(frame1, ref v2))
            Scale = v1 * weight0 + v2 * weight1;

        if (KeyOrient.TryGetQuaternion(frame0, ref q1) && KeyOrient.TryGetQuaternion(frame1, ref q2))
            Orient = KoQuaternion.Slerp(q1, q2, weight1);

        ReCalcMatrix();
    }

    public override bool TickAnimationKey(float frame)
    {
        bool needReCalc = base.TickAnimationKey(frame);
        Quaternion orient = Orient;
        if (KeyOrient.TryGetQuaternion(frame, ref orient))
        {
            Orient = orient;
            needReCalc = true;
        }

        return needReCalc;
    }

    /// <summary>
    /// CN3Joint::ReCalcMatrix — rotation (qRot * qOrient in __Quaternion
    /// multiply order when orient keys exist), optional scale, translation,
    /// then the parent matrix.
    /// </summary>
    public override void ReCalcMatrix()
    {
        Quaternion q = KeyOrient.Count > 0 ? KoQuaternion.Multiply(Rotation, Orient) : Rotation;
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(q);

        if (Scale.X != 1f || Scale.Y != 1f || Scale.Z != 1f)
            m *= Matrix4x4.CreateScale(Scale);

        m.Translation = Position;

        if (Parent != null)
            m *= Parent.Matrix;

        Matrix = m;
    }

    /// <summary>CN3Joint::MatricesGet — pre-order matrix collection (skinning palette).</summary>
    public void MatricesGet(Matrix4x4[] matrices, ref int jointIndex)
    {
        matrices[jointIndex++] = Matrix;
        foreach (N3Joint child in Children)
            child.MatricesGet(matrices, ref jointIndex);
    }
}
