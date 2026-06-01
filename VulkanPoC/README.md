# KnightOnline – Vulkan Character PoC

A small, self-contained **proof of concept** for porting the N3 engine's
renderer (currently DirectX 9, see `Client/N3Base`) to **Vulkan**, running on
**macOS via MoltenVK**.

The PoC does exactly one thing: render **a single animated character** with
GPU skinning, in a cross-platform Vulkan window.

> ⚠️ This lives *next to* the existing engine, it does not replace it. It is a
> throwaway prototype to validate the rendering path and the MoltenVK toolchain
> before touching the real engine. It does **not** depend on any proprietary
> Knight Online assets (none are shipped in this repo) — the character geometry
> and skeleton are generated procedurally at startup.

![character placeholder](docs-not-included)

## What it demonstrates

| N3 engine (DirectX 9)                              | This PoC (Vulkan)                                   |
| -------------------------------------------------- | --------------------------------------------------- |
| `CN3Chr` = skeleton + skinned mesh parts           | `poc::Character` = `Joint[]` + skinned `Vertex[]`   |
| `__VertexSkinned` (origin + joint indices/weights) | `Vertex` (pos + `ivec4 joints` + `vec4 weights`)    |
| `CN3Chr::BuildMesh()` skins vertices on the **CPU**| Linear-blend skinning on the **GPU** (vertex shader)|
| `m_MtxJoints[i]` · `m_MtxInverses[i]`              | per-joint skinning matrix in a uniform buffer       |
| `CN3AnimControl` keyframe animation                | procedural idle animation (`skinningMatrices()`)    |
| Fixed-function lighting + part textures            | Blinn-Phong directional light in the frag shader    |

The important takeaway for the real port: **N3's per-frame CPU vertex skinning
(`BuildMesh`) maps directly onto a Vulkan vertex shader** that blends per-joint
matrices uploaded once per frame. The rest is standard Vulkan setup.

## Why MoltenVK?

macOS has no native Vulkan driver. The
[LunarG Vulkan SDK](https://vulkan.lunarg.com/) ships **MoltenVK**, a layer that
translates Vulkan to Apple's Metal. The PoC enables the required portability
bits so the same code runs on Windows/Linux (native Vulkan) and macOS (MoltenVK):

- Instance: `VK_KHR_portability_enumeration` +
  `VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR`
  (`src/vulkan_app.cpp`, `createInstance()`).
- Device: `VK_KHR_portability_subset` when the physical device reports it
  (`createLogicalDevice()`).

These are added conditionally, so nothing breaks on platforms with a real
Vulkan driver.

## Prerequisites

- A C++17 compiler and CMake ≥ 3.21.
- The **Vulkan SDK** (provides headers, the loader, `glslc`, and — on macOS —
  MoltenVK). Download from <https://vulkan.lunarg.com/>.
- Network access at configure time (GLFW and GLM are pulled via
  `FetchContent`).

### macOS

```sh
# 1. Install the Vulkan SDK (.dmg from LunarG), then in each shell:
source ~/VulkanSDK/<version>/setup-env.sh   # sets VULKAN_SDK, VK_ICD_FILENAMES, etc.

# (Homebrew alternative: `brew install molten-vk vulkan-loader vulkan-headers
#  vulkan-tools shaderc glslang` — but the LunarG SDK is the smoothest path.)
```

## Build & run

```sh
cd VulkanPoC
cmake -B build -DCMAKE_BUILD_TYPE=Debug
cmake --build build -j

# binary lands in build/ (or build/Debug on multi-config generators)
./build/vulkan_character_poc
```

You should get a window titled *"KnightOnline - Vulkan Character PoC"* showing a
low-poly humanoid slowly turning, with a subtle idle animation (breathing,
arm sway, vertical bob).

Validation layers are enabled automatically in Debug builds if the
`VK_LAYER_KHRONOS_validation` layer is installed (it ships with the SDK).

## Layout

```
VulkanPoC/
├── CMakeLists.txt          # find_package(Vulkan), FetchContent GLFW/GLM, shader build
├── README.md
├── shaders/
│   ├── character.vert      # GPU linear-blend skinning (the core of the port)
│   └── character.frag      # Blinn-Phong directional lighting
└── src/
    ├── main.cpp            # entry point
    ├── character.hpp/.cpp  # procedural skeleton + skinned mesh (mirrors CN3Chr)
    └── vulkan_app.hpp/.cpp # the Vulkan renderer (instance→device→swapchain→draw)
```

## Next steps toward a real port

This PoC intentionally stops at "one character on screen". A real port would,
roughly in order:

1. Replace the procedural mesh with a loader for the real `.n3pmesh` / `.n3chr`
   formats (the parsing already exists in `Client/N3Base`; only the GPU upload
   path changes).
2. Load the `.dxt` part textures into `VkImage`s + samplers and bind a combined
   image sampler in the fragment shader.
3. Drive `skinningMatrices()` from `CN3AnimControl` keyframes instead of the
   procedural idle pose.
4. Introduce a thin render-hardware-interface (RHI) seam in `N3Base` so the
   engine can target D3D9 *or* Vulkan, then port the remaining draw calls
   (terrain, sky, effects, UI) behind it.
