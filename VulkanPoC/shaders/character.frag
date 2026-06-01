#version 450

// -----------------------------------------------------------------------------
// Character fragment shader.
//
// Samples the part's diffuse texture (decoded from the game's DXT/BC1 .dxt
// files to RGBA8 on the CPU) and applies simple directional Blinn-Phong
// lighting, similar in spirit to the original N3 fixed-function setup.
// -----------------------------------------------------------------------------

layout(set = 0, binding = 0) uniform CameraUBO {
    mat4 view;
    mat4 proj;
    vec4 lightDir;
    vec4 camPos;
} cam;

layout(set = 1, binding = 0) uniform sampler2D diffuseTex;

layout(location = 0) in vec3 inNormal;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inWorldPos;

layout(location = 0) out vec4 outColor;

void main() {
    vec4 tex = texture(diffuseTex, inUV);
    if (tex.a < 0.3) discard; // 1-bit DXT1 cutout

    vec3 N = normalize(inNormal);
    vec3 L = normalize(-cam.lightDir.xyz);
    vec3 V = normalize(cam.camPos.xyz - inWorldPos);
    vec3 H = normalize(L + V);

    float ambient = 0.35;
    float diff    = max(dot(N, L), 0.0);
    float spec    = pow(max(dot(N, H), 0.0), 24.0) * 0.15;

    vec3 color = tex.rgb * (ambient + diff) + vec3(spec);
    outColor = vec4(color, 1.0);
}
