using OpenKO.N3;
using OpenKO.Numerics;

namespace OpenKO.Client.Rendering;

/// <summary>
/// Builds a small procedural mesh and texture (no external assets required) so the render pipeline
/// can be exercised end-to-end. This is throwaway scaffolding: once the N3 asset path is wired up,
/// real meshes/textures load through the same <see cref="MeshRenderer"/> / <see cref="GpuTexture"/>.
/// </summary>
internal static class DemoScene
{
    /// <summary>A subclass that lets the demo populate the protected mesh fields.</summary>
    private sealed class BuiltMesh : N3IMesh
    {
        public void Set(VertexXyzNormal[] verts, ushort[] vIdx, float[] uvs, ushort[] uvIdx, int faces)
        {
            FaceCount = faces;
            Vertices = verts;
            VertexIndices = vIdx;
            UvCount = uvs.Length / 2;
            Uvs = uvs;
            UvIndices = uvIdx;
            FindMinMax();
        }
    }

    /// <summary>A textured quad (two triangles) facing +Z.</summary>
    public static N3IMesh CreateQuad()
    {
        var normal = new Vector3(0, 0, 1);
        var verts = new[]
        {
            new VertexXyzNormal(new Vector3(-1, -1, 0), normal),
            new VertexXyzNormal(new Vector3( 1, -1, 0), normal),
            new VertexXyzNormal(new Vector3( 1,  1, 0), normal),
            new VertexXyzNormal(new Vector3(-1,  1, 0), normal),
        };

        // Two triangles: 0-1-2, 0-2-3.
        var vIdx = new ushort[] { 0, 1, 2, 0, 2, 3 };
        var uvs = new[] { 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f };
        var uvIdx = new ushort[] { 0, 1, 2, 0, 2, 3 };

        var mesh = new BuiltMesh();
        mesh.Set(verts, vIdx, uvs, uvIdx, faces: 2);
        return mesh;
    }

    /// <summary>An 8x8 checkerboard A8R8G8B8 texture (stored B,G,R,A like the D3D format).</summary>
    public static N3Texture CreateCheckerboard()
    {
        const int size = 8;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool light = ((x ^ y) & 1) == 0;
                int i = (y * size + x) * 4;
                byte c = light ? (byte)220 : (byte)40;
                pixels[i + 0] = c;     // B
                pixels[i + 1] = c;     // G
                pixels[i + 2] = light ? (byte)200 : (byte)60; // R (slight tint)
                pixels[i + 3] = 255;   // A
            }
        }

        var tex = new N3Texture();
        tex.SetData("checker", size, size, N3PixelFormat.A8R8G8B8, new[] { pixels });
        return tex;
    }
}
