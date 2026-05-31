using System.Runtime.InteropServices;
using OpenKO.N3;
using Silk.NET.OpenGL;

namespace OpenKO.Client.Rendering;

/// <summary>
/// Uploads an <see cref="N3IMesh"/>'s expanded vertex list into an OpenGL VAO/VBO and draws it.
///
/// The vertex layout is <see cref="VertexT1"/>: position (vec3), normal (vec3), uv (vec2) — 8 floats,
/// 32 bytes — matching the original FVF_VNT1 stream. This is the GL replacement for
/// <c>CN3IMesh::Render</c>, which on D3D9 set FVF_VNT1 and called DrawPrimitiveUP.
/// </summary>
public sealed class MeshRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly int _vertexCount;

    public unsafe MeshRenderer(GL gl, N3IMesh mesh)
    {
        _gl = gl;

        VertexT1[] vertices = mesh.BuildVertexList();
        _vertexCount = vertices.Length;

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(vertices.AsSpan());
        fixed (byte* p = bytes)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)bytes.Length, p, BufferUsageARB.StaticDraw);
        }

        const uint stride = 8 * sizeof(float);
        // location 0: position
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        // location 1: normal
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        // location 2: uv
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    public void Draw()
    {
        if (_vertexCount == 0)
            return;

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
