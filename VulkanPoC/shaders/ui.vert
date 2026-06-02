#version 450

// 2D UI vertex shader for the login screen. Positions come in as virtual-canvas
// pixels and are mapped to clip space by an orthographic matrix supplied as a
// push constant (computed per frame to fit/letterbox the canvas in the window).

layout(push_constant) uniform Push {
    mat4 ortho;
} pc;

layout(location = 0) in vec2 inPos;
layout(location = 1) in vec2 inUV;

layout(location = 0) out vec2 outUV;

void main() {
    outUV = inUV;
    gl_Position = pc.ortho * vec4(inPos, 0.0, 1.0);
}
