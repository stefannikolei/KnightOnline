using System.Numerics;
using OpenKO.Game.Rendering;
using OpenKO.N3;
using OpenKO.Numerics;
using Silk.NET.OpenGL;

namespace OpenKO.Client.Rendering;

/// <summary>
/// OpenGL implementation of <see cref="IUiRenderer"/> — the screen-space 2D pass that draws the
/// ported UI controls. Replaces the original client's pre-transformed (RHW) UI quads with an
/// orthographic projection (pixel coordinates, origin top-left) and a single quad shader.
///
/// Textures referenced by <see cref="N3UIImage"/> are uploaded on demand and cached per
/// <see cref="N3Texture"/> via <see cref="GpuTexture"/>.
/// </summary>
public sealed class UiRenderer : IUiRenderer, IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly Dictionary<N3Texture, GpuTexture> _textureCache = new();

    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    public unsafe UiRenderer(GL gl, int screenWidth, int screenHeight)
    {
        _gl = gl;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;

        _shader = new ShaderProgram(gl, UiShaders.Vertex, UiShaders.Fragment);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // 4 vertices * (vec2 pos + vec2 uv) = 16 floats, streamed each draw.
        _gl.BufferData(BufferTargetARB.ArrayBuffer, 16 * sizeof(float), null, BufferUsageARB.DynamicDraw);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    public void Resize(int width, int height)
    {
        ScreenWidth = width;
        ScreenHeight = height;
    }

    public void Begin()
    {
        _shader.Use();

        // Pixel coordinates, origin at the top-left (y grows downward).
        Matrix4x4 ortho = Matrix4x4.CreateOrthographicOffCenter(0, ScreenWidth, ScreenHeight, 0, -1f, 1f);
        _shader.SetUniform("uProjection", ortho);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _gl.BindVertexArray(_vao);
    }

    public void DrawQuad(Rect region, UiColor color)
    {
        (float r, float g, float b, float a) = color.ToFloats();
        _shader.SetUniform("uUseTexture", 0);
        _shader.SetUniform("uTint", r, g, b, a);
        UploadQuad(region, new FloatRect(0, 0, 1, 1));
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    public void DrawImage(Rect region, N3Texture texture, FloatRect uv, UiColor tint)
    {
        GpuTexture gpu = GetOrCreate(texture);

        (float r, float g, float b, float a) = tint.ToFloats();
        _shader.SetUniform("uUseTexture", 1);
        _shader.SetUniform("uTexture", 0);
        _shader.SetUniform("uTint", r, g, b, a);

        gpu.Bind(TextureUnit.Texture0);
        UploadQuad(region, uv);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    public void End()
    {
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
    }

    private unsafe void UploadQuad(Rect region, FloatRect uv)
    {
        float l = region.Left, t = region.Top, rt = region.Right, b = region.Bottom;

        // Triangle strip order: top-left, bottom-left, top-right, bottom-right.
        ReadOnlySpan<float> verts = stackalloc float[]
        {
            l,  t,  uv.Left,  uv.Top,
            l,  b,  uv.Left,  uv.Bottom,
            rt, t,  uv.Right, uv.Top,
            rt, b,  uv.Right, uv.Bottom,
        };

        fixed (float* p = verts)
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(verts.Length * sizeof(float)), p);
    }

    private GpuTexture GetOrCreate(N3Texture texture)
    {
        if (!_textureCache.TryGetValue(texture, out GpuTexture? gpu))
        {
            gpu = new GpuTexture(_gl, texture);
            _textureCache[texture] = gpu;
        }

        return gpu;
    }

    public void Dispose()
    {
        foreach (GpuTexture tex in _textureCache.Values)
            tex.Dispose();
        _textureCache.Clear();

        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
