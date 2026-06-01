// -----------------------------------------------------------------------------
// KnightOnline - Vulkan Character PoC
//
// Proof-of-concept for porting the N3 engine's renderer from DirectX 9 to
// Vulkan. It renders a single skinned character using GPU linear-blend skinning,
// mirroring the data model of CN3Chr (skeleton + skinned vertices). On macOS it
// runs on top of MoltenVK (the Vulkan-on-Metal layer bundled with the LunarG
// Vulkan SDK) via the portability extensions enabled in vulkan_app.cpp.
// -----------------------------------------------------------------------------

#include <cstdlib>
#include <iostream>

#include "vulkan_app.hpp"

int main() {
    try {
        poc::VulkanApp app;
        app.run();
    } catch (const std::exception& e) {
        std::cerr << "Fatal: " << e.what() << "\n";
        return EXIT_FAILURE;
    }
    return EXIT_SUCCESS;
}
