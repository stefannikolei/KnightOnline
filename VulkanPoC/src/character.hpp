#pragma once

// -----------------------------------------------------------------------------
// Procedural skinned character.
//
// This mirrors the data model of CN3Chr in the original N3 engine, but in a
// self-contained way so the Vulkan PoC has something to render without the
// proprietary .n3chr/.n3pmesh/.dxt assets (which are not part of this repo):
//
//   N3 engine                         this PoC
//   ----------------------------      ------------------------------------------
//   CN3Joint hierarchy            ->  Joint[] (parent index + bind transform)
//   __VertexSkinned (origin +     ->  Vertex (pos + joint indices + weights)
//     joint indices + weights)
//   m_MtxJoints / m_MtxInverses   ->  skinningMatrices() (world * inverseBind)
//   CN3AnimControl / BuildMesh    ->  evaluate() (procedural idle animation)
//
// The geometry is a simple low-poly humanoid built from boxes, each rigidly
// bound to one joint, which is enough to demonstrate the full skinned-mesh
// Vulkan pipeline (the part that actually matters for the port).
// -----------------------------------------------------------------------------

#include <cstdint>
#include <vector>

#include <glm/glm.hpp>

namespace poc {

struct Vertex {
    glm::vec3  pos;     // bind-pose position (model space)
    glm::vec3  normal;
    glm::vec2  uv;
    glm::ivec4 joints;  // influencing joint indices
    glm::vec4  weights; // matching blend weights
};

// Number of joints in the demo skeleton (kept well under the shader's MAX_BONES).
static constexpr int kMaxBones = 64;

class Character {
public:
    Character();

    const std::vector<Vertex>&   vertices() const { return m_vertices; }
    const std::vector<uint32_t>& indices()  const { return m_indices; }

    int jointCount() const { return static_cast<int>(m_joints.size()); }

    // Advance the procedural idle animation and return the per-joint skinning
    // matrices (jointWorldAnimated * inverseBindPose) to upload to the GPU.
    // Equivalent to CN3Chr::TickJoints() + the matrix setup in BuildMesh().
    std::vector<glm::mat4> skinningMatrices(float timeSeconds) const;

private:
    struct Joint {
        int       parent;       // -1 for the root
        glm::vec3 bindLocalPos; // local translation relative to parent in bind pose
    };

    void addBox(const glm::vec3& center, const glm::vec3& halfExtents, int joint);
    void buildSkeleton();
    void buildMesh();

    std::vector<Joint>     m_joints;
    std::vector<glm::mat4> m_inverseBind; // computed from the bind pose
    std::vector<Vertex>    m_vertices;
    std::vector<uint32_t>  m_indices;
};

} // namespace poc
