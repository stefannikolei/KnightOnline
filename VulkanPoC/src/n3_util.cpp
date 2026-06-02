#include "n3_util.hpp"

#include <filesystem>
#include <iostream>

namespace fs = std::filesystem;

namespace poc {

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
        if (!found) cur = candidate; // let the caller see a clear failing path
    }
    return cur.string();
}

// ===========================================================================
// NTF (.dxt) texture decoding
// ===========================================================================
namespace {

void rgb565(uint16_t c, int& r, int& g, int& b) {
    r = ((c >> 11) & 0x1F) * 255 / 31;
    g = ((c >> 5) & 0x3F) * 255 / 63;
    b = (c & 0x1F) * 255 / 31;
}

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
        r[3] = g[3] = b[3] = 0; a[3] = 0;
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

constexpr uint32_t kFourCC(char a, char b, char c, char d) {
    return uint32_t(uint8_t(a)) | (uint32_t(uint8_t(b)) << 8) |
           (uint32_t(uint8_t(c)) << 16) | (uint32_t(uint8_t(d)) << 24);
}
constexpr uint32_t FMT_DXT1 = kFourCC('D', 'X', 'T', '1');
constexpr uint32_t FMT_DXT3 = kFourCC('D', 'X', 'T', '3');
constexpr uint32_t FMT_DXT5 = kFourCC('D', 'X', 'T', '5');

// D3DFORMAT values for the uncompressed surfaces stored by the UI textures.
constexpr uint32_t D3DFMT_A8R8G8B8 = 21;
constexpr uint32_t D3DFMT_R5G6B5   = 23;
constexpr uint32_t D3DFMT_A1R5G5B5 = 25;
constexpr uint32_t D3DFMT_A4R4G4B4 = 26;

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

            uint8_t alpha[16];
            if (dxt1) {
                for (int i = 0; i < 16; ++i) alpha[i] = texel[i][3];
            } else if (dxt3) {
                for (int i = 0; i < 16; ++i) {
                    int nib = (p[i / 2] >> ((i & 1) * 4)) & 0x0F;
                    alpha[i] = static_cast<uint8_t>(nib * 17);
                }
            } else {
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

// Uncompressed 16-bit ARGB surfaces (UI textures).
bool decode16(uint32_t fmt, const uint8_t* data, size_t dataSize,
              int w, int h, std::vector<uint8_t>& rgba) {
    if (static_cast<size_t>(w) * h * 2 > dataSize) return false;
    rgba.assign(static_cast<size_t>(w) * h * 4, 0);
    const auto* px = reinterpret_cast<const uint16_t*>(data);
    for (int i = 0; i < w * h; ++i) {
        uint16_t p = px[i];
        int r, g, b, a = 255;
        if (fmt == D3DFMT_A4R4G4B4) {
            a = ((p >> 12) & 0xF) * 17;
            r = ((p >> 8) & 0xF) * 17;
            g = ((p >> 4) & 0xF) * 17;
            b = (p & 0xF) * 17;
        } else if (fmt == D3DFMT_A1R5G5B5) {
            a = (p & 0x8000) ? 255 : 0;
            r = ((p >> 10) & 0x1F) * 255 / 31;
            g = ((p >> 5) & 0x1F) * 255 / 31;
            b = (p & 0x1F) * 255 / 31;
        } else { // R5G6B5
            r = ((p >> 11) & 0x1F) * 255 / 31;
            g = ((p >> 5) & 0x3F) * 255 / 63;
            b = (p & 0x1F) * 255 / 31;
        }
        rgba[i * 4 + 0] = static_cast<uint8_t>(r);
        rgba[i * 4 + 1] = static_cast<uint8_t>(g);
        rgba[i * 4 + 2] = static_cast<uint8_t>(b);
        rgba[i * 4 + 3] = static_cast<uint8_t>(a);
    }
    return true;
}

bool decode32(const uint8_t* data, size_t dataSize, int w, int h, std::vector<uint8_t>& rgba) {
    if (static_cast<size_t>(w) * h * 4 > dataSize) return false;
    rgba.assign(static_cast<size_t>(w) * h * 4, 0);
    for (int i = 0; i < w * h; ++i) {
        // D3DFMT_A8R8G8B8 in memory (little-endian) is B,G,R,A.
        rgba[i * 4 + 0] = data[i * 4 + 2];
        rgba[i * 4 + 1] = data[i * 4 + 1];
        rgba[i * 4 + 2] = data[i * 4 + 0];
        rgba[i * 4 + 3] = data[i * 4 + 3];
    }
    return true;
}

} // namespace

bool loadNTFTexture(const std::string& path, TextureData& out) {
    Reader r;
    if (!r.open(path)) return false;

    (void)r.str(); // CN3BaseFileAccess name

    char id[4];
    if (!r.read(id, 4)) return false;
    int w = r.i32();
    int h = r.i32();
    uint32_t fmt = static_cast<uint32_t>(r.i32());
    (void)r.i32(); // bMipMap

    if (id[0] != 'N' || id[1] != 'T' || id[2] != 'F') return false;
    if (id[3] == 7) {
        std::cerr << "Encrypted texture (NTF v7) not supported: " << path << "\n";
        return false;
    }
    if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return false;

    const uint8_t* data = r.buf.data() + r.pos;
    size_t avail = r.buf.size() - r.pos;

    out.width  = w;
    out.height = h;

    if (fmt == FMT_DXT1 || fmt == FMT_DXT3 || fmt == FMT_DXT5)
        return decodeDXT(fmt, data, avail, w, h, out.rgba);
    if (fmt == D3DFMT_A4R4G4B4 || fmt == D3DFMT_A1R5G5B5 || fmt == D3DFMT_R5G6B5)
        return decode16(fmt, data, avail, w, h, out.rgba);
    if (fmt == D3DFMT_A8R8G8B8)
        return decode32(data, avail, w, h, out.rgba);

    std::cerr << "Unsupported texture format " << fmt << " in " << path << "\n";
    return false;
}

} // namespace poc
