using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Pure vertex conversion from the asset-layer FVF structs to MonoGame
/// vertex types (position/normal/uv map 1:1; only the container changes).
/// </summary>
public static class MeshGeometry
{
    public static VertexPositionNormalTexture[] ToXna(ReadOnlySpan<N3VertexT1> vertices)
    {
        var result = new VertexPositionNormalTexture[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            ref readonly N3VertexT1 v = ref vertices[i];
            result[i] = new VertexPositionNormalTexture(
                v.Position.ToXna(), v.Normal.ToXna(), new Microsoft.Xna.Framework.Vector2(v.Tu, v.Tv));
        }

        return result;
    }

    /// <summary>
    /// D3DFMT_INDEX16 index buffers arrive as ushort[]; MonoGame's 16-bit
    /// draw overload takes short[] — bit-identical reinterpretation.
    /// </summary>
    public static short[] ToIndexBuffer(ReadOnlySpan<ushort> indices)
    {
        var result = new short[indices.Length];
        for (int i = 0; i < indices.Length; i++)
            result[i] = unchecked((short)indices[i]);
        return result;
    }
}
