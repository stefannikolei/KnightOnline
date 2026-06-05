# KnightOnline – Vulkan Character PoC

A small, self-contained **proof of concept** for porting the N3 engine's
renderer (currently DirectX 9, see `Client/N3Base`) to **Vulkan**, running on
**macOS via MoltenVK**.

It renders **one real Knight Online character** with GPU skinning and its real
textures, and can **switch to the real 2D login screen** — both built from the
actual game assets, in a cross-platform Vulkan window. Press **TAB** to toggle
between the two.

> ⚠️ This lives *next to* the existing engine; it does not replace it. It is a
> throwaway prototype to validate the rendering path and the MoltenVK toolchain
> before touching the real engine.

## What it does

* Parses the **real `.n3chr` character assets** (from the `Client/Data`
  submodule), including the skeleton (`.n3joint`), skinned mesh parts
  (`.n3cpart` → `.n3cskins`), the DXT/BC1 textures (`.dxt`) and the animation
  ranges (`.n3anim`).
* Performs **GPU linear-blend skinning** in a Vulkan vertex shader — the direct
  equivalent of the engine's per-frame CPU skinning in `CN3Chr::BuildMesh()`.
* Plays the character's first animation (typically idle/stand) and slowly spins
  the model so you can see it in 3D.
* Runs on Windows/Linux (native Vulkan) and **macOS (MoltenVK)** from the same
  code.
* Switchable **2D login screen**: parses a real `.uif` UI form
  (e.g. `UI_US/el_login_intro_us.uif`) and renders its image elements as
  textured quads, reproducing the original login layout (background tiles +
  logo + UI art) on a virtual 1024×768 canvas. Press **TAB** / **L** to toggle,
  or **1** (character) / **2** (login).

The matrix/quaternion math is done with the repo's own **`MathUtils`** library,
so the skinning is byte-for-byte identical to the original engine.

### Code reuse

The PoC reuses what is genuinely portable from the engine and avoids duplicating
its own code:

* **`MathUtils`** (the engine's own library) is linked directly, so the joint
  matrices and skinning are identical to `CN3Chr::BuildMesh()`.
* The binary parsing/decoding shared by the character and login loaders lives
  once in **`n3_util`** (`Reader`, `resolvePath`, `loadNTFTexture`).

The N3Base loader *classes* themselves (`CN3Chr`, `CN3Texture`, `CN3UIBase`, …)
**cannot** be linked here: they are hard-wired to DirectX 9 / Windows
(`LPDIRECT3DDEVICE9`, D3DX, the global render device, texture/sound managers) —
which is exactly why the repo only builds the client on Windows. So the PoC
re-implements the *parsing* of each binary format, faithfully matching the
layouts in `Client/N3Base`, while the rendering is Vulkan instead of D3D9.

### Asset chain

```
.n3chr     container        -> joint file + part files + animation file
.n3joint   skeleton         -> joint hierarchy (TRS + animation keys)
.n3cpart   part descriptor  -> diffuse texture name + skin-mesh name
.n3cskins  skinned mesh      -> CN3Skin, 4 LODs (PoC uses LOD 0)
.dxt       NTF texture       -> DXT1/BC1 or A4R4G4B4/… (decoded to RGBA8 on CPU)
.n3anim    animation control -> named frame ranges
.uif       UI form           -> recursive UI tree; image elements -> 2D quads
```

### How it maps to the engine

| N3 engine (DirectX 9)                              | This PoC (Vulkan)                                    |
| -------------------------------------------------- | ---------------------------------------------------- |
| `CN3Chr` = skeleton + skinned parts                | `poc::N3Character` (same files, same layout)         |
| `__VertexSkinned` (origin + joint indices/weights) | `Vertex` (`pos` + `ivec4 joints` + `vec4 weights`)   |
| `CN3Chr::BuildMesh()` — **CPU** vertex skinning    | linear-blend skinning in `character.vert` (**GPU**)  |
| `m_MtxJoints[i]` · `m_MtxInverses[i]`              | per-joint skinning matrix in a uniform buffer        |
| `CN3Joint::ReCalcMatrix()` (MathUtils)             | reused verbatim via the `MathUtils` library          |
| `CN3Texture` DXT1 surfaces                         | BC1 decoded to `VK_FORMAT_R8G8B8A8` (MoltenVK-safe)  |

> The engine stores matrices row-major (DirectX, `v * M`). The shader uploads
> those bytes directly; because GLSL reads a `mat4` column-major, it transposes
> them for free, so `boneMatrix * pos` reproduces the engine's `pos * boneMatrix`
> exactly. See the comments in `n3_character.cpp` / `vulkan_app.cpp`.

### Why CPU texture decoding?

The `.dxt` files are DXT1/BC1 (S3TC) compressed. **Apple GPUs don't support
S3TC/BC formats through MoltenVK**, so the textures are decoded to plain RGBA8
on the CPU (`decodeDXT` in `n3_character.cpp`) and uploaded as
`VK_FORMAT_R8G8B8A8_SRGB`, which works everywhere. (Encrypted NTF v7 textures
are not supported and fall back to white; the v3 assets in this repo decode
fine.)

## Why MoltenVK?

macOS has no native Vulkan driver. The
[LunarG Vulkan SDK](https://vulkan.lunarg.com/) ships **MoltenVK**, which
translates Vulkan to Apple's Metal. The PoC enables the required portability
bits so the same code runs everywhere:

- Instance: `VK_KHR_portability_enumeration` +
  `VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR` (`createInstance()`).
- Device: `VK_KHR_portability_subset` when reported (`createLogicalDevice()`).

## Prerequisites

- A C++20 compiler and CMake ≥ 3.21.
- The **Vulkan SDK** (headers, loader, `glslc`, and — on macOS — MoltenVK):
  <https://vulkan.lunarg.com/>.
- Network access at configure time (GLFW and GLM are fetched via
  `FetchContent`).
- The **`Client/Data` submodule** checked out (that's where the character
  assets live):

  ```sh
  git submodule update --init --depth 1 Client/Data
  ```

### macOS

```sh
# Install the Vulkan SDK (.dmg from LunarG), then in each shell:
source ~/VulkanSDK/<version>/setup-env.sh   # sets VULKAN_SDK, VK_ICD_FILENAMES…
```

## Build & run

The PoC is wired into the repo-root CMake build (option
`OPENKO_BUILD_VULKAN_POC`, **on by default**) *and* can be built standalone.
It auto-skips with a warning if the Vulkan SDK isn't installed, so it never
breaks a server/tools-only build.

### Standalone (recommended for iterating on the PoC)

```sh
cd VulkanPoC
cmake -S . -B build -G Ninja   # fetches GLFW + GLM, reuses ../MathUtils
cmake --build build
```

### From the repo root

```sh
cmake -S . -B build -G Ninja
cmake --build build --target vulkan_character_poc
# disable with -DOPENKO_BUILD_VULKAN_POC=OFF
```

> Note: building the whole repo root also configures the server dependencies
> (asio/llfio/boost/…). If those fail to fetch on your machine, build the PoC
> standalone as shown above — it shares no dependencies with the servers.

### Run

```sh
# Defaults to Chr/npc_el_knight.n3chr + UI_US/el_login_intro_us.uif.
./build/vulkan_character_poc

# Or pick any character / login form:
./build/vulkan_character_poc ../Client/Data Chr/mob_zombie.n3chr UI_US/ka_login_intro_us.uif
```

You should get a window showing the character with its textures, playing its
idle animation and slowly rotating. **Press TAB** to switch to the login screen
and back.

`vulkan_character_poc [dataRoot] [characterRelPath] [loginUifPath]` — all
arguments are optional; the data root defaults to the in-tree `Client/Data`.

Controls: **TAB** / **L** toggle, **1** character, **2** login, **Esc** quit.

Known-good characters include `Chr/npc_el_knight.n3chr`, `Chr/mob_zombie.n3chr`,
`Chr/npc_el_shop.n3chr`, `Chr/mon_twohead.n3chr`. (A few assets in the submodule
are incomplete — e.g. some player skeletons are missing their `.n3joint` — and
the loader reports those cleanly.)

## Layout

```
VulkanPoC/
├── CMakeLists.txt          # find_package(Vulkan), FetchContent GLFW/GLM, MathUtils, shaders
├── README.md
├── shaders/
│   ├── character.vert      # GPU linear-blend skinning (the core of the port)
│   ├── character.frag      # textured Blinn-Phong lighting
│   ├── ui.vert / ui.frag   # 2D login-screen quads
└── src/
    ├── main.cpp            # entry point + CLI args
    ├── n3_util.hpp/.cpp    # shared Reader / resolvePath / NTF texture decoding
    ├── n3_character.hpp/.cpp # real .n3chr/.n3joint/.n3cskins/.dxt loader + skinning
    ├── login_screen.hpp/.cpp # real .uif loader -> 2D textured quads
    └── vulkan_app.hpp/.cpp # the Vulkan renderer (both render modes + toggle)
```

## Next steps toward a real port

1. Load the additional LODs and switch between them by distance (the data is
   already in the `.n3cskins` files; the PoC just uses LOD 0).
2. Drive animations from `CN3AnimControl` by name (idle/walk/attack…) instead of
   always playing the first range, and add animation blending.
3. Add weapon/cloak *plugs* and FX (the `.n3chr` already lists them).
4. Introduce a thin render-hardware-interface (RHI) seam in `N3Base` so the
   engine can target D3D9 *or* Vulkan, then port terrain, sky, effects and UI
   behind it.
