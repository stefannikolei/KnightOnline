// -----------------------------------------------------------------------------
// KnightOnline - Vulkan Character PoC
//
// Proof of concept for porting the N3 engine's renderer from DirectX 9 to
// Vulkan. Loads ONE real character from the game assets (the `Client/Data`
// submodule) and renders it with GPU linear-blend skinning, mirroring the data
// model of CN3Chr (skeleton + skinned mesh parts + DXT textures). On macOS it
// runs on top of MoltenVK via the portability extensions (see vulkan_app.cpp).
//
// Press TAB (or L) in the window to switch between the 3D character view and
// the 2D login screen.
//
// Usage:
//   vulkan_character_poc [dataRoot] [characterRelPath] [loginUifPath]
//
//   dataRoot          path to Client/Data (default: compiled-in POC_DATA_DIR)
//   characterRelPath  e.g. "Chr/npc_el_knight.n3chr" (default below)
//   loginUifPath      e.g. "UI_US/el_login_intro_us.uif" (default below)
// -----------------------------------------------------------------------------

#include <cstdlib>
#include <iostream>
#include <string>

#include "vulkan_app.hpp"

#ifndef POC_DATA_DIR
#define POC_DATA_DIR "./"
#endif

int main(int argc, char** argv) {
    std::string dataRoot = (argc > 1) ? argv[1] : POC_DATA_DIR;
    std::string chrRel   = (argc > 2) ? argv[2] : "Chr/npc_el_knight.n3chr";
    std::string loginUif = (argc > 3) ? argv[3] : "UI_US/el_login_intro_us.uif";

    if (!dataRoot.empty() && dataRoot.back() != '/' && dataRoot.back() != '\\')
        dataRoot += '/';

    std::cout << "Data root : " << dataRoot << "\n"
              << "Character : " << chrRel << "\n"
              << "Login UI  : " << loginUif << "\n";

    try {
        poc::VulkanApp app(dataRoot, chrRel, loginUif);
        app.run();
    } catch (const std::exception& e) {
        std::cerr << "Fatal: " << e.what() << "\n";
        return EXIT_FAILURE;
    }
    return EXIT_SUCCESS;
}
