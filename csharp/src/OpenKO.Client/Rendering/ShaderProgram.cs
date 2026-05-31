using Silk.NET.OpenGL;

namespace OpenKO.Client.Rendering;

/// <summary>Thin helper around an OpenGL shader program (vertex + fragment), cross-platform via Silk.NET.</summary>
public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;

        uint vert = Compile(ShaderType.VertexShader, vertexSource);
        uint frag = Compile(ShaderType.FragmentShader, fragmentSource);

        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vert);
        _gl.AttachShader(_handle, frag);
        _gl.LinkProgram(_handle);

        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"Shader link failed: {_gl.GetProgramInfoLog(_handle)}");

        _gl.DetachShader(_handle, vert);
        _gl.DetachShader(_handle, frag);
        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);
    }

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"{type} compile failed: {_gl.GetShaderInfoLog(shader)}");

        return shader;
    }

    public void Use() => _gl.UseProgram(_handle);

    public void SetUniform(string name, in System.Numerics.Matrix4x4 value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc < 0)
            return;

        ReadOnlySpan<float> data = stackalloc float[]
        {
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44,
        };
        _gl.UniformMatrix4(loc, 1, false, data);
    }

    public void SetUniform(string name, int value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc >= 0)
            _gl.Uniform1(loc, value);
    }

    public void SetUniform(string name, float x, float y, float z, float w)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc >= 0)
            _gl.Uniform4(loc, x, y, z, w);
    }

    public void Dispose() => _gl.DeleteProgram(_handle);
}
