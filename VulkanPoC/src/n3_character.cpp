#include "n3_character.hpp"

#include <algorithm>
#include <cstring>
#include <functional>
#include <iostream>

namespace poc {

// Reader, resolvePath and loadNTFTexture are shared with the login-screen
// loader (see n3_util.hpp) so the binary parsing is written once.
namespace {

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
    TextureData tex;
    if (!loadNTFTexture(path, tex)) return -1;
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
