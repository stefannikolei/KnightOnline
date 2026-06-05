cmake_minimum_required(VERSION 3.21)

project(KnightOnlineVulkanPoC LANGUAGES CXX)

# Whether this is the top-level project (standalone) or included from the repo
# root via add_subdirectory().
if(CMAKE_SOURCE_DIR STREQUAL CMAKE_CURRENT_SOURCE_DIR)
  set(_POC_STANDALONE ON)
else()
  set(_POC_STANDALONE OFF)
endif()

# C++20 is required because the repo's MathUtils library (reused here for
# engine-identical matrix/quaternion math) uses abbreviated function templates.
# This is scoped to the PoC, so it doesn't affect the rest of the repo when
# built as a subdirectory.
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
  set(CMAKE_BUILD_TYPE Debug CACHE STRING "" FORCE)
endif()

# -----------------------------------------------------------------------------
# Dependencies
# -----------------------------------------------------------------------------
# Vulkan. On macOS this resolves to the loader from the LunarG Vulkan SDK, which
# routes to MoltenVK (Vulkan-on-Metal). Make sure the SDK is installed and the
# VULKAN_SDK environment variable is set (`source setup-env.sh`).
#
# Not REQUIRED: when the PoC is part of the repo-root build we don't want a
# missing Vulkan SDK to break the (server/tools) build for everyone else — we
# just skip the PoC with a warning.
find_package(Vulkan)
if(NOT Vulkan_FOUND)
  message(WARNING
    "VulkanPoC: Vulkan SDK not found - skipping the Vulkan character/login PoC. "
    "Install the LunarG Vulkan SDK (https://vulkan.lunarg.com/) and set VULKAN_SDK.")
  return()
endif()

include(FetchContent)

# GLFW (windowing + cross-platform Vulkan surface creation, incl. macOS/Metal).
FetchContent_Declare(
  glfw
  GIT_REPOSITORY https://github.com/glfw/glfw.git
  GIT_TAG        3.4
)
set(GLFW_BUILD_DOCS     OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_TESTS    OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(glfw)

# GLM (header-only math).
FetchContent_Declare(
  glm
  GIT_REPOSITORY https://github.com/g-truc/glm.git
  GIT_TAG        1.0.1
)
FetchContent_MakeAvailable(glm)

# MathUtils: the engine's own cross-platform math library, reused so the
# skinning math is byte-for-byte identical to CN3Chr::BuildMesh(). When built
# from the repo root the target already exists, so only add it standalone.
if(NOT TARGET MathUtils)
  add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/../MathUtils"
                   "${CMAKE_CURRENT_BINARY_DIR}/MathUtils")
endif()

# -----------------------------------------------------------------------------
# Shader compilation (GLSL -> SPIR-V)
# -----------------------------------------------------------------------------
set(SHADER_OUTPUT_DIR "${CMAKE_CURRENT_BINARY_DIR}/shaders")
file(MAKE_DIRECTORY "${SHADER_OUTPUT_DIR}")

# Prefer glslc (shipped with the Vulkan SDK); fall back to glslangValidator.
if(Vulkan_GLSLC_EXECUTABLE)
  set(_GLSL_COMPILER "${Vulkan_GLSLC_EXECUTABLE}")
  set(_GLSL_MODE glslc)
elseif(Vulkan_GLSLANG_VALIDATOR_EXECUTABLE)
  set(_GLSL_COMPILER "${Vulkan_GLSLANG_VALIDATOR_EXECUTABLE}")
  set(_GLSL_MODE glslang)
else()
  message(WARNING
    "VulkanPoC: no GLSL->SPIR-V compiler found (glslc / glslangValidator) - "
    "skipping the PoC. Install the Vulkan SDK and ensure VULKAN_SDK is set.")
  return()
endif()

set(SHADER_SOURCES
  "${CMAKE_CURRENT_SOURCE_DIR}/shaders/character.vert"
  "${CMAKE_CURRENT_SOURCE_DIR}/shaders/character.frag"
  "${CMAKE_CURRENT_SOURCE_DIR}/shaders/ui.vert"
  "${CMAKE_CURRENT_SOURCE_DIR}/shaders/ui.frag"
)

set(SPIRV_OUTPUTS "")
foreach(_shader ${SHADER_SOURCES})
  get_filename_component(_name "${_shader}" NAME)
  set(_spv "${SHADER_OUTPUT_DIR}/${_name}.spv")
  if(_GLSL_MODE STREQUAL "glslc")
    add_custom_command(
      OUTPUT  "${_spv}"
      COMMAND "${_GLSL_COMPILER}" "${_shader}" -o "${_spv}"
      DEPENDS "${_shader}"
      COMMENT "Compiling ${_name} -> SPIR-V (glslc)"
      VERBATIM)
  else()
    add_custom_command(
      OUTPUT  "${_spv}"
      COMMAND "${_GLSL_COMPILER}" -V "${_shader}" -o "${_spv}"
      DEPENDS "${_shader}"
      COMMENT "Compiling ${_name} -> SPIR-V (glslangValidator)"
      VERBATIM)
  endif()
  list(APPEND SPIRV_OUTPUTS "${_spv}")
endforeach()

add_custom_target(poc_shaders DEPENDS ${SPIRV_OUTPUTS})

# -----------------------------------------------------------------------------
# Executable
# -----------------------------------------------------------------------------
add_executable(vulkan_character_poc
  src/main.cpp
  src/vulkan_app.cpp
  src/n3_util.cpp
  src/n3_character.cpp
  src/login_screen.cpp
)

add_dependencies(vulkan_character_poc poc_shaders)

target_link_libraries(vulkan_character_poc PRIVATE
  Vulkan::Vulkan
  glfw
  glm::glm
  MathUtils
)

# Default location of the game assets (the Client/Data submodule). Can be
# overridden at runtime: vulkan_character_poc <dataRoot> <Chr/xxx.n3chr>
set(POC_DATA_DIR_DEFAULT "${CMAKE_CURRENT_SOURCE_DIR}/../Client/Data")

target_compile_definitions(vulkan_character_poc PRIVATE
  POC_SHADER_DIR="${SHADER_OUTPUT_DIR}"
  POC_DATA_DIR="${POC_DATA_DIR_DEFAULT}"
  GLFW_INCLUDE_VULKAN
)

# Some older GNU stdlibs need a separate library for std::filesystem.
if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU" AND CMAKE_CXX_COMPILER_VERSION VERSION_LESS 9.0)
  target_link_libraries(vulkan_character_poc PRIVATE stdc++fs)
endif()

if(APPLE)
  # MoltenVK lives behind the Vulkan loader; nothing extra to link here, but make
  # the dependency on a present Vulkan SDK explicit for the user.
  message(STATUS "macOS build: Vulkan calls are translated to Metal by MoltenVK "
                 "(ensure the Vulkan SDK is installed and VULKAN_SDK is set).")
endif()
