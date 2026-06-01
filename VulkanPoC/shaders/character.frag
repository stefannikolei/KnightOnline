#version 450

// -----------------------------------------------------------------------------
// Character fragment shader.
//
// Simple directional (Blinn-Phong) lighting. The original N3 engine relied on
// fixed-function lighting + a diffuse texture per character part; for this PoC
// we use a flat base colour so the silhouette/animation is clearly visible
// without needing the proprietary .dxt character textures (which are not part
// of this repository).
// -----------------------------------------------------------------------------

layout(set = 0, binding = 0) uniform CameraUBO {
    mat4 view;
    mat4 proj;
    vec4 lightDir;
    vec4 camPos;
} cam;

layout(location = 0) in vec3 inNormal;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inWorldPos;

layout(location = 0) out vec4 outColor;

void main() {
    vec3 N = normalize(inNormal);
    vec3 L = normalize(-cam.lightDir.xyz);
    vec3 V = normalize(cam.camPos.xyz - inWorldPos);
    vec3 H = normalize(L + V);

    float ambient = 0.25;
    float diff    = max(dot(N, L), 0.0);
    float spec    = pow(max(dot(N, H), 0.0), 32.0) * 0.25;

    vec3 base  = vec3(0.62, 0.54, 0.46);
    vec3 color = base * (ambient + diff) + vec3(spec);

    outColor = vec4(color, 1.0);
}
