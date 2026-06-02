#pragma once

// -----------------------------------------------------------------------------
// Shared helpers for loading N3 assets: an in-memory binary reader, a
// case-insensitive path resolver, and the NTF/DXT texture loader (used by both
// the character loader and the login-screen UI loader).
// -----------------------------------------------------------------------------

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>

namespace poc {

// Decoded texture (RGBA8, top-down).
struct TextureData {
    int                  width  = 0;
    int                  height = 0;
    std::vector<uint8_t> rgba;
};

// In-memory binary reader (all N3 asset files are small enough to slurp).
struct Reader {
    std::vector<uint8_t> buf;
    size_t               pos = 0;

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
    int32_t  i32() { int32_t v = 0;  read(&v, 4); return v; }
    int16_t  i16() { int16_t v = 0;  read(&v, 2); return v; }
    float    f32() { float v = 0;    read(&v, 4); return v; }

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
// data root. Works on case-insensitive (macOS/Windows) and case-sensitive
// (Linux) filesystems.
std::string resolvePath(const std::string& dataRoot, std::string rel);

// Load and decode an NTF (.dxt) texture to RGBA8. Supports DXT1/3/5 (decoded on
// the CPU, because Apple GPUs / MoltenVK don't support S3TC/BC formats) and the
// uncompressed 16-bit formats used by the UI (A4R4G4B4 / A1R5G5B5 / R5G6B5) and
// A8R8G8B8. Returns false on failure (e.g. encrypted NTF v7).
bool loadNTFTexture(const std::string& path, TextureData& out);

} // namespace poc
