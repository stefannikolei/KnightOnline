using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Pure CPU skinning — the CN3Chr::Init/BuildMesh math: inverse bind
/// matrices from the frame-0 pose, then per vertex
/// vDest = Σ (origin · invBind[j] · joint[j]) · weight.
/// </summary>
public static class SkinDeformer
{
    /// <summary>
    /// CN3Chr::Init — ticks the skeleton at frame 0, collects the joints in
    /// pre-order (the FindPointerByID order) and inverts their matrices.
    /// </summary>
    public static (N3Joint[] Joints, Matrix4x4[] InverseBind) ComputeBindPose(N3Joint root)
    {
        root.Tick(0f);

        var joints = new List<N3Joint>();
        Collect(root, joints);

        var inverse = new Matrix4x4[joints.Count];
        for (int i = 0; i < joints.Count; i++)
        {
            if (!Matrix4x4.Invert(joints[i].Matrix, out inverse[i]))
                inverse[i] = Matrix4x4.Identity;
        }

        return (joints.ToArray(), inverse);

        static void Collect(N3Joint joint, List<N3Joint> list)
        {
            list.Add(joint);
            foreach (N3Joint child in joint.Children)
                Collect(child, list);
        }
    }

    /// <summary>
    /// CN3Chr::BuildMesh for one skin: writes the deformed positions.
    /// Vertices with no affecting joint keep their bind-pose position
    /// (the C++ leaves them untouched).
    /// </summary>
    public static void Deform(
        N3Skin skin,
        ReadOnlySpan<Matrix4x4> joints,
        ReadOnlySpan<Matrix4x4> inverseBind,
        Span<Vector3> destPositions)
    {
        N3SkinVertex[] skinVertices = skin.SkinVertices;
        N3VertexXyzNormal[] bindVertices = skin.Vertices;

        for (int j = 0; j < skinVertices.Length; j++)
        {
            N3SkinVertex src = skinVertices[j];
            if (src.Joints.Length == 1)
            {
                int index = src.Joints[0];
                destPositions[j] = Vector3.Transform(
                    Vector3.Transform(src.Origin, inverseBind[index]), joints[index]);
            }
            else if (src.Joints.Length > 1)
            {
                var final = Vector3.Zero;
                for (int k = 0; k < src.Joints.Length; k++)
                {
                    int index = src.Joints[k];
                    final += Vector3.Transform(
                        Vector3.Transform(src.Origin, inverseBind[index]), joints[index]) * src.Weights[k];
                }

                destPositions[j] = final;
            }
            else
            {
                destPositions[j] = bindVertices[j].Position;
            }
        }
    }

    /// <summary>
    /// CN3IMesh::BuildVertexList over the deformed positions: flattens the
    /// two index lists into a non-indexed triangle list (FaceCount*3).
    /// </summary>
    public static VertexPositionNormalTexture[] Flatten(N3Skin skin, ReadOnlySpan<Vector3> deformedPositions)
    {
        int corners = skin.FaceCount * 3;
        var result = new VertexPositionNormalTexture[corners];
        for (int n = 0; n < corners; n++)
        {
            int vi = skin.VertexIndices[n];
            float tu = 0f, tv = 0f;
            if (skin.UvCount > 0)
            {
                int uvIndex = skin.UvIndices[n];
                tu = skin.Uvs[uvIndex * 2];
                tv = skin.Uvs[uvIndex * 2 + 1];
            }

            result[n] = new VertexPositionNormalTexture(
                deformedPositions[vi].ToXna(),
                skin.Vertices[vi].Normal.ToXna(),
                new Microsoft.Xna.Framework.Vector2(tu, tv));
        }

        return result;
    }
}
