namespace OpenKO.Client.Rendering;

/// <summary>
/// GLSL shaders for the 2D, screen-space UI pass. Vertices arrive in pixel coordinates and are mapped
/// to clip space by an orthographic projection; a single program handles both solid-colour quads
/// (<c>uUseTexture == 0</c>) and tinted textured quads (<c>uUseTexture == 1</c>).
/// </summary>
internal static class UiShaders
{
    public const string Vertex = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aUv;

        uniform mat4 uProjection;

        out vec2 vUv;

        void main()
        {
            gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
            vUv = aUv;
        }
        """;

    public const string Fragment = """
        #version 330 core
        in vec2 vUv;

        uniform sampler2D uTexture;
        uniform vec4 uTint;
        uniform int uUseTexture;

        out vec4 FragColor;

        void main()
        {
            if (uUseTexture == 1)
                FragColor = texture(uTexture, vUv) * uTint;
            else
                FragColor = uTint;
        }
        """;
}
