#include "n3_character.hpp"

#include <algorithm>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>

namespace fs = std::filesystem;

namespace poc {

// ===========================================================================
// Small in-memory binary reader (all N3 asset files are small).
// ===========================================================================
namespace {

struct Reader {
    std::vector<uint8_t> buf;
    size_t pos = 0;

    bool open(const std::string& path) {
        std::ifstream f(path, std::ios::binary | std::ios::ate);
        if (!f) return false;
        std::streamsize n = f.tellg();
        f.seekg(0);
        buf.resize(static_cast<size_t>(n));
        f.read(reinterpret_cast<char*>(buf.data()), n);
        pos = 0;
        return true;
    }
    bool eof() const { return pos >= buf.size(); }
    void skip(size_t n) { pos += n; }
    bool read(void* dst, size_t n) {
        if (pos + n > buf.size()) return false;
        std::memcpy(dst, buf.data() + pos, n);
        pos += n;
        return true;
    }
    int32_t i32() { int32_t v = 0; read(&v, 4); return v; }
    float   f32() { float v = 0;   read(&v, 4); return v; }

    // Length-prefixed string (CN3BaseFileAccess style): int length + bytes.
    std::string str() {
        int n = i32();
        if (n <= 0) return {};
        std::string s(static_cast<size_t>(n), '\0');
        read(&s[0], static_cast<size_t>(n));
        return s;
    }
};

// Resolve a (possibly wrong-case, backslash-separated) asset path against the
// data root. macOS/Windows are case-insensitive; this also makes it work on
// case-sensitive Linux by matching each path component case-insensitively.
std::string resolvePath(const std::string& dataRoot, std::string rel) {
    std::replace(rel.begin(), rel.end(), '\\', '/');

    fs::path base = dataRoot.empty() ? fs::path(".") : fs::path(dataRoot);
    fs::path cur  = base;

    size_t start = 0;
    while (start < rel.size()) {
        size_t slash = rel.find('/', start);
        std::string comp = rel.substr(start, slash == std::string::npos
                                                  ? std::string::npos
                                                  : slash - start);
        start = (slash == std::string::npos) ? rel.size() : slash + 1;
        if (comp.empty() || comp == ".") continue;

        fs::path candidate = cur / comp;
        if (fs::exists(candidate)) {
            cur = candidate;
            continue;
        }
        // Case-insensitive fallback.
        bool found = false;
        std::error_code ec;
        if (fs::is_directory(cur, ec)) {
            std::string lower = comp;
            std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);
            for (const auto& e : fs::directory_iterator(cur, ec)) {
                std::string name = e.path().filename().string();
                std::string nlow = name;
                std::transform(nlow.begin(), nlow.end(), nlow.begin(), ::tolower);
                if (nlow == lower) { cur = e.path(); found = true; break; }
            }
        }
        if (!found) cur = candidate; // let the caller's open() fail with a clear path
    }
    return cur.string();
}

// --- DXT/BC block decoding (to RGBA8) --------------------------------------
void rgb565(uint16_t c, int& r, int& g, int& b) {
    r = ((c >> 11) & 0x1F) * 255 / 31;
    g = ((c >> 5) & 0x3F) * 255 / 63;
    b = (c & 0x1F) * 255 / 31;
}

// Decode one BC1 (DXT1) colour block; writes RGB (and A for 1-bit transparency).
void decodeBC1Color(const uint8_t* blk, uint8_t out[16][4]) {
    uint16_t c0 = blk[0] | (blk[1] << 8);
    uint16_t c1 = blk[2] | (blk[3] << 8);
    int r[4], g[4], b[4], a[4] = { 255, 255, 255, 255 };
    rgb565(c0, r[0], g[0], b[0]);
    rgb565(c1, r[1], g[1], b[1]);
    if (c0 > c1) {
        r[2] = (2 * r[0] + r[1]) / 3; g[2] = (2 * g[0] + g[1]) / 3; b[2] = (2 * b[0] + b[1]) / 3;
        r[3] = (r[0] + 2 * r[1]) / 3; g[3] = (g[0] + 2 * g[1]) / 3; b[3] = (b[0] + 2 * b[1]) / 3;
    } else {
        r[2] = (r[0] + r[1]) / 2; g[2] = (g[0] + g[1]) / 2; b[2] = (b[0] + b[1]) / 2;
        r[3] = g[3] = b[3] = 0; a[3] = 0; // transparent
    }
    uint32_t bits = blk[4] | (blk[5] << 8) | (blk[6] << 16) | (blk[7] << 24);
    for (int i = 0; i < 16; ++i) {
        int idx = (bits >> (2 * i)) & 0x3;
        out[i][0] = static_cast<uint8_t>(r[idx]);
        out[i][1] = static_cast<uint8_t>(g[idx]);
        out[i][2] = static_cast<uint8_t>(b[idx]);
        out[i][3] = static_cast<uint8_t>(a[idx]);
    }
}

// fourcc helpers
constexpr uint32_t kFourCC(char a, char b, char c, char d) {
    return uint32_t(uint8_t(a)) | (uint32_t(uint8_t(b)) << 8) |
           (uint32_t(uint8_t(c)) << 16) | (uint32_t(uint8_t(d)) << 24);
}
constexpr uint32_t FMT_DXT1 = kFourCC('D', 'X', 'T', '1');
constexpr uint32_t FMT_DXT3 = kFourCC('D', 'X', 'T', '3');
constexpr uint32_t FMT_DXT5 = kFourCC('D', 'X', 'T', '5');

bool decodeDXT(uint32_t fourcc, const uint8_t* data, size_t dataSize,
               int w, int h, std::vector<uint8_t>& rgba) {
    const bool dxt1 = (fourcc == FMT_DXT1);
    const bool dxt3 = (fourcc == FMT_DXT3);
    const bool dxt5 = (fourcc == FMT_DXT5);
    if (!dxt1 && !dxt3 && !dxt5) return false;

    const int blockBytes = dxt1 ? 8 : 16;
    const int bw = (w + 3) / 4;
    const int bh = (h + 3) / 4;
    if (static_cast<size_t>(bw) * bh * blockBytes > dataSize) return false;

    rgba.assign(static_cast<size_t>(w) * h * 4, 0);

    const uint8_t* p = data;
    for (int by = 0; by < bh; ++by) {
        for (int bx = 0; bx < bw; ++bx, p += blockBytes) {
            const uint8_t* colorBlk = dxt1 ? p : (p + 8);
            uint8_t texel[16][4];
            decodeBC1Color(colorBlk, texel);

            // Alpha block.
            uint8_t alpha[16];
            if (dxt1) {
                for (int i = 0; i < 16; ++i) alpha[i] = texel[i][3];
            } else if (dxt3) {
                for (int i = 0; i < 16; ++i) {
                    int nib = (p[i / 2] >> ((i & 1) * 4)) & 0x0F;
                    alpha[i] = static_cast<uint8_t>(nib * 17);
                }
            } else { // dxt5
                int a0 = p[0], a1 = p[1];
                uint64_t abits = 0;
                for (int k = 0; k < 6; ++k) abits |= uint64_t(p[2 + k]) << (8 * k);
                int aLut[8];
                aLut[0] = a0; aLut[1] = a1;
                if (a0 > a1) {
                    for (int k = 1; k <= 6; ++k) aLut[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                } else {
                    for (int k = 1; k <= 4; ++k) aLut[k + 1] = ((5 - k) * a0 + k * a1) / 5;
                    aLut[6] = 0; aLut[7] = 255;
                }
                for (int i = 0; i < 16; ++i) alpha[i] = static_cast<uint8_t>(aLut[(abits >> (3 * i)) & 0x7]);
            }

            for (int py = 0; py < 4; ++py) {
                for (int px = 0; px < 4; ++px) {
                    int x = bx * 4 + px, y = by * 4 + py;
                    if (x >= w || y >= h) continue;
                    int i = py * 4 + px;
                    size_t o = (static_cast<size_t>(y) * w + x) * 4;
                    rgba[o + 0] = texel[i][0];
                    rgba[o + 1] = texel[i][1];
                    rgba[o + 2] = texel[i][2];
                    rgba[o + 3] = alpha[i];
                }
            }
        }
    }
    return true;
}

// --- animation-key sampling (mirrors CN3AnimKey::DataGet) -------------------
__Vector3 lerp(const __Vector3& a, const __Vector3& b, float d) {
    return __Vector3{ a.x + (b.x - a.x) * d, a.y + (b.y - a.y) * d, a.z + (b.z - a.z) * d };
}

} // namespace

template <typename T>
static T sampleKey(const std::vector<T>& data, float rate, float frame, const T& fallback);

template <>
__Vector3 sampleKey<__Vector3>(const std::vector<__Vector3>& data, float rate, float frame,
                               const __Vector3& fallback) {
    if (data.empty()) return fallback;
    const int count = static_cast<int>(data.size());
    const float fD = 30.0f / rate;
    int idx = static_cast<int>(frame * (rate / 30.0f));
    if (idx < 0) idx = 0;
    if (idx >= count - 1) return data[count - 1];
    float delta = (frame - idx * fD) / fD;
    if (delta <= 0.0f) return data[idx];
    return lerp(data[idx], data[idx + 1], delta);
}

template <>
__Quaternion sampleKey<__Quaternion>(const std::vector<__Quaternion>& data, float rate, float frame,
                                     const __Quaternion& fallback) {
    if (data.empty()) return fallback;
    const int count = static_cast<int>(data.size());
    const float fD = 30.0f / rate;
    int idx = static_cast<int>(frame * (rate / 30.0f));
    if (idx < 0) idx = 0;
    if (idx >= count - 1) return data[count - 1];
    float delta = (frame - idx * fD) / fD;
    if (delta <= 0.0f) return data[idx];
    __Quaternion q;
    q.Slerp(data[idx], data[idx + 1], delta);
    return q;
}

// ===========================================================================
// Joint evaluation (mirrors CN3Joint::ReCalcMatrix / Tick)
// ===========================================================================
__Matrix44 N3Character::localMatrix(const Joint& j, float frame) const {
    __Vector3    vPos   = sampleKey<__Vector3>(j.keyPos.data, j.keyPos.rate, frame, j.vPos);
    __Quaternion qRot   = sampleKey<__Quaternion>(j.keyRot.data, j.keyRot.rate, frame, j.qRot);
    __Vector3    vScale = sampleKey<__Vector3>(j.keyScale.data, j.keyScale.rate, frame, j.vScale);

    __Matrix44 m;
    if (!j.keyOrient.empty()) {
        __Quaternion qOrient = sampleKey<__Quaternion>(j.keyOrient.data, j.keyOrient.rate, frame,
                                                       __Quaternion{ 0, 0, 0, 1 });
        m = (qRot * qOrient); // __Matrix44::operator=(__Quaternion)
    } else {
        m = qRot;
    }

    if (vScale.x != 1.0f || vScale.y != 1.0f || vScale.z != 1.0f) {
        __Matrix44 s;
        s.Identity();
        s.Scale(vScale);
        m *= s;
    }
    m.PosSet(vPos);
    return m;
}

void N3Character::computeWorld(std::vector<__Matrix44>& world, float frame) const {
    const int n = static_cast<int>(m_joints.size());
    world.resize(n);
    for (int i = 0; i < n; ++i) {
        __Matrix44 local = localMatrix(m_joints[i], frame);
        if (m_joints[i].parent >= 0)
            world[i] = local * world[m_joints[i].parent]; // child = local * parent (DX order)
        else
            world[i] = local;
    }
}

std::vector<std::array<float, 16>> N3Character::skinningMatrices(float frame) const {
    std::vector<__Matrix44> world;
    computeWorld(world, frame);

    const int n = static_cast<int>(m_joints.size());
    std::vector<std::array<float, 16>> out(n);
    for (int i = 0; i < n; ++i) {
        __Matrix44 s = m_inverseBind[i] * world[i]; // v * invBind * world
        std::memcpy(out[i].data(), s.m, sizeof(float) * 16);
    }
    return out;
}

// ===========================================================================
// Asset parsing
// ===========================================================================
namespace {
// Reads a CN3Transform body (without the trailing joint/child data).
template <typename V, typename Q>
void readAnimKeyV(Reader& r, std::vector<V>& out, float& rate) {
    int count = r.i32();
    if (count <= 0) return;
    int type = r.i32(); (void)type;
    rate = r.f32();
    out.resize(static_cast<size_t>(count));
    r.read(out.data(), sizeof(V) * count);
}
template <typename Q>
void readAnimKeyQ(Reader& r, std::vector<Q>& out, float& rate) {
    int count = r.i32();
    if (count <= 0) return;
    int type = r.i32(); (void)type;
    rate = r.f32();
    out.resize(static_cast<size_t>(count));
    r.read(out.data(), sizeof(Q) * count);
}
} // namespace

bool N3Character::loadJoints(const std::string& path) {
    Reader r;
    if (!r.open(path)) {
        std::cerr << "Failed to open joint file: " << path << "\n";
        return false;
    }

    // Recursive joint reader producing a preorder flat list.
    std::function<void(int)> readJoint = [&](int parent) {
        Joint j;
        j.parent = parent;

        // CN3Transform::Load
        (void)r.str(); // name
        r.read(&j.vPos, sizeof(__Vector3));
        r.read(&j.qRot, sizeof(__Quaternion));
        r.read(&j.vScale, sizeof(__Vector3));
        readAnimKeyV<__Vector3, void>(r, j.keyPos.data, j.keyPos.rate);
        readAnimKeyQ<__Quaternion>(r, j.keyRot.data, j.keyRot.rate);
        readAnimKeyV<__Vector3, void>(r, j.keyScale.data, j.keyScale.rate);

        // CN3Joint::Load
        readAnimKeyQ<__Quaternion>(r, j.keyOrient.data, j.keyOrient.rate);

        int myIndex = static_cast<int>(m_joints.size());
        m_joints.push_back(j);

        int childCount = r.i32();
        for (int c = 0; c < childCount; ++c)
            readJoint(myIndex);
    };

    readJoint(-1);

    // Bind pose == frame 0 (CN3Chr::Init does m_pRootJointRef->Tick(0)).
    std::vector<__Matrix44> bindWorld;
    computeWorld(bindWorld, 0.0f);
    m_inverseBind.resize(m_joints.size());
    for (size_t i = 0; i < m_joints.size(); ++i)
        m_inverseBind[i] = bindWorld[i].Inverse();

    return true;
}

int N3Character::loadTexture(const std::string& path) {
    Reader r;
    if (!r.open(path)) return -1;

    (void)r.str(); // name (CN3BaseFileAccess::Load)

    char id[4];
    if (!r.read(id, 4)) return -1;
    int w = r.i32();
    int h = r.i32();
    uint32_t fmt = static_cast<uint32_t>(r.i32());
    (void)r.i32(); // bMipMap

    if (id[0] != 'N' || id[1] != 'T' || id[2] != 'F') return -1;
    if (id[3] == 7) {
        std::cerr << "Encrypted texture (NTF v7) not supported: " << path << "\n";
        return -1;
    }
    if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return -1;

    const bool dxt1 = (fmt == FMT_DXT1);
    const int baseBytes = dxt1 ? (w * h / 2) : (w * h); // DXT1: 4bpp, DXT3/5: 8bpp
    if (r.pos + static_cast<size_t>(baseBytes) > r.buf.size()) return -1;

    TextureData tex;
    tex.width  = w;
    tex.height = h;
    if (!decodeDXT(fmt, r.buf.data() + r.pos, baseBytes, w, h, tex.rgba)) {
        std::cerr << "Unsupported texture format in " << path << "\n";
        return -1;
    }

    m_textures.push_back(std::move(tex));
    return static_cast<int>(m_textures.size()) - 1;
}

bool N3Character::loadSkin(const std::string& path, int& /*outTextureIndex*/,
                           const std::string& /*texRel*/, const std::string& /*dataRoot*/) {
    Reader r;
    if (!r.open(path)) {
        std::cerr << "Failed to open skins file: " << path << "\n";
        return false;
    }

    (void)r.str(); // CN3CPartSkins name

    // Only LOD 0 is needed (4 LODs are stored sequentially; we stop after 0).
    // CN3Skin::Load == CN3IMesh::Load + per-vertex skin data.
    (void)r.str(); // CN3IMesh name
    int nFC  = r.i32();
    int nVC  = r.i32();
    int nUVC = r.i32();
    if (nFC <= 0 || nVC <= 0) {
        std::cerr << "Empty skin LOD0 in " << path << "\n";
        return false;
    }

    struct VtxXyzN { __Vector3 p; __Vector3 n; };
    std::vector<VtxXyzN> verts(static_cast<size_t>(nVC));
    r.read(verts.data(), sizeof(VtxXyzN) * nVC);

    std::vector<uint16_t> vtxIdx(static_cast<size_t>(nFC) * 3);
    r.read(vtxIdx.data(), sizeof(uint16_t) * vtxIdx.size());

    std::vector<float>    uvs;
    std::vector<uint16_t> uvIdx;
    if (nUVC > 0) {
        uvs.resize(static_cast<size_t>(nUVC) * 2);
        r.read(uvs.data(), sizeof(float) * uvs.size());
        uvIdx.resize(static_cast<size_t>(nFC) * 3);
        r.read(uvIdx.data(), sizeof(uint16_t) * uvIdx.size());
    }

    // Per-vertex skinning data (CN3Skin::Load tail).
    struct Skin4 { int j[4]; float w[4]; int affect; };
    std::vector<Skin4> skin(static_cast<size_t>(nVC));
    for (int i = 0; i < nVC; ++i) {
        __Vector3 vOrigin;
        r.read(&vOrigin, sizeof(__Vector3)); // == verts[i].p (bind pose)
        int nAffect = r.i32();
        r.skip(8); // two unused 32-bit pointers in the file

        Skin4 s{};
        s.affect = 0;
        if (nAffect > 1) {
            std::vector<int>   jn(nAffect);
            std::vector<float> wt(nAffect);
            r.read(jn.data(), sizeof(int) * nAffect);
            r.read(wt.data(), sizeof(float) * nAffect);
            // Keep the (up to) 4 strongest influences.
            std::vector<int> order(nAffect);
            for (int k = 0; k < nAffect; ++k) order[k] = k;
            std::sort(order.begin(), order.end(),
                      [&](int a, int b) { return wt[a] > wt[b]; });
            int use = std::min(4, nAffect);
            float sum = 0.0f;
            for (int k = 0; k < use; ++k) { s.j[k] = jn[order[k]]; s.w[k] = wt[order[k]]; sum += wt[order[k]]; }
            if (sum > 0.0f) for (int k = 0; k < use; ++k) s.w[k] /= sum;
            s.affect = use;
        } else if (nAffect == 1) {
            int jn = r.i32();
            s.j[0] = jn; s.w[0] = 1.0f; s.affect = 1;
        }
        skin[i] = s;
    }

    // Texture for this part is resolved by the caller; record the submesh range.
    uint32_t firstIndex = static_cast<uint32_t>(m_indices.size());

    auto clampJoint = [&](int j) {
        if (j < 0) return 0;
        if (j >= jointCount()) return jointCount() - 1;
        return j;
    };

    for (int f = 0; f < nFC; ++f) {
        for (int c = 0; c < 3; ++c) {
            int vi = vtxIdx[static_cast<size_t>(f) * 3 + c];
            if (vi < 0 || vi >= nVC) vi = 0;

            Vertex v{};
            v.pos    = glm::vec3(verts[vi].p.x, verts[vi].p.y, verts[vi].p.z);
            v.normal = glm::vec3(verts[vi].n.x, verts[vi].n.y, verts[vi].n.z);
            if (nUVC > 0) {
                int ui = uvIdx[static_cast<size_t>(f) * 3 + c];
                if (ui < 0 || ui >= nUVC) ui = 0;
                v.uv = glm::vec2(uvs[static_cast<size_t>(ui) * 2 + 0],
                                 uvs[static_cast<size_t>(ui) * 2 + 1]);
            }
            const Skin4& s = skin[vi];
            v.joints = glm::ivec4(clampJoint(s.j[0]), clampJoint(s.j[1]),
                                  clampJoint(s.j[2]), clampJoint(s.j[3]));
            v.weights = glm::vec4(s.w[0], s.w[1], s.w[2], s.w[3]);
            if (s.affect == 0) { v.weights = glm::vec4(1, 0, 0, 0); v.joints = glm::ivec4(0); }

            m_indices.push_back(static_cast<uint32_t>(m_vertices.size()));
            m_vertices.push_back(v);
        }
    }

    SubMesh sm;
    sm.firstIndex = firstIndex;
    sm.indexCount = static_cast<uint32_t>(m_indices.size()) - firstIndex;
    sm.textureIndex = -1; // set by caller
    m_subMeshes.push_back(sm);
    return true;
}

bool N3Character::loadPart(const std::string& dataRoot, const std::string& cpartRel) {
    Reader r;
    std::string path = resolvePath(dataRoot, cpartRel);
    if (!r.open(path)) {
        std::cerr << "Failed to open part file: " << path << "\n";
        return false;
    }

    (void)r.str();      // CN3CPart name
    (void)r.i32();      // m_dwReserved
    r.skip(92);         // __Material (sizeof _D3DMATERIAL9 + 6 * uint32 = 92)
    std::string texRel   = r.str();
    std::string skinsRel = r.str();

    if (skinsRel.empty()) return false;

    int dummyTex = -1;
    if (!loadSkin(resolvePath(dataRoot, skinsRel), dummyTex, texRel, dataRoot))
        return false;

    int texIndex = -1;
    if (!texRel.empty())
        texIndex = loadTexture(resolvePath(dataRoot, texRel));

    m_subMeshes.back().textureIndex = texIndex;
    return true;
}

bool N3Character::load(const std::string& dataRoot, const std::string& n3chrRel) {
    Reader r;
    std::string chrPath = resolvePath(dataRoot, n3chrRel);
    if (!r.open(chrPath)) {
        std::cerr << "Failed to open character file: " << chrPath << "\n";
        return false;
    }

    // CN3TransformCollision::Load (top-level character transform; usually identity).
    (void)r.str(); // name
    __Vector3 vPos, vScale; __Quaternion qRot;
    r.read(&vPos, sizeof(__Vector3));
    r.read(&qRot, sizeof(__Quaternion));
    r.read(&vScale, sizeof(__Vector3));
    { std::vector<__Vector3> t; float rate; readAnimKeyV<__Vector3, void>(r, t, rate); }   // KeyPos
    { std::vector<__Quaternion> t; float rate; readAnimKeyQ<__Quaternion>(r, t, rate); }   // KeyRot
    { std::vector<__Vector3> t; float rate; readAnimKeyV<__Vector3, void>(r, t, rate); }   // KeyScale
    (void)r.str(); // collision mesh name
    (void)r.str(); // climb mesh name

    // Joint file.
    std::string jointRel = r.str();
    if (jointRel.empty() || !loadJoints(resolvePath(dataRoot, jointRel))) {
        std::cerr << "Character has no usable skeleton\n";
        return false;
    }

    // Parts.
    int partCount = r.i32();
    std::vector<std::string> partRels;
    for (int i = 0; i < partCount; ++i) {
        std::string p = r.str();
        if (!p.empty()) partRels.push_back(p);
    }

    // Plugs (weapons etc.) — skipped for the PoC.
    int plugCount = r.i32();
    for (int i = 0; i < plugCount; ++i) (void)r.str();

    // Animation control.
    std::string animRel = r.str();

    for (const auto& p : partRels)
        loadPart(dataRoot, p);

    if (m_vertices.empty()) {
        std::cerr << "Character has no renderable parts\n";
        return false;
    }

    // Bounds (bind pose).
    m_boundsMin = glm::vec3(1e9f);
    m_boundsMax = glm::vec3(-1e9f);
    for (const auto& v : m_vertices) {
        m_boundsMin = glm::min(m_boundsMin, v.pos);
        m_boundsMax = glm::max(m_boundsMax, v.pos);
    }

    // Animation range: play the first defined animation (typically idle/stand).
    if (!animRel.empty()) {
        Reader ar;
        if (ar.open(resolvePath(dataRoot, animRel))) {
            int count = ar.i32();
            if (count > 0) {
                (void)ar.i32();                 // dummy string-pointer slot
                m_frmStart  = ar.f32();
                m_frmEnd    = ar.f32();
                m_frmPerSec = ar.f32();
                if (m_frmPerSec <= 0.0f) m_frmPerSec = 30.0f;
            }
        }
    }
    if (m_frmEnd <= m_frmStart) {
        // Fall back to the full key range found on the root joint.
        float maxFrames = 0.0f;
        for (const auto& j : m_joints) {
            float c = static_cast<float>(j.keyRot.data.size());
            if (!j.keyRot.empty()) c = c * 30.0f / j.keyRot.rate;
            maxFrames = std::max(maxFrames, c);
        }
        m_frmStart = 0.0f;
        m_frmEnd   = (maxFrames > 1.0f) ? maxFrames : 1.0f;
    }

    std::cout << "Loaded character: " << n3chrRel << "\n"
              << "  joints   : " << m_joints.size() << "\n"
              << "  parts    : " << m_subMeshes.size() << "\n"
              << "  vertices : " << m_vertices.size() << "\n"
              << "  textures : " << m_textures.size() << "\n"
              << "  frames   : " << m_frmStart << " .. " << m_frmEnd
              << " @ " << m_frmPerSec << " fps\n";
    return true;
}

} // namespace poc
