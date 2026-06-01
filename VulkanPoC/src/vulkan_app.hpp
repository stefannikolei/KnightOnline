#pragma once

// -----------------------------------------------------------------------------
// Minimal Vulkan renderer for the character PoC.
//
// Cross-platform; works on macOS through MoltenVK (the instance enables
// VK_KHR_portability_enumeration and the device enables VK_KHR_portability_subset
// — see vulkan_app.cpp). GLFW provides the window/surface.
//
// Textures are decoded from the game's DXT/BC1 data to RGBA8 on the CPU
// (n3_character.cpp) and uploaded as VK_FORMAT_R8G8B8A8 — Apple GPUs don't
// support S3TC/BC compressed formats, so plain RGBA8 keeps it MoltenVK-friendly.
// -----------------------------------------------------------------------------

// Include the Vulkan header ourselves and tell GLFW not to pull in any GL
// headers (GLFW_INCLUDE_VULKAN alone would still include GL/gl.h, which need
// not exist on a headless/Vulkan-only system). GLFW's Vulkan helpers are still
// declared because vulkan.h is included first.
#include <vulkan/vulkan.h>
#define GLFW_INCLUDE_NONE
#include <GLFW/glfw3.h>

#include <glm/glm.hpp>

#include <array>
#include <cstdint>
#include <string>
#include <vector>

#include "n3_character.hpp"

namespace poc {

class VulkanApp {
public:
    VulkanApp(std::string dataRoot, std::string chrRel);
    ~VulkanApp();

    VulkanApp(const VulkanApp&)            = delete;
    VulkanApp& operator=(const VulkanApp&) = delete;

    void run();

private:
    struct CameraUBO {
        glm::mat4 view;
        glm::mat4 proj;
        glm::vec4 lightDir;
        glm::vec4 camPos;
    };
    struct BoneUBO {
        glm::mat4 bones[kMaxBones];
    };

    struct GpuTexture {
        VkImage         image  = VK_NULL_HANDLE;
        VkDeviceMemory  memory = VK_NULL_HANDLE;
        VkImageView     view   = VK_NULL_HANDLE;
        VkDescriptorSet set    = VK_NULL_HANDLE;
    };

    static constexpr int kFramesInFlight = 2;

    // --- setup -------------------------------------------------------------
    void initWindow();
    void initVulkan();
    void mainLoop();
    void cleanup();

    void createInstance();
    void setupDebugMessenger();
    void createSurface();
    void pickPhysicalDevice();
    void createLogicalDevice();
    void createSwapchain();
    void createImageViews();
    void createRenderPass();
    void createDescriptorSetLayouts();
    void createGraphicsPipeline();
    void createDepthResources();
    void createFramebuffers();
    void createCommandPool();
    void createGeometryBuffers();
    void createTextures();
    void createSampler();
    void createUniformBuffers();
    void createDescriptorPool();
    void createDescriptorSets();
    void createCommandBuffers();
    void createSyncObjects();

    void recreateSwapchain();
    void cleanupSwapchain();

    // --- per-frame ---------------------------------------------------------
    void drawFrame();
    void updateUniforms(uint32_t frameIndex);
    void recordCommandBuffer(VkCommandBuffer cmd, uint32_t imageIndex, uint32_t frameIndex);

    // --- helpers -----------------------------------------------------------
    struct QueueFamilies {
        uint32_t graphics = UINT32_MAX;
        uint32_t present  = UINT32_MAX;
        bool complete() const { return graphics != UINT32_MAX && present != UINT32_MAX; }
    };
    QueueFamilies findQueueFamilies(VkPhysicalDevice dev) const;
    bool          deviceSupportsRequiredExtensions(VkPhysicalDevice dev) const;

    uint32_t       findMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags props) const;
    void           createBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                                VkMemoryPropertyFlags props, VkBuffer& buffer,
                                VkDeviceMemory& memory) const;
    void           copyBuffer(VkBuffer src, VkBuffer dst, VkDeviceSize size) const;
    VkCommandBuffer beginOneTimeCommands() const;
    void            endOneTimeCommands(VkCommandBuffer cmd) const;
    GpuTexture      uploadTexture(const uint8_t* rgba, int w, int h);
    VkShaderModule createShaderModule(const std::vector<char>& code) const;
    VkFormat       findDepthFormat() const;

    static void framebufferResizeCallback(GLFWwindow* window, int width, int height);

    // --- state -------------------------------------------------------------
    std::string m_dataRoot;
    std::string m_chrRel;

    GLFWwindow* m_window      = nullptr;
    uint32_t    m_width       = 1280;
    uint32_t    m_height      = 720;
    bool        m_framebufferResized = false;

    VkInstance               m_instance       = VK_NULL_HANDLE;
    VkDebugUtilsMessengerEXT m_debugMessenger = VK_NULL_HANDLE;
    VkSurfaceKHR             m_surface        = VK_NULL_HANDLE;
    VkPhysicalDevice         m_physicalDevice = VK_NULL_HANDLE;
    VkDevice                 m_device         = VK_NULL_HANDLE;
    VkQueue                  m_graphicsQueue  = VK_NULL_HANDLE;
    VkQueue                  m_presentQueue   = VK_NULL_HANDLE;
    QueueFamilies            m_queues;

    VkSwapchainKHR             m_swapchain = VK_NULL_HANDLE;
    std::vector<VkImage>       m_swapchainImages;
    std::vector<VkImageView>   m_swapchainImageViews;
    VkFormat                   m_swapchainFormat = VK_FORMAT_UNDEFINED;
    VkExtent2D                 m_swapchainExtent{};
    std::vector<VkFramebuffer> m_framebuffers;

    VkImage        m_depthImage       = VK_NULL_HANDLE;
    VkDeviceMemory m_depthImageMemory = VK_NULL_HANDLE;
    VkImageView    m_depthImageView   = VK_NULL_HANDLE;
    VkFormat       m_depthFormat      = VK_FORMAT_UNDEFINED;

    VkRenderPass          m_renderPass    = VK_NULL_HANDLE;
    VkDescriptorSetLayout m_setLayoutFrame = VK_NULL_HANDLE; // set 0: camera + bones
    VkDescriptorSetLayout m_setLayoutTex   = VK_NULL_HANDLE; // set 1: texture sampler
    VkPipelineLayout      m_pipelineLayout = VK_NULL_HANDLE;
    VkPipeline            m_pipeline       = VK_NULL_HANDLE;
    VkCommandPool         m_commandPool    = VK_NULL_HANDLE;

    VkBuffer       m_vertexBuffer       = VK_NULL_HANDLE;
    VkDeviceMemory m_vertexBufferMemory = VK_NULL_HANDLE;
    VkBuffer       m_indexBuffer        = VK_NULL_HANDLE;
    VkDeviceMemory m_indexBufferMemory  = VK_NULL_HANDLE;

    VkSampler                m_sampler = VK_NULL_HANDLE;
    std::vector<GpuTexture>  m_gpuTextures;   // one per character texture
    GpuTexture               m_whiteTexture;  // fallback for untextured parts

    std::array<VkBuffer, kFramesInFlight>       m_cameraUBO{};
    std::array<VkDeviceMemory, kFramesInFlight> m_cameraUBOMemory{};
    std::array<void*, kFramesInFlight>          m_cameraUBOMapped{};
    std::array<VkBuffer, kFramesInFlight>       m_boneUBO{};
    std::array<VkDeviceMemory, kFramesInFlight> m_boneUBOMemory{};
    std::array<void*, kFramesInFlight>          m_boneUBOMapped{};

    VkDescriptorPool                             m_descriptorPool = VK_NULL_HANDLE;
    std::array<VkDescriptorSet, kFramesInFlight> m_frameSets{};
    std::array<VkCommandBuffer, kFramesInFlight> m_commandBuffers{};
    std::array<VkSemaphore, kFramesInFlight>     m_imageAvailable{};
    std::array<VkSemaphore, kFramesInFlight>     m_renderFinished{};
    std::array<VkFence, kFramesInFlight>         m_inFlight{};
    uint32_t                                     m_currentFrame = 0;

    N3Character m_character;
    double      m_startTime = 0.0;

    bool                     m_validation = false;
    std::vector<const char*> m_deviceExtensions;
};

} // namespace poc
