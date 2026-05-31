namespace OpenKO.Client.Rendering;

/// <summary>
/// Default GLSL shaders for drawing <see cref="MeshRenderer"/> geometry (VertexT1: position, normal,
/// uv) with a single texture and simple directional lighting. Kept minimal — this is the baseline
/// the ported N3 material/render flags will build on later.
/// </summary>
internal static class Shaders
{
    public const string Vertex = """
        #version 330 core
        layout (location = 0) in vec3 aPos;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aUv;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vNormal;
        out vec2 vUv;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
            vNormal = mat3(uModel) * aNormal;
            vUv = aUv;
        }
        """;

    public const string Fragment = """
        #version 330 core
        in vec3 vNormal;
        in vec2 vUv;

        uniform sampler2D uTexture;

        out vec4 FragColor;

        void main()
        {
            vec3 lightDir = normalize(vec3(0.4, 0.8, 0.5));
            float diffuse = max(dot(normalize(vNormal), lightDir), 0.0);
            float ambient = 0.3;
            vec4 tex = texture(uTexture, vUv);
            FragColor = vec4(tex.rgb * (ambient + diffuse), tex.a);
        }
        """;
}
