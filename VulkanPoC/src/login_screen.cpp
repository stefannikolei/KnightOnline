#include "login_screen.hpp"

#include <algorithm>
#include <iostream>

namespace poc {

// eUI_TYPE values from Client/N3Base/N3UIDef.h.
enum {
    UI_TYPE_BASE = 0,
    UI_TYPE_BUTTON,
    UI_TYPE_STATIC,
    UI_TYPE_PROGRESS,
    UI_TYPE_IMAGE,
    UI_TYPE_SCROLLBAR,
    UI_TYPE_STRING,
    UI_TYPE_TRACKBAR,
    UI_TYPE_EDIT,
    UI_TYPE_AREA,
    UI_TYPE_TOOLTIP,
    UI_TYPE_ICON,
    UI_TYPE_ICON_MANAGER,
    UI_TYPE_ICONSLOT,
    UI_TYPE_LIST,
};

// UI forms are loaded with N3FORMAT_VER_DEFAULT (== N3FORMAT_VER_1264), so the
// version-gated reads in the engine all take their ">= 1264" path.
namespace {
constexpr uint32_t UISTYLE_IMAGE_ANIMATE = 0x00010000;
}

// Reads the common CN3UIBase body (children first, then this node's fields) and
// returns the node's screen rectangle (m_rcRegion).
LoginScreen::Rect LoginScreen::parseBase(Reader& r) {
    (void)r.str(); // CN3BaseFileAccess name

    // children count (int16 + int16 padding in >= 1264 format)
    int16_t cc  = r.i16();
    (void)r.i16();

    for (int i = 0; i < cc; ++i) {
        int32_t childType = r.i32();
        parseNode(r, childType);
    }

    (void)r.str(); // id

    Rect rc;
    r.read(&rc, sizeof(int32_t) * 4); // m_rcRegion
    int32_t mov[4];
    r.read(mov, sizeof(int32_t) * 4); // m_rcMovable
    (void)r.i32();                    // m_dwStyle
    (void)r.i32();                    // m_dwReserved
    (void)r.str();                    // tooltip
    (void)r.str();                    // open sound
    (void)r.str();                    // close sound
    return rc;
}

void LoginScreen::parseNode(Reader& r, int32_t type) {
    Rect rc = parseBase(r);

    switch (type) {
    case UI_TYPE_IMAGE: {
        std::string tex = r.str();
        float uv[4];
        r.read(uv, sizeof(float) * 4); // m_frcUVRect
        (void)r.f32();                 // m_fAnimFrame
        int texIndex = tex.empty() ? -1 : loadTexture(m_dataRoot, tex);
        if (texIndex >= 0)
            addImageQuad(rc, uv, texIndex);
        break;
    }
    case UI_TYPE_STRING: {
        std::string font = r.str();
        if (!font.empty()) { (void)r.i32(); (void)r.i32(); } // height, flags
        (void)r.i32();                                       // color
        (void)r.str();                                       // text
        (void)r.i32();                                       // >= 1264: idk0
        break;
    }
    case UI_TYPE_BUTTON: {
        int32_t rcClick[4];
        r.read(rcClick, sizeof(int32_t) * 4);
        (void)r.str(); // on sound
        (void)r.str(); // click sound
        break;
    }
    case UI_TYPE_STATIC:
        (void)r.str(); // click sound
        break;
    case UI_TYPE_EDIT:
        (void)r.str(); // static's click sound
        (void)r.str(); // typing sound
        break;
    case UI_TYPE_AREA:
        (void)r.i32(); // area type
        break;
    case UI_TYPE_LIST: {
        std::string font = r.str();
        if (!font.empty()) { (void)r.i32(); (void)r.i32(); (void)r.i32(); (void)r.i32(); }
        break;
    }
    case UI_TYPE_BASE:
    case UI_TYPE_PROGRESS:
    case UI_TYPE_SCROLLBAR:
    case UI_TYPE_TRACKBAR:
        // No extra fields beyond the base + children.
        break;
    default:
        std::cerr << "login: unhandled UI type " << type << " at offset " << r.pos << "\n";
        break;
    }
}

int LoginScreen::loadTexture(const std::string& dataRoot, const std::string& texRel) {
    auto it = m_texCache.find(texRel);
    if (it != m_texCache.end()) return it->second;

    TextureData tex;
    int index = -1;
    if (loadNTFTexture(resolvePath(dataRoot, texRel), tex)) {
        m_textures.push_back(std::move(tex));
        index = static_cast<int>(m_textures.size()) - 1;
    }
    m_texCache[texRel] = index;
    return index;
}

void LoginScreen::addImageQuad(const Rect& rc, const float uv[4], int textureIndex) {
    float l = static_cast<float>(rc.l), t = static_cast<float>(rc.t);
    float rr = static_cast<float>(rc.r), b = static_cast<float>(rc.b);
    float uL = uv[0], vT = uv[1], uR = uv[2], vB = uv[3];

    uint32_t base = static_cast<uint32_t>(m_vertices.size());
    m_vertices.push_back({ { l,  t }, { uL, vT } }); // TL
    m_vertices.push_back({ { rr, t }, { uR, vT } }); // TR
    m_vertices.push_back({ { rr, b }, { uR, vB } }); // BR
    m_vertices.push_back({ { l,  b }, { uL, vB } }); // BL

    uint32_t firstIndex = static_cast<uint32_t>(m_indices.size());
    for (uint32_t i : { base + 0, base + 1, base + 2, base + 0, base + 2, base + 3 })
        m_indices.push_back(i);

    // Merge consecutive quads that share a texture into one submesh.
    if (!m_subMeshes.empty() && m_subMeshes.back().textureIndex == textureIndex) {
        m_subMeshes.back().indexCount += 6;
    } else {
        UISubMesh sm;
        sm.firstIndex   = firstIndex;
        sm.indexCount   = 6;
        sm.textureIndex = textureIndex;
        m_subMeshes.push_back(sm);
    }

    m_canvasW = std::max(m_canvasW, rr);
    m_canvasH = std::max(m_canvasH, b);
}

bool LoginScreen::load(const std::string& dataRoot, const std::string& uifRel) {
    m_dataRoot = dataRoot;

    Reader r;
    std::string path = resolvePath(dataRoot, uifRel);
    if (!r.open(path)) {
        std::cerr << "Failed to open login UI form: " << path << "\n";
        return false;
    }

    m_canvasW = 1024.0f;
    m_canvasH = 768.0f;

    // The top-level dialog is a plain CN3UIBase node (no leading type byte).
    parseBase(r);

    if (m_vertices.empty()) {
        std::cerr << "Login form has no image elements: " << path << "\n";
        return false;
    }

    std::cout << "Loaded login screen: " << uifRel << "\n"
              << "  canvas   : " << m_canvasW << " x " << m_canvasH << "\n"
              << "  quads    : " << m_vertices.size() / 4 << "\n"
              << "  draws    : " << m_subMeshes.size() << "\n"
              << "  textures : " << m_textures.size() << "\n";
    return true;
}

} // namespace poc
