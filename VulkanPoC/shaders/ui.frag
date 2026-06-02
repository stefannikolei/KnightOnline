#version 450

// 2D UI fragment shader: just samples the (CPU-decoded RGBA8) UI texture.
// Alpha blending is enabled in the UI pipeline for transparent overlays.

layout(set = 0, binding = 0) uniform sampler2D uiTex;

layout(location = 0) in vec2 inUV;

layout(location = 0) out vec4 outColor;

void main() {
    outColor = texture(uiTex, inUV);
}
