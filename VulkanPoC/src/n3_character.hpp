#pragma once

// -----------------------------------------------------------------------------
// Loader for a real Knight Online / N3 engine character.
//
// Parses the actual game assets shipped via the `Client/Data` submodule
// (ko-client-assets) and turns them into GPU-ready data for the Vulkan PoC:
//
//   .n3chr     container        -> joint file + part files + animation file
//   .n3joint   skeleton         -> joint hierarchy with TRS + animation keys
//   .n3cpart   part descriptor  -> texture name + skin-mesh name
//   .n3cskins  skinned mesh      -> CN3Skin (4 LODs; we use LOD 0)
//   .dxt       NTF texture       -> DXT1/BC1 block data (decoded to RGBA8)
//   .n3anim    animation control -> named frame ranges (we play the first one)
//
// The math is done with the engine's own MathUtils library (row-vector /
// DirectX convention) so the skinning is identical to CN3Chr::BuildMesh().
// -----------------------------------------------------------------------------

#include <array>
#include <cstdint>
#include <string>
#include <vector>

#include <glm/glm.hpp>

#include <MathUtils/MathUtils.h>

namespace poc {

struct Vertex {
    glm::vec3  pos;     // bind-pose position (model space) == __VertexSkinned::vOrigin
    glm::vec3  normal;
    glm::vec2  uv;
    glm::ivec4 joints;  // influencing joint indices (preorder skeleton index)
    glm::vec4  weights; // matching blend weights (sum == 1)
};

// A contiguous range of the index buffer that shares one texture (one CN3CPart).
struct SubMesh {
    uint32_t firstIndex  = 0;
    uint32_t indexCount  = 0;
    int      textureIndex = -1; // index into textures(); -1 => untextured
};

struct TextureData {
    int                  width  = 0;
    int                  height = 0;
    std::vector<uint8_t> rgba;  // width*height*4, R8G8B8A8
};

static constexpr int kMaxBones = 128;

class N3Character {
public:
    // dataRoot   : path to Client/Data (with trailing slash), e.g. "Client/Data/"
    // n3chrRel   : character file relative to it, e.g. "Chr/npc_el_knight.n3chr"
    bool load(const std::string& dataRoot, const std::string& n3chrRel);

    const std::vector<Vertex>&      vertices()  const { return m_vertices; }
    const std::vector<uint32_t>&    indices()   const { return m_indices; }
    const std::vector<SubMesh>&     subMeshes() const { return m_subMeshes; }
    const std::vector<TextureData>& textures()  const { return m_textures; }

    int   jointCount() const { return static_cast<int>(m_joints.size()); }
    bool  valid()      const { return !m_vertices.empty() && !m_joints.empty(); }

    // Animation playback range (frame units, 30 fps standard).
    float frameStart()  const { return m_frmStart; }
    float frameEnd()    const { return m_frmEnd; }
    float framesPerSec()const { return m_frmPerSec; }

    // Per-joint skinning matrices (inverseBind * animatedWorld), row-major /
    // DirectX layout — upload raw to the shader (see vulkan_app.cpp).
    // Equivalent to CN3Chr::TickJoints() + the matrix setup in BuildMesh().
    std::vector<std::array<float, 16>> skinningMatrices(float frame) const;

    glm::vec3 boundsMin() const { return m_boundsMin; }
    glm::vec3 boundsMax() const { return m_boundsMax; }

private:
    template <typename T>
    struct AnimKey {
        std::vector<T> data;
        float          rate = 30.0f;
        bool empty() const { return data.empty(); }
    };

    struct Joint {
        int                     parent = -1;
        __Vector3               vPos{};
        __Quaternion            qRot{};
        __Vector3               vScale{ 1, 1, 1 };
        AnimKey<__Vector3>      keyPos;
        AnimKey<__Quaternion>   keyRot;
        AnimKey<__Vector3>      keyScale;
        AnimKey<__Quaternion>   keyOrient;
    };

    // --- joint evaluation (mirrors CN3Joint) ---
    __Matrix44 localMatrix(const Joint& j, float frame) const;
    void       computeWorld(std::vector<__Matrix44>& world, float frame) const;

    // --- asset parsing ---
    bool loadJoints(const std::string& path);
    bool loadPart(const std::string& dataRoot, const std::string& cpartRel);
    bool loadSkin(const std::string& path, int& outTextureIndex,
                  const std::string& texRel, const std::string& dataRoot);
    int  loadTexture(const std::string& path);

    std::vector<Joint>      m_joints;       // preorder (matches FindPointerByID order)
    std::vector<__Matrix44> m_inverseBind;  // inverse of bind-pose (frame 0) world

    std::vector<Vertex>      m_vertices;
    std::vector<uint32_t>    m_indices;
    std::vector<SubMesh>     m_subMeshes;
    std::vector<TextureData> m_textures;

    float m_frmStart  = 0.0f;
    float m_frmEnd    = 0.0f;
    float m_frmPerSec = 30.0f;

    glm::vec3 m_boundsMin{ 0 };
    glm::vec3 m_boundsMax{ 0 };
};

} // namespace poc
