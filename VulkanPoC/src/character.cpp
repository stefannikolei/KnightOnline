#include "character.hpp"

#include <glm/gtc/matrix_transform.hpp>

#include <cmath>

namespace poc {

// Joint indices for readability.
enum JointId {
    J_PELVIS = 0,
    J_SPINE,
    J_HEAD,
    J_L_UPPERARM,
    J_L_FOREARM,
    J_R_UPPERARM,
    J_R_FOREARM,
    J_L_THIGH,
    J_L_SHIN,
    J_R_THIGH,
    J_R_SHIN,
    J_COUNT
};

Character::Character() {
    buildSkeleton();
    buildMesh();
}

void Character::buildSkeleton() {
    // World-space bind positions of each joint (Y up, character ~1.8m tall).
    struct Def { int parent; glm::vec3 world; };
    const Def defs[J_COUNT] = {
        /* J_PELVIS    */ { -1,          {  0.00f, 0.95f, 0.0f } },
        /* J_SPINE     */ { J_PELVIS,    {  0.00f, 1.25f, 0.0f } },
        /* J_HEAD      */ { J_SPINE,     {  0.00f, 1.60f, 0.0f } },
        /* J_L_UPPERARM*/ { J_SPINE,     {  0.22f, 1.45f, 0.0f } },
        /* J_L_FOREARM */ { J_L_UPPERARM,{  0.45f, 1.30f, 0.0f } },
        /* J_R_UPPERARM*/ { J_SPINE,     { -0.22f, 1.45f, 0.0f } },
        /* J_R_FOREARM */ { J_R_UPPERARM,{ -0.45f, 1.30f, 0.0f } },
        /* J_L_THIGH   */ { J_PELVIS,    {  0.11f, 0.85f, 0.0f } },
        /* J_L_SHIN    */ { J_L_THIGH,   {  0.11f, 0.45f, 0.0f } },
        /* J_R_THIGH   */ { J_PELVIS,    { -0.11f, 0.85f, 0.0f } },
        /* J_R_SHIN    */ { J_R_THIGH,   { -0.11f, 0.45f, 0.0f } },
    };

    m_joints.resize(J_COUNT);
    std::vector<glm::mat4> bindWorld(J_COUNT);

    for (int i = 0; i < J_COUNT; ++i) {
        // Local bind translation = world - parentWorld (rotation is identity in bind pose).
        glm::vec3 pWorld = (defs[i].parent >= 0) ? defs[defs[i].parent].world : glm::vec3(0.0f);
        m_joints[i].parent       = defs[i].parent;
        m_joints[i].bindLocalPos = defs[i].world - pWorld;

        glm::mat4 local = glm::translate(glm::mat4(1.0f), m_joints[i].bindLocalPos);
        bindWorld[i] = (defs[i].parent >= 0) ? bindWorld[defs[i].parent] * local : local;
    }

    m_inverseBind.resize(J_COUNT);
    for (int i = 0; i < J_COUNT; ++i)
        m_inverseBind[i] = glm::inverse(bindWorld[i]);
}

void Character::addBox(const glm::vec3& c, const glm::vec3& h, int joint) {
    // 8 corners.
    const glm::vec3 p[8] = {
        c + glm::vec3(-h.x, -h.y, -h.z), c + glm::vec3( h.x, -h.y, -h.z),
        c + glm::vec3( h.x,  h.y, -h.z), c + glm::vec3(-h.x,  h.y, -h.z),
        c + glm::vec3(-h.x, -h.y,  h.z), c + glm::vec3( h.x, -h.y,  h.z),
        c + glm::vec3( h.x,  h.y,  h.z), c + glm::vec3(-h.x,  h.y,  h.z),
    };
    // Six faces (4 corner indices each) with outward normals.
    struct Face { int a, b, c, d; glm::vec3 n; };
    const Face faces[6] = {
        { 0, 1, 2, 3, {  0,  0, -1 } }, // back
        { 5, 4, 7, 6, {  0,  0,  1 } }, // front
        { 4, 0, 3, 7, { -1,  0,  0 } }, // left
        { 1, 5, 6, 2, {  1,  0,  0 } }, // right
        { 3, 2, 6, 7, {  0,  1,  0 } }, // top
        { 4, 5, 1, 0, {  0, -1,  0 } }, // bottom
    };
    const glm::vec2 uv[4] = { {0,0}, {1,0}, {1,1}, {0,1} };

    for (const Face& f : faces) {
        uint32_t base = static_cast<uint32_t>(m_vertices.size());
        const int idx[4] = { f.a, f.b, f.c, f.d };
        for (int k = 0; k < 4; ++k) {
            Vertex v{};
            v.pos     = p[idx[k]];
            v.normal  = f.n;
            v.uv      = uv[k];
            v.joints  = glm::ivec4(joint, 0, 0, 0);
            v.weights = glm::vec4(1.0f, 0.0f, 0.0f, 0.0f); // rigid bind
            m_vertices.push_back(v);
        }
        m_indices.insert(m_indices.end(),
                         { base + 0, base + 1, base + 2, base + 0, base + 2, base + 3 });
    }
}

void Character::buildMesh() {
    // center, halfExtents, joint
    addBox({  0.00f, 0.95f, 0.0f }, { 0.15f, 0.10f, 0.10f }, J_PELVIS);
    addBox({  0.00f, 1.30f, 0.0f }, { 0.17f, 0.22f, 0.10f }, J_SPINE);   // torso
    addBox({  0.00f, 1.73f, 0.0f }, { 0.11f, 0.13f, 0.11f }, J_HEAD);

    addBox({  0.33f, 1.38f, 0.0f }, { 0.12f, 0.06f, 0.06f }, J_L_UPPERARM);
    addBox({  0.52f, 1.18f, 0.0f }, { 0.10f, 0.06f, 0.06f }, J_L_FOREARM);
    addBox({ -0.33f, 1.38f, 0.0f }, { 0.12f, 0.06f, 0.06f }, J_R_UPPERARM);
    addBox({ -0.52f, 1.18f, 0.0f }, { 0.10f, 0.06f, 0.06f }, J_R_FOREARM);

    addBox({  0.11f, 0.65f, 0.0f }, { 0.08f, 0.22f, 0.09f }, J_L_THIGH);
    addBox({  0.11f, 0.25f, 0.0f }, { 0.07f, 0.22f, 0.08f }, J_L_SHIN);
    addBox({ -0.11f, 0.65f, 0.0f }, { 0.08f, 0.22f, 0.09f }, J_R_THIGH);
    addBox({ -0.11f, 0.25f, 0.0f }, { 0.07f, 0.22f, 0.08f }, J_R_SHIN);
}

std::vector<glm::mat4> Character::skinningMatrices(float t) const {
    const int n = static_cast<int>(m_joints.size());

    // Per-joint animated local rotation (radians) for a gentle idle pose.
    std::vector<glm::mat4> localAnim(n, glm::mat4(1.0f));

    const float breathe = std::sin(t * 1.6f);
    const float sway    = std::sin(t * 0.9f);

    auto rotX = [](float a) { return glm::rotate(glm::mat4(1.0f), a, glm::vec3(1, 0, 0)); };
    auto rotZ = [](float a) { return glm::rotate(glm::mat4(1.0f), a, glm::vec3(0, 0, 1)); };

    localAnim[J_SPINE]      = rotX(0.04f * breathe) * rotZ(0.02f * sway);
    localAnim[J_HEAD]       = rotZ(0.05f * sway);
    localAnim[J_L_UPPERARM] = rotX(0.10f * breathe) * rotZ(-0.08f);
    localAnim[J_R_UPPERARM] = rotX(-0.10f * breathe) * rotZ(0.08f);
    localAnim[J_L_FOREARM]  = rotX(0.06f + 0.05f * breathe);
    localAnim[J_R_FOREARM]  = rotX(0.06f - 0.05f * breathe);

    // Root bob (vertical) — small up/down motion of the whole body.
    const float bob = 0.015f * std::sin(t * 1.6f + 1.5f);

    std::vector<glm::mat4> world(n);
    for (int i = 0; i < n; ++i) {
        glm::vec3 trans = m_joints[i].bindLocalPos;
        if (i == J_PELVIS) trans.y += bob;

        glm::mat4 local = glm::translate(glm::mat4(1.0f), trans) * localAnim[i];
        world[i] = (m_joints[i].parent >= 0) ? world[m_joints[i].parent] * local : local;
    }

    std::vector<glm::mat4> skin(n);
    for (int i = 0; i < n; ++i)
        skin[i] = world[i] * m_inverseBind[i];
    return skin;
}

} // namespace poc
