using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// One skinned vertex (__VertexSkinned): the bind-pose position plus the
/// affecting joints. With a single joint no weight is stored (implicitly 1);
/// with several, parallel joint/weight arrays.
/// </summary>
public sealed class N3SkinVertex
{
    public Vector3 Origin { get; set; }

    /// <summary>Joint indices; length is the C++ nAffect (0, 1 or n).</summary>
    public int[] Joints { get; set; } = [];

    /// <summary>Weights, parallel to Joints — empty when Joints.Length &lt;= 1.</summary>
    public float[] Weights { get; set; } = [];
}

/// <summary>
/// Port of <c>CN3Skin</c> (Client/N3Base/N3Skin.cpp) — an indexed mesh whose
/// vertices carry skinning data. The file appends, per IMesh vertex: the
/// origin, the affect count, 8 dead bytes (the serialized 32-bit pnJoints /
/// pfWeights pointers) and then the joint/weight arrays.
/// </summary>
public sealed class N3Skin : N3IMesh
{
    public N3SkinVertex[] SkinVertices { get; private set; } = [];

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        var skinVertices = new N3SkinVertex[VertexCount];
        for (int i = 0; i < skinVertices.Length; i++)
        {
            var vtx = new N3SkinVertex { Origin = reader.ReadVector3() };
            int affect = reader.ReadInt32();
            reader.BaseStream.Seek(8, SeekOrigin.Current); // dead 32-bit pointers

            if (affect > 1)
            {
                vtx.Joints = reader.ReadStructs<int>(affect);
                vtx.Weights = reader.ReadStructs<float>(affect);
            }
            else if (affect == 1)
            {
                vtx.Joints = [reader.ReadInt32()];
            }

            skinVertices[i] = vtx;
        }

        SkinVertices = skinVertices;
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        foreach (N3SkinVertex vtx in SkinVertices)
        {
            writer.Write(vtx.Origin);
            writer.Write(vtx.Joints.Length);
            writer.Write(0); // dead pnJoints pointer
            writer.Write(0); // dead pfWeights pointer

            if (vtx.Joints.Length > 1)
            {
                writer.WriteStructs<int>(vtx.Joints);
                writer.WriteStructs<float>(vtx.Weights);
            }
            else if (vtx.Joints.Length == 1)
            {
                writer.Write(vtx.Joints[0]);
            }
        }
    }

    public void InitializeSkin(N3SkinVertex[] skinVertices)
        => SkinVertices = skinVertices;
}
