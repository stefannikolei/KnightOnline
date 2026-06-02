#pragma once

// -----------------------------------------------------------------------------
// Login-screen loader.
//
// Parses a real N3 UI form (`.uif`, e.g. UI_US/el_login_intro_us.uif) and turns
// its image elements into 2D textured quads, reproducing the original login
// screen layout (background tiles + logo + UI art) on a virtual 1024x768 canvas.
//
// It mirrors the binary layout of CN3UIBase::Load and the per-control loaders in
// Client/N3Base (UIImage/UIString/UIButton/UIStatic/UIEdit/UIArea/UIList/...),
// and reuses the shared Reader / resolvePath / loadNTFTexture helpers from
// n3_util.hpp (the same code the character loader uses).
// -----------------------------------------------------------------------------

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

#include <glm/glm.hpp>

#include "n3_util.hpp"

namespace poc {

struct UIVertex {
    glm::vec2 pos; // position in virtual-canvas pixels (Y down)
    glm::vec2 uv;
};

struct UISubMesh {
    uint32_t firstIndex   = 0;
    uint32_t indexCount   = 0;
    int      textureIndex = -1;
};

class LoginScreen {
public:
    bool load(const std::string& dataRoot, const std::string& uifRel);

    const std::vector<UIVertex>&    vertices()  const { return m_vertices; }
    const std::vector<uint32_t>&    indices()   const { return m_indices; }
    const std::vector<UISubMesh>&   subMeshes() const { return m_subMeshes; }
    const std::vector<TextureData>& textures()  const { return m_textures; }

    float canvasWidth()  const { return m_canvasW; }
    float canvasHeight() const { return m_canvasH; }
    bool  valid()        const { return !m_vertices.empty(); }

private:
    // RECT (DirectX): left, top, right, bottom (LONG).
    struct Rect { int32_t l = 0, t = 0, r = 0, b = 0; };

    // Recursive .uif parsing (mirrors CN3UIBase::Load + control extras).
    Rect parseBase(Reader& r);
    void parseNode(Reader& r, int32_t type);

    int  loadTexture(const std::string& dataRoot, const std::string& texRel);
    void addImageQuad(const Rect& rc, const float uv[4], int textureIndex);

    std::string                          m_dataRoot;
    std::vector<UIVertex>                m_vertices;
    std::vector<uint32_t>                m_indices;
    std::vector<UISubMesh>               m_subMeshes;
    std::vector<TextureData>             m_textures;
    std::unordered_map<std::string, int> m_texCache;

    float m_canvasW = 1024.0f;
    float m_canvasH = 768.0f;
};

} // namespace poc
