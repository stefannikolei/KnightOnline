#version 450

// -----------------------------------------------------------------------------
// Character vertex shader (GPU linear-blend skinning).
//
// This is the Vulkan equivalent of what CN3Chr::BuildMesh() does on the CPU in
// the original DirectX 9 N3 engine: each vertex is transformed by a weighted
// blend of its influencing joint matrices. In the N3 engine this was computed
// per-vertex on the CPU every frame; here we upload the per-joint skinning
// matrices once per frame and let the GPU do the blend.
// -----------------------------------------------------------------------------

layout(set = 0, binding = 0) uniform CameraUBO {
    mat4 view;
    mat4 proj;
    vec4 lightDir; // xyz = directional light direction (world space)
    vec4 camPos;   // xyz = camera position (world space)
} cam;

const int MAX_BONES = 64;

layout(set = 0, binding = 1) uniform BoneUBO {
    // Per-joint skinning matrix == jointWorldAnimated * inverseBindPose.
    // Mirrors N3Chr's m_MtxJoints[i] combined with m_MtxInverses[i].
    mat4 bones[MAX_BONES];
} skin;

layout(location = 0) in vec3  inPos;     // bind-pose position (model space)
layout(location = 1) in vec3  inNormal;
layout(location = 2) in vec2  inUV;
layout(location = 3) in ivec4 inJoints;  // up to 4 influencing joints
layout(location = 4) in vec4  inWeights; // matching blend weights (sum ~= 1)

layout(location = 0) out vec3 outNormal;
layout(location = 1) out vec2 outUV;
layout(location = 2) out vec3 outWorldPos;

void main() {
    mat4 skinMat =
        inWeights.x * skin.bones[inJoints.x] +
        inWeights.y * skin.bones[inJoints.y] +
        inWeights.z * skin.bones[inJoints.z] +
        inWeights.w * skin.bones[inJoints.w];

    vec4 skinnedPos    = skinMat * vec4(inPos, 1.0);
    vec3 skinnedNormal = mat3(skinMat) * inNormal;

    outWorldPos = skinnedPos.xyz;
    outNormal   = normalize(skinnedNormal);
    outUV       = inUV;

    gl_Position = cam.proj * cam.view * skinnedPos;
}
