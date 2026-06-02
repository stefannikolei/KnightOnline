#include "vulkan_app.hpp"

#include <glm/gtc/matrix_transform.hpp>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iostream>
#include <set>
#include <stdexcept>

#ifndef POC_SHADER_DIR
#define POC_SHADER_DIR "shaders"
#endif

namespace poc {

namespace {

const std::vector<const char*> kValidationLayers = {
    "VK_LAYER_KHRONOS_validation",
};

std::vector<char> readFile(const std::string& path) {
    std::ifstream file(path, std::ios::ate | std::ios::binary);
    if (!file.is_open())
        throw std::runtime_error("Failed to open file: " + path);
    size_t size = static_cast<size_t>(file.tellg());
    std::vector<char> buffer(size);
    file.seekg(0);
    file.read(buffer.data(), static_cast<std::streamsize>(size));
    return buffer;
}

void check(VkResult r, const char* what) {
    if (r != VK_SUCCESS)
        throw std::runtime_error(std::string("Vulkan error in ") + what +
                                 " (VkResult " + std::to_string(static_cast<int>(r)) + ")");
}

VKAPI_ATTR VkBool32 VKAPI_CALL debugCallback(
    VkDebugUtilsMessageSeverityFlagBitsEXT severity,
    VkDebugUtilsMessageTypeFlagsEXT,
    const VkDebugUtilsMessengerCallbackDataEXT* data,
    void*) {
    if (severity >= VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT)
        std::cerr << "[validation] " << data->pMessage << "\n";
    return VK_FALSE;
}

bool hasInstanceExtension(const char* name) {
    uint32_t count = 0;
    vkEnumerateInstanceExtensionProperties(nullptr, &count, nullptr);
    std::vector<VkExtensionProperties> props(count);
    vkEnumerateInstanceExtensionProperties(nullptr, &count, props.data());
    for (const auto& p : props)
        if (std::strcmp(p.extensionName, name) == 0) return true;
    return false;
}

bool hasValidationLayer() {
    uint32_t count = 0;
    vkEnumerateInstanceLayerProperties(&count, nullptr);
    std::vector<VkLayerProperties> layers(count);
    vkEnumerateInstanceLayerProperties(&count, layers.data());
    for (const auto* want : kValidationLayers) {
        bool found = false;
        for (const auto& l : layers)
            if (std::strcmp(l.layerName, want) == 0) { found = true; break; }
        if (!found) return false;
    }
    return true;
}

} // namespace

VulkanApp::VulkanApp(std::string dataRoot, std::string chrRel, std::string loginUif)
    : m_dataRoot(std::move(dataRoot)), m_chrRel(std::move(chrRel)),
      m_loginUif(std::move(loginUif)) {
#ifndef NDEBUG
    m_validation = true;
#endif
    if (!m_character.load(m_dataRoot, m_chrRel))
        throw std::runtime_error("Failed to load character: " + m_chrRel);

    // The login screen is optional: if its assets are missing, the PoC still
    // runs in character mode.
    m_loginAvailable = m_login.load(m_dataRoot, m_loginUif);
    if (!m_loginAvailable)
        std::cerr << "Login screen unavailable; running in character mode only.\n";

    std::cout << "Press TAB to switch between the character view and the login screen.\n";
}

VulkanApp::~VulkanApp() {
    cleanup();
}

void VulkanApp::run() {
    initWindow();
    initVulkan();
    mainLoop();
}

// ---------------------------------------------------------------------------
// Window
// ---------------------------------------------------------------------------
void VulkanApp::initWindow() {
    if (!glfwInit())
        throw std::runtime_error("glfwInit failed");
    glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
    m_window = glfwCreateWindow(static_cast<int>(m_width), static_cast<int>(m_height),
                                "KnightOnline - Vulkan Character PoC", nullptr, nullptr);
    if (!m_window)
        throw std::runtime_error("glfwCreateWindow failed");
    glfwSetWindowUserPointer(m_window, this);
    glfwSetFramebufferSizeCallback(m_window, framebufferResizeCallback);
    glfwSetKeyCallback(m_window, keyCallback);
}

void VulkanApp::framebufferResizeCallback(GLFWwindow* window, int, int) {
    auto* app = static_cast<VulkanApp*>(glfwGetWindowUserPointer(window));
    app->m_framebufferResized = true;
}

void VulkanApp::keyCallback(GLFWwindow* window, int key, int, int action, int) {
    if (action != GLFW_PRESS) return;
    auto* app = static_cast<VulkanApp*>(glfwGetWindowUserPointer(window));
    if (key == GLFW_KEY_TAB || key == GLFW_KEY_L) {
        if (app->m_loginAvailable)
            app->m_mode = (app->m_mode == Mode::Character) ? Mode::Login : Mode::Character;
    } else if (key == GLFW_KEY_1) {
        app->m_mode = Mode::Character;
    } else if (key == GLFW_KEY_2 && app->m_loginAvailable) {
        app->m_mode = Mode::Login;
    } else if (key == GLFW_KEY_ESCAPE) {
        glfwSetWindowShouldClose(window, GLFW_TRUE);
    }
}

// ---------------------------------------------------------------------------
// Vulkan init
// ---------------------------------------------------------------------------
void VulkanApp::initVulkan() {
    createInstance();
    setupDebugMessenger();
    createSurface();
    pickPhysicalDevice();
    createLogicalDevice();
    createSwapchain();
    createImageViews();
    createRenderPass();
    createDescriptorSetLayouts();
    createGraphicsPipeline();
    createUIPipeline();
    createDepthResources();
    createFramebuffers();
    createCommandPool();
    createGeometryBuffers();
    createSampler();
    createTextures();
    createLoginResources();
    createUniformBuffers();
    createDescriptorPool();
    createDescriptorSets();
    createCommandBuffers();
    createSyncObjects();
    m_startTime = glfwGetTime();
}

void VulkanApp::createInstance() {
    if (m_validation && !hasValidationLayer()) {
        std::cerr << "Validation layers requested but not available; disabling.\n";
        m_validation = false;
    }

    VkApplicationInfo app{};
    app.sType              = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    app.pApplicationName   = "KnightOnline Vulkan PoC";
    app.applicationVersion = VK_MAKE_VERSION(0, 1, 0);
    app.pEngineName        = "N3-Vulkan";
    app.engineVersion      = VK_MAKE_VERSION(0, 1, 0);
    app.apiVersion         = VK_API_VERSION_1_1;

    uint32_t glfwCount = 0;
    const char** glfwExt = glfwGetRequiredInstanceExtensions(&glfwCount);
    std::vector<const char*> extensions(glfwExt, glfwExt + glfwCount);

    if (m_validation)
        extensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);

    VkInstanceCreateFlags flags = 0;
    // MoltenVK / portability: required on macOS for the Vulkan->Metal layer.
    if (hasInstanceExtension(VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME)) {
        extensions.push_back(VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME);
        flags |= VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR;
    }
    if (hasInstanceExtension(VK_KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME))
        extensions.push_back(VK_KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME);

    VkInstanceCreateInfo ci{};
    ci.sType                   = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    ci.flags                   = flags;
    ci.pApplicationInfo        = &app;
    ci.enabledExtensionCount   = static_cast<uint32_t>(extensions.size());
    ci.ppEnabledExtensionNames = extensions.data();

    VkDebugUtilsMessengerCreateInfoEXT dbg{};
    if (m_validation) {
        ci.enabledLayerCount   = static_cast<uint32_t>(kValidationLayers.size());
        ci.ppEnabledLayerNames = kValidationLayers.data();

        dbg.sType           = VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;
        dbg.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT |
                              VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
        dbg.messageType     = VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT |
                              VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT |
                              VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
        dbg.pfnUserCallback = debugCallback;
        ci.pNext            = &dbg;
    }

    check(vkCreateInstance(&ci, nullptr, &m_instance), "vkCreateInstance");
}

void VulkanApp::setupDebugMessenger() {
    if (!m_validation) return;
    auto fn = reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(
        vkGetInstanceProcAddr(m_instance, "vkCreateDebugUtilsMessengerEXT"));
    if (!fn) return;

    VkDebugUtilsMessengerCreateInfoEXT ci{};
    ci.sType           = VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;
    ci.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT |
                         VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
    ci.messageType     = VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT |
                         VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT |
                         VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
    ci.pfnUserCallback = debugCallback;
    fn(m_instance, &ci, nullptr, &m_debugMessenger);
}

void VulkanApp::createSurface() {
    check(glfwCreateWindowSurface(m_instance, m_window, nullptr, &m_surface),
          "glfwCreateWindowSurface");
}

VulkanApp::QueueFamilies VulkanApp::findQueueFamilies(VkPhysicalDevice dev) const {
    QueueFamilies q;
    uint32_t count = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(dev, &count, nullptr);
    std::vector<VkQueueFamilyProperties> families(count);
    vkGetPhysicalDeviceQueueFamilyProperties(dev, &count, families.data());

    for (uint32_t i = 0; i < count; ++i) {
        if (families[i].queueFlags & VK_QUEUE_GRAPHICS_BIT)
            q.graphics = i;
        VkBool32 present = VK_FALSE;
        vkGetPhysicalDeviceSurfaceSupportKHR(dev, i, m_surface, &present);
        if (present)
            q.present = i;
        if (q.complete()) break;
    }
    return q;
}

bool VulkanApp::deviceSupportsRequiredExtensions(VkPhysicalDevice dev) const {
    uint32_t count = 0;
    vkEnumerateDeviceExtensionProperties(dev, nullptr, &count, nullptr);
    std::vector<VkExtensionProperties> props(count);
    vkEnumerateDeviceExtensionProperties(dev, nullptr, &count, props.data());

    std::set<std::string> required = { VK_KHR_SWAPCHAIN_EXTENSION_NAME };
    for (const auto& p : props)
        required.erase(p.extensionName);
    return required.empty();
}

void VulkanApp::pickPhysicalDevice() {
    uint32_t count = 0;
    vkEnumeratePhysicalDevices(m_instance, &count, nullptr);
    if (count == 0)
        throw std::runtime_error("No Vulkan-capable GPU found");
    std::vector<VkPhysicalDevice> devices(count);
    vkEnumeratePhysicalDevices(m_instance, &count, devices.data());

    for (auto dev : devices) {
        if (findQueueFamilies(dev).complete() && deviceSupportsRequiredExtensions(dev)) {
            m_physicalDevice = dev;
            break;
        }
    }
    if (m_physicalDevice == VK_NULL_HANDLE)
        throw std::runtime_error("No suitable GPU found");

    VkPhysicalDeviceProperties props;
    vkGetPhysicalDeviceProperties(m_physicalDevice, &props);
    std::cout << "Using GPU: " << props.deviceName << "\n";
}

void VulkanApp::createLogicalDevice() {
    m_queues = findQueueFamilies(m_physicalDevice);

    std::set<uint32_t> uniqueFamilies = { m_queues.graphics, m_queues.present };
    std::vector<VkDeviceQueueCreateInfo> queueInfos;
    float priority = 1.0f;
    for (uint32_t family : uniqueFamilies) {
        VkDeviceQueueCreateInfo qi{};
        qi.sType            = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
        qi.queueFamilyIndex = family;
        qi.queueCount       = 1;
        qi.pQueuePriorities = &priority;
        queueInfos.push_back(qi);
    }

    m_deviceExtensions = { VK_KHR_SWAPCHAIN_EXTENSION_NAME };

    // Enable VK_KHR_portability_subset when present (mandatory on MoltenVK).
    {
        uint32_t count = 0;
        vkEnumerateDeviceExtensionProperties(m_physicalDevice, nullptr, &count, nullptr);
        std::vector<VkExtensionProperties> props(count);
        vkEnumerateDeviceExtensionProperties(m_physicalDevice, nullptr, &count, props.data());
        for (const auto& p : props)
            if (std::strcmp(p.extensionName, "VK_KHR_portability_subset") == 0)
                m_deviceExtensions.push_back("VK_KHR_portability_subset");
    }

    VkPhysicalDeviceFeatures features{};

    VkDeviceCreateInfo ci{};
    ci.sType                   = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    ci.queueCreateInfoCount    = static_cast<uint32_t>(queueInfos.size());
    ci.pQueueCreateInfos       = queueInfos.data();
    ci.pEnabledFeatures        = &features;
    ci.enabledExtensionCount   = static_cast<uint32_t>(m_deviceExtensions.size());
    ci.ppEnabledExtensionNames = m_deviceExtensions.data();
    if (m_validation) {
        ci.enabledLayerCount   = static_cast<uint32_t>(kValidationLayers.size());
        ci.ppEnabledLayerNames = kValidationLayers.data();
    }

    check(vkCreateDevice(m_physicalDevice, &ci, nullptr, &m_device), "vkCreateDevice");
    vkGetDeviceQueue(m_device, m_queues.graphics, 0, &m_graphicsQueue);
    vkGetDeviceQueue(m_device, m_queues.present, 0, &m_presentQueue);
}

void VulkanApp::createSwapchain() {
    VkSurfaceCapabilitiesKHR caps;
    vkGetPhysicalDeviceSurfaceCapabilitiesKHR(m_physicalDevice, m_surface, &caps);

    uint32_t formatCount = 0;
    vkGetPhysicalDeviceSurfaceFormatsKHR(m_physicalDevice, m_surface, &formatCount, nullptr);
    std::vector<VkSurfaceFormatKHR> formats(formatCount);
    vkGetPhysicalDeviceSurfaceFormatsKHR(m_physicalDevice, m_surface, &formatCount, formats.data());

    VkSurfaceFormatKHR surfaceFormat = formats[0];
    for (const auto& f : formats) {
        if (f.format == VK_FORMAT_B8G8R8A8_SRGB &&
            f.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR) {
            surfaceFormat = f;
            break;
        }
    }

    uint32_t presentCount = 0;
    vkGetPhysicalDeviceSurfacePresentModesKHR(m_physicalDevice, m_surface, &presentCount, nullptr);
    std::vector<VkPresentModeKHR> presentModes(presentCount);
    vkGetPhysicalDeviceSurfacePresentModesKHR(m_physicalDevice, m_surface, &presentCount,
                                              presentModes.data());
    VkPresentModeKHR presentMode = VK_PRESENT_MODE_FIFO_KHR;
    for (auto m : presentModes)
        if (m == VK_PRESENT_MODE_MAILBOX_KHR) { presentMode = m; break; }

    VkExtent2D extent = caps.currentExtent;
    if (caps.currentExtent.width == UINT32_MAX) {
        int w, h;
        glfwGetFramebufferSize(m_window, &w, &h);
        extent.width  = std::clamp(static_cast<uint32_t>(w),
                                   caps.minImageExtent.width, caps.maxImageExtent.width);
        extent.height = std::clamp(static_cast<uint32_t>(h),
                                   caps.minImageExtent.height, caps.maxImageExtent.height);
    }

    uint32_t imageCount = caps.minImageCount + 1;
    if (caps.maxImageCount > 0 && imageCount > caps.maxImageCount)
        imageCount = caps.maxImageCount;

    VkSwapchainCreateInfoKHR ci{};
    ci.sType            = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
    ci.surface          = m_surface;
    ci.minImageCount    = imageCount;
    ci.imageFormat      = surfaceFormat.format;
    ci.imageColorSpace  = surfaceFormat.colorSpace;
    ci.imageExtent      = extent;
    ci.imageArrayLayers = 1;
    ci.imageUsage       = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    ci.preTransform     = caps.currentTransform;
    ci.compositeAlpha   = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
    ci.presentMode      = presentMode;
    ci.clipped          = VK_TRUE;
    ci.oldSwapchain     = VK_NULL_HANDLE;

    uint32_t indices[] = { m_queues.graphics, m_queues.present };
    if (m_queues.graphics != m_queues.present) {
        ci.imageSharingMode      = VK_SHARING_MODE_CONCURRENT;
        ci.queueFamilyIndexCount = 2;
        ci.pQueueFamilyIndices   = indices;
    } else {
        ci.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
    }

    check(vkCreateSwapchainKHR(m_device, &ci, nullptr, &m_swapchain), "vkCreateSwapchainKHR");

    vkGetSwapchainImagesKHR(m_device, m_swapchain, &imageCount, nullptr);
    m_swapchainImages.resize(imageCount);
    vkGetSwapchainImagesKHR(m_device, m_swapchain, &imageCount, m_swapchainImages.data());

    m_swapchainFormat = surfaceFormat.format;
    m_swapchainExtent = extent;
}

void VulkanApp::createImageViews() {
    m_swapchainImageViews.resize(m_swapchainImages.size());
    for (size_t i = 0; i < m_swapchainImages.size(); ++i) {
        VkImageViewCreateInfo ci{};
        ci.sType                       = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        ci.image                       = m_swapchainImages[i];
        ci.viewType                    = VK_IMAGE_VIEW_TYPE_2D;
        ci.format                      = m_swapchainFormat;
        ci.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        ci.subresourceRange.levelCount = 1;
        ci.subresourceRange.layerCount = 1;
        check(vkCreateImageView(m_device, &ci, nullptr, &m_swapchainImageViews[i]),
              "vkCreateImageView");
    }
}

VkFormat VulkanApp::findDepthFormat() const {
    const VkFormat candidates[] = { VK_FORMAT_D32_SFLOAT,
                                    VK_FORMAT_D32_SFLOAT_S8_UINT,
                                    VK_FORMAT_D24_UNORM_S8_UINT };
    for (VkFormat f : candidates) {
        VkFormatProperties props;
        vkGetPhysicalDeviceFormatProperties(m_physicalDevice, f, &props);
        if (props.optimalTilingFeatures & VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT)
            return f;
    }
    throw std::runtime_error("No supported depth format");
}

void VulkanApp::createRenderPass() {
    m_depthFormat = findDepthFormat();

    VkAttachmentDescription color{};
    color.format         = m_swapchainFormat;
    color.samples        = VK_SAMPLE_COUNT_1_BIT;
    color.loadOp         = VK_ATTACHMENT_LOAD_OP_CLEAR;
    color.storeOp        = VK_ATTACHMENT_STORE_OP_STORE;
    color.stencilLoadOp  = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    color.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    color.initialLayout  = VK_IMAGE_LAYOUT_UNDEFINED;
    color.finalLayout    = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

    VkAttachmentDescription depth{};
    depth.format         = m_depthFormat;
    depth.samples        = VK_SAMPLE_COUNT_1_BIT;
    depth.loadOp         = VK_ATTACHMENT_LOAD_OP_CLEAR;
    depth.storeOp        = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    depth.stencilLoadOp  = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    depth.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    depth.initialLayout  = VK_IMAGE_LAYOUT_UNDEFINED;
    depth.finalLayout    = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;

    VkAttachmentReference colorRef{ 0, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL };
    VkAttachmentReference depthRef{ 1, VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL };

    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint       = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount    = 1;
    subpass.pColorAttachments       = &colorRef;
    subpass.pDepthStencilAttachment = &depthRef;

    VkSubpassDependency dep{};
    dep.srcSubpass    = VK_SUBPASS_EXTERNAL;
    dep.dstSubpass    = 0;
    dep.srcStageMask  = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
                        VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
    dep.dstStageMask  = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
                        VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
    dep.srcAccessMask = 0;
    dep.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
                        VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;

    std::array<VkAttachmentDescription, 2> attachments = { color, depth };
    VkRenderPassCreateInfo ci{};
    ci.sType           = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    ci.attachmentCount = static_cast<uint32_t>(attachments.size());
    ci.pAttachments    = attachments.data();
    ci.subpassCount    = 1;
    ci.pSubpasses      = &subpass;
    ci.dependencyCount = 1;
    ci.pDependencies   = &dep;

    check(vkCreateRenderPass(m_device, &ci, nullptr, &m_renderPass), "vkCreateRenderPass");
}

void VulkanApp::createDescriptorSetLayouts() {
    // set 0: camera UBO (binding 0, VS+FS) + bone UBO (binding 1, VS)
    VkDescriptorSetLayoutBinding camBinding{};
    camBinding.binding         = 0;
    camBinding.descriptorType  = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    camBinding.descriptorCount = 1;
    camBinding.stageFlags      = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;

    VkDescriptorSetLayoutBinding boneBinding{};
    boneBinding.binding         = 1;
    boneBinding.descriptorType  = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    boneBinding.descriptorCount = 1;
    boneBinding.stageFlags      = VK_SHADER_STAGE_VERTEX_BIT;

    std::array<VkDescriptorSetLayoutBinding, 2> frameBindings = { camBinding, boneBinding };
    VkDescriptorSetLayoutCreateInfo frameCI{};
    frameCI.sType        = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    frameCI.bindingCount = static_cast<uint32_t>(frameBindings.size());
    frameCI.pBindings    = frameBindings.data();
    check(vkCreateDescriptorSetLayout(m_device, &frameCI, nullptr, &m_setLayoutFrame),
          "vkCreateDescriptorSetLayout(frame)");

    // set 1: combined image sampler (binding 0, FS)
    VkDescriptorSetLayoutBinding texBinding{};
    texBinding.binding         = 0;
    texBinding.descriptorType  = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    texBinding.descriptorCount = 1;
    texBinding.stageFlags      = VK_SHADER_STAGE_FRAGMENT_BIT;

    VkDescriptorSetLayoutCreateInfo texCI{};
    texCI.sType        = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    texCI.bindingCount = 1;
    texCI.pBindings    = &texBinding;
    check(vkCreateDescriptorSetLayout(m_device, &texCI, nullptr, &m_setLayoutTex),
          "vkCreateDescriptorSetLayout(tex)");
}

VkShaderModule VulkanApp::createShaderModule(const std::vector<char>& code) const {
    VkShaderModuleCreateInfo ci{};
    ci.sType    = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    ci.codeSize = code.size();
    ci.pCode    = reinterpret_cast<const uint32_t*>(code.data());
    VkShaderModule module;
    check(vkCreateShaderModule(m_device, &ci, nullptr, &module), "vkCreateShaderModule");
    return module;
}

void VulkanApp::createGraphicsPipeline() {
    auto vertCode = readFile(std::string(POC_SHADER_DIR) + "/character.vert.spv");
    auto fragCode = readFile(std::string(POC_SHADER_DIR) + "/character.frag.spv");
    VkShaderModule vert = createShaderModule(vertCode);
    VkShaderModule frag = createShaderModule(fragCode);

    VkPipelineShaderStageCreateInfo vertStage{};
    vertStage.sType  = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    vertStage.stage  = VK_SHADER_STAGE_VERTEX_BIT;
    vertStage.module = vert;
    vertStage.pName  = "main";

    VkPipelineShaderStageCreateInfo fragStage{};
    fragStage.sType  = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    fragStage.stage  = VK_SHADER_STAGE_FRAGMENT_BIT;
    fragStage.module = frag;
    fragStage.pName  = "main";

    VkPipelineShaderStageCreateInfo stages[] = { vertStage, fragStage };

    VkVertexInputBindingDescription binding{};
    binding.binding   = 0;
    binding.stride    = sizeof(Vertex);
    binding.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

    std::array<VkVertexInputAttributeDescription, 5> attrs{};
    attrs[0] = { 0, 0, VK_FORMAT_R32G32B32_SFLOAT,    static_cast<uint32_t>(offsetof(Vertex, pos)) };
    attrs[1] = { 1, 0, VK_FORMAT_R32G32B32_SFLOAT,    static_cast<uint32_t>(offsetof(Vertex, normal)) };
    attrs[2] = { 2, 0, VK_FORMAT_R32G32_SFLOAT,       static_cast<uint32_t>(offsetof(Vertex, uv)) };
    attrs[3] = { 3, 0, VK_FORMAT_R32G32B32A32_SINT,   static_cast<uint32_t>(offsetof(Vertex, joints)) };
    attrs[4] = { 4, 0, VK_FORMAT_R32G32B32A32_SFLOAT, static_cast<uint32_t>(offsetof(Vertex, weights)) };

    VkPipelineVertexInputStateCreateInfo vertexInput{};
    vertexInput.sType                           = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInput.vertexBindingDescriptionCount   = 1;
    vertexInput.pVertexBindingDescriptions      = &binding;
    vertexInput.vertexAttributeDescriptionCount = static_cast<uint32_t>(attrs.size());
    vertexInput.pVertexAttributeDescriptions    = attrs.data();

    VkPipelineInputAssemblyStateCreateInfo inputAsm{};
    inputAsm.sType    = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAsm.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

    VkPipelineViewportStateCreateInfo viewport{};
    viewport.sType         = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewport.viewportCount = 1;
    viewport.scissorCount  = 1;

    VkPipelineRasterizationStateCreateInfo raster{};
    raster.sType       = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    raster.polygonMode = VK_POLYGON_MODE_FILL;
    // Culling disabled: N3 meshes are authored for DirectX's left-handed winding;
    // disabling avoids guessing the front face for this PoC.
    raster.cullMode    = VK_CULL_MODE_NONE;
    raster.frontFace   = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    raster.lineWidth   = 1.0f;

    VkPipelineMultisampleStateCreateInfo multisample{};
    multisample.sType                = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisample.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    VkPipelineDepthStencilStateCreateInfo depthStencil{};
    depthStencil.sType            = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
    depthStencil.depthTestEnable  = VK_TRUE;
    depthStencil.depthWriteEnable = VK_TRUE;
    depthStencil.depthCompareOp   = VK_COMPARE_OP_LESS;

    VkPipelineColorBlendAttachmentState colorBlendAttachment{};
    colorBlendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
                                          VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

    VkPipelineColorBlendStateCreateInfo colorBlend{};
    colorBlend.sType           = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    colorBlend.attachmentCount = 1;
    colorBlend.pAttachments    = &colorBlendAttachment;

    std::array<VkDynamicState, 2> dynamics = { VK_DYNAMIC_STATE_VIEWPORT,
                                               VK_DYNAMIC_STATE_SCISSOR };
    VkPipelineDynamicStateCreateInfo dynamicState{};
    dynamicState.sType             = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynamicState.dynamicStateCount = static_cast<uint32_t>(dynamics.size());
    dynamicState.pDynamicStates    = dynamics.data();

    std::array<VkDescriptorSetLayout, 2> setLayouts = { m_setLayoutFrame, m_setLayoutTex };
    VkPipelineLayoutCreateInfo layoutInfo{};
    layoutInfo.sType          = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    layoutInfo.setLayoutCount = static_cast<uint32_t>(setLayouts.size());
    layoutInfo.pSetLayouts    = setLayouts.data();
    check(vkCreatePipelineLayout(m_device, &layoutInfo, nullptr, &m_pipelineLayout),
          "vkCreatePipelineLayout");

    VkGraphicsPipelineCreateInfo pipelineInfo{};
    pipelineInfo.sType               = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipelineInfo.stageCount          = 2;
    pipelineInfo.pStages             = stages;
    pipelineInfo.pVertexInputState   = &vertexInput;
    pipelineInfo.pInputAssemblyState = &inputAsm;
    pipelineInfo.pViewportState      = &viewport;
    pipelineInfo.pRasterizationState = &raster;
    pipelineInfo.pMultisampleState   = &multisample;
    pipelineInfo.pDepthStencilState  = &depthStencil;
    pipelineInfo.pColorBlendState    = &colorBlend;
    pipelineInfo.pDynamicState       = &dynamicState;
    pipelineInfo.layout              = m_pipelineLayout;
    pipelineInfo.renderPass          = m_renderPass;
    pipelineInfo.subpass             = 0;

    check(vkCreateGraphicsPipelines(m_device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr,
                                    &m_pipeline),
          "vkCreateGraphicsPipelines");

    vkDestroyShaderModule(m_device, vert, nullptr);
    vkDestroyShaderModule(m_device, frag, nullptr);
}

void VulkanApp::createUIPipeline() {
    auto vertCode = readFile(std::string(POC_SHADER_DIR) + "/ui.vert.spv");
    auto fragCode = readFile(std::string(POC_SHADER_DIR) + "/ui.frag.spv");
    VkShaderModule vert = createShaderModule(vertCode);
    VkShaderModule frag = createShaderModule(fragCode);

    VkPipelineShaderStageCreateInfo vertStage{};
    vertStage.sType  = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    vertStage.stage  = VK_SHADER_STAGE_VERTEX_BIT;
    vertStage.module = vert;
    vertStage.pName  = "main";

    VkPipelineShaderStageCreateInfo fragStage{};
    fragStage.sType  = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    fragStage.stage  = VK_SHADER_STAGE_FRAGMENT_BIT;
    fragStage.module = frag;
    fragStage.pName  = "main";

    VkPipelineShaderStageCreateInfo stages[] = { vertStage, fragStage };

    VkVertexInputBindingDescription binding{};
    binding.binding   = 0;
    binding.stride    = sizeof(UIVertex);
    binding.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

    std::array<VkVertexInputAttributeDescription, 2> attrs{};
    attrs[0] = { 0, 0, VK_FORMAT_R32G32_SFLOAT, static_cast<uint32_t>(offsetof(UIVertex, pos)) };
    attrs[1] = { 1, 0, VK_FORMAT_R32G32_SFLOAT, static_cast<uint32_t>(offsetof(UIVertex, uv)) };

    VkPipelineVertexInputStateCreateInfo vertexInput{};
    vertexInput.sType                           = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInput.vertexBindingDescriptionCount   = 1;
    vertexInput.pVertexBindingDescriptions      = &binding;
    vertexInput.vertexAttributeDescriptionCount = static_cast<uint32_t>(attrs.size());
    vertexInput.pVertexAttributeDescriptions    = attrs.data();

    VkPipelineInputAssemblyStateCreateInfo inputAsm{};
    inputAsm.sType    = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAsm.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

    VkPipelineViewportStateCreateInfo viewport{};
    viewport.sType         = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewport.viewportCount = 1;
    viewport.scissorCount  = 1;

    VkPipelineRasterizationStateCreateInfo raster{};
    raster.sType       = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    raster.polygonMode = VK_POLYGON_MODE_FILL;
    raster.cullMode    = VK_CULL_MODE_NONE;
    raster.frontFace   = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    raster.lineWidth   = 1.0f;

    VkPipelineMultisampleStateCreateInfo multisample{};
    multisample.sType                = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisample.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    VkPipelineDepthStencilStateCreateInfo depthStencil{};
    depthStencil.sType            = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
    depthStencil.depthTestEnable  = VK_FALSE;
    depthStencil.depthWriteEnable = VK_FALSE;
    depthStencil.depthCompareOp   = VK_COMPARE_OP_ALWAYS;

    // Alpha blending for transparent UI overlays.
    VkPipelineColorBlendAttachmentState blendAttachment{};
    blendAttachment.colorWriteMask      = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
                                          VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    blendAttachment.blendEnable         = VK_TRUE;
    blendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
    blendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    blendAttachment.colorBlendOp        = VK_BLEND_OP_ADD;
    blendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
    blendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ZERO;
    blendAttachment.alphaBlendOp        = VK_BLEND_OP_ADD;

    VkPipelineColorBlendStateCreateInfo colorBlend{};
    colorBlend.sType           = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    colorBlend.attachmentCount = 1;
    colorBlend.pAttachments    = &blendAttachment;

    std::array<VkDynamicState, 2> dynamics = { VK_DYNAMIC_STATE_VIEWPORT,
                                               VK_DYNAMIC_STATE_SCISSOR };
    VkPipelineDynamicStateCreateInfo dynamicState{};
    dynamicState.sType             = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynamicState.dynamicStateCount = static_cast<uint32_t>(dynamics.size());
    dynamicState.pDynamicStates    = dynamics.data();

    VkPushConstantRange pushRange{};
    pushRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
    pushRange.offset     = 0;
    pushRange.size       = sizeof(glm::mat4);

    VkPipelineLayoutCreateInfo layoutInfo{};
    layoutInfo.sType                  = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    layoutInfo.setLayoutCount         = 1;
    layoutInfo.pSetLayouts            = &m_setLayoutTex;
    layoutInfo.pushConstantRangeCount = 1;
    layoutInfo.pPushConstantRanges    = &pushRange;
    check(vkCreatePipelineLayout(m_device, &layoutInfo, nullptr, &m_uiPipelineLayout),
          "vkCreatePipelineLayout(ui)");

    VkGraphicsPipelineCreateInfo pipelineInfo{};
    pipelineInfo.sType               = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipelineInfo.stageCount          = 2;
    pipelineInfo.pStages             = stages;
    pipelineInfo.pVertexInputState   = &vertexInput;
    pipelineInfo.pInputAssemblyState = &inputAsm;
    pipelineInfo.pViewportState      = &viewport;
    pipelineInfo.pRasterizationState = &raster;
    pipelineInfo.pMultisampleState   = &multisample;
    pipelineInfo.pDepthStencilState  = &depthStencil;
    pipelineInfo.pColorBlendState    = &colorBlend;
    pipelineInfo.pDynamicState       = &dynamicState;
    pipelineInfo.layout              = m_uiPipelineLayout;
    pipelineInfo.renderPass          = m_renderPass;
    pipelineInfo.subpass             = 0;

    check(vkCreateGraphicsPipelines(m_device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr,
                                    &m_uiPipeline),
          "vkCreateGraphicsPipelines(ui)");

    vkDestroyShaderModule(m_device, vert, nullptr);
    vkDestroyShaderModule(m_device, frag, nullptr);
}

void VulkanApp::createDepthResources() {
    VkImageCreateInfo ci{};
    ci.sType         = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ci.imageType     = VK_IMAGE_TYPE_2D;
    ci.extent        = { m_swapchainExtent.width, m_swapchainExtent.height, 1 };
    ci.mipLevels     = 1;
    ci.arrayLayers   = 1;
    ci.format        = m_depthFormat;
    ci.tiling        = VK_IMAGE_TILING_OPTIMAL;
    ci.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    ci.usage         = VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
    ci.samples       = VK_SAMPLE_COUNT_1_BIT;
    ci.sharingMode   = VK_SHARING_MODE_EXCLUSIVE;
    check(vkCreateImage(m_device, &ci, nullptr, &m_depthImage), "vkCreateImage(depth)");

    VkMemoryRequirements req;
    vkGetImageMemoryRequirements(m_device, m_depthImage, &req);
    VkMemoryAllocateInfo alloc{};
    alloc.sType           = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    alloc.allocationSize  = req.size;
    alloc.memoryTypeIndex = findMemoryType(req.memoryTypeBits,
                                           VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    check(vkAllocateMemory(m_device, &alloc, nullptr, &m_depthImageMemory),
          "vkAllocateMemory(depth)");
    vkBindImageMemory(m_device, m_depthImage, m_depthImageMemory, 0);

    VkImageViewCreateInfo view{};
    view.sType                       = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    view.image                       = m_depthImage;
    view.viewType                    = VK_IMAGE_VIEW_TYPE_2D;
    view.format                      = m_depthFormat;
    view.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    view.subresourceRange.levelCount = 1;
    view.subresourceRange.layerCount = 1;
    check(vkCreateImageView(m_device, &view, nullptr, &m_depthImageView),
          "vkCreateImageView(depth)");
}

void VulkanApp::createFramebuffers() {
    m_framebuffers.resize(m_swapchainImageViews.size());
    for (size_t i = 0; i < m_swapchainImageViews.size(); ++i) {
        std::array<VkImageView, 2> attachments = { m_swapchainImageViews[i], m_depthImageView };
        VkFramebufferCreateInfo ci{};
        ci.sType           = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        ci.renderPass      = m_renderPass;
        ci.attachmentCount = static_cast<uint32_t>(attachments.size());
        ci.pAttachments    = attachments.data();
        ci.width           = m_swapchainExtent.width;
        ci.height          = m_swapchainExtent.height;
        ci.layers          = 1;
        check(vkCreateFramebuffer(m_device, &ci, nullptr, &m_framebuffers[i]),
              "vkCreateFramebuffer");
    }
}

void VulkanApp::createCommandPool() {
    VkCommandPoolCreateInfo ci{};
    ci.sType            = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    ci.flags            = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    ci.queueFamilyIndex = m_queues.graphics;
    check(vkCreateCommandPool(m_device, &ci, nullptr, &m_commandPool), "vkCreateCommandPool");
}

uint32_t VulkanApp::findMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags props) const {
    VkPhysicalDeviceMemoryProperties memProps;
    vkGetPhysicalDeviceMemoryProperties(m_physicalDevice, &memProps);
    for (uint32_t i = 0; i < memProps.memoryTypeCount; ++i) {
        if ((typeFilter & (1u << i)) &&
            (memProps.memoryTypes[i].propertyFlags & props) == props)
            return i;
    }
    throw std::runtime_error("No suitable memory type");
}

void VulkanApp::createBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                             VkMemoryPropertyFlags props, VkBuffer& buffer,
                             VkDeviceMemory& memory) const {
    VkBufferCreateInfo ci{};
    ci.sType       = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    ci.size        = size;
    ci.usage       = usage;
    ci.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    check(vkCreateBuffer(m_device, &ci, nullptr, &buffer), "vkCreateBuffer");

    VkMemoryRequirements req;
    vkGetBufferMemoryRequirements(m_device, buffer, &req);
    VkMemoryAllocateInfo alloc{};
    alloc.sType           = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    alloc.allocationSize  = req.size;
    alloc.memoryTypeIndex = findMemoryType(req.memoryTypeBits, props);
    check(vkAllocateMemory(m_device, &alloc, nullptr, &memory), "vkAllocateMemory");
    vkBindBufferMemory(m_device, buffer, memory, 0);
}

VkCommandBuffer VulkanApp::beginOneTimeCommands() const {
    VkCommandBufferAllocateInfo alloc{};
    alloc.sType              = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    alloc.level              = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    alloc.commandPool        = m_commandPool;
    alloc.commandBufferCount = 1;
    VkCommandBuffer cmd;
    vkAllocateCommandBuffers(m_device, &alloc, &cmd);

    VkCommandBufferBeginInfo begin{};
    begin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkBeginCommandBuffer(cmd, &begin);
    return cmd;
}

void VulkanApp::endOneTimeCommands(VkCommandBuffer cmd) const {
    vkEndCommandBuffer(cmd);
    VkSubmitInfo submit{};
    submit.sType              = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit.commandBufferCount = 1;
    submit.pCommandBuffers    = &cmd;
    vkQueueSubmit(m_graphicsQueue, 1, &submit, VK_NULL_HANDLE);
    vkQueueWaitIdle(m_graphicsQueue);
    vkFreeCommandBuffers(m_device, m_commandPool, 1, &cmd);
}

void VulkanApp::copyBuffer(VkBuffer src, VkBuffer dst, VkDeviceSize size) const {
    VkCommandBuffer cmd = beginOneTimeCommands();
    VkBufferCopy region{};
    region.size = size;
    vkCmdCopyBuffer(cmd, src, dst, 1, &region);
    endOneTimeCommands(cmd);
}

void VulkanApp::createGeometryBuffers() {
    const auto& vertices = m_character.vertices();
    const auto& indices  = m_character.indices();

    {
        VkDeviceSize size = sizeof(Vertex) * vertices.size();
        VkBuffer staging; VkDeviceMemory stagingMem;
        createBuffer(size, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                     VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                     staging, stagingMem);
        void* data;
        vkMapMemory(m_device, stagingMem, 0, size, 0, &data);
        std::memcpy(data, vertices.data(), static_cast<size_t>(size));
        vkUnmapMemory(m_device, stagingMem);

        createBuffer(size,
                     VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT,
                     VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, m_vertexBuffer, m_vertexBufferMemory);
        copyBuffer(staging, m_vertexBuffer, size);
        vkDestroyBuffer(m_device, staging, nullptr);
        vkFreeMemory(m_device, stagingMem, nullptr);
    }
    {
        VkDeviceSize size = sizeof(uint32_t) * indices.size();
        VkBuffer staging; VkDeviceMemory stagingMem;
        createBuffer(size, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                     VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                     staging, stagingMem);
        void* data;
        vkMapMemory(m_device, stagingMem, 0, size, 0, &data);
        std::memcpy(data, indices.data(), static_cast<size_t>(size));
        vkUnmapMemory(m_device, stagingMem);

        createBuffer(size,
                     VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_INDEX_BUFFER_BIT,
                     VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, m_indexBuffer, m_indexBufferMemory);
        copyBuffer(staging, m_indexBuffer, size);
        vkDestroyBuffer(m_device, staging, nullptr);
        vkFreeMemory(m_device, stagingMem, nullptr);
    }
}

void VulkanApp::createSampler() {
    VkSamplerCreateInfo ci{};
    ci.sType        = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    ci.magFilter    = VK_FILTER_LINEAR;
    ci.minFilter    = VK_FILTER_LINEAR;
    ci.addressModeU = VK_SAMPLER_ADDRESS_MODE_REPEAT;
    ci.addressModeV = VK_SAMPLER_ADDRESS_MODE_REPEAT;
    ci.addressModeW = VK_SAMPLER_ADDRESS_MODE_REPEAT;
    ci.mipmapMode   = VK_SAMPLER_MIPMAP_MODE_LINEAR;
    ci.maxLod       = 0.0f;
    check(vkCreateSampler(m_device, &ci, nullptr, &m_sampler), "vkCreateSampler");
}

VulkanApp::GpuTexture VulkanApp::uploadTexture(const uint8_t* rgba, int w, int h) {
    GpuTexture tex{};
    VkDeviceSize size = static_cast<VkDeviceSize>(w) * h * 4;

    VkBuffer staging; VkDeviceMemory stagingMem;
    createBuffer(size, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                 VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                 staging, stagingMem);
    void* data;
    vkMapMemory(m_device, stagingMem, 0, size, 0, &data);
    std::memcpy(data, rgba, static_cast<size_t>(size));
    vkUnmapMemory(m_device, stagingMem);

    VkImageCreateInfo ci{};
    ci.sType         = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ci.imageType     = VK_IMAGE_TYPE_2D;
    ci.extent        = { static_cast<uint32_t>(w), static_cast<uint32_t>(h), 1 };
    ci.mipLevels     = 1;
    ci.arrayLayers   = 1;
    ci.format        = VK_FORMAT_R8G8B8A8_SRGB;
    ci.tiling        = VK_IMAGE_TILING_OPTIMAL;
    ci.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    ci.usage         = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
    ci.samples       = VK_SAMPLE_COUNT_1_BIT;
    ci.sharingMode   = VK_SHARING_MODE_EXCLUSIVE;
    check(vkCreateImage(m_device, &ci, nullptr, &tex.image), "vkCreateImage(tex)");

    VkMemoryRequirements req;
    vkGetImageMemoryRequirements(m_device, tex.image, &req);
    VkMemoryAllocateInfo alloc{};
    alloc.sType           = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    alloc.allocationSize  = req.size;
    alloc.memoryTypeIndex = findMemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    check(vkAllocateMemory(m_device, &alloc, nullptr, &tex.memory), "vkAllocateMemory(tex)");
    vkBindImageMemory(m_device, tex.image, tex.memory, 0);

    // Transition + copy + transition.
    VkCommandBuffer cmd = beginOneTimeCommands();

    VkImageMemoryBarrier barrier{};
    barrier.sType                       = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    barrier.oldLayout                   = VK_IMAGE_LAYOUT_UNDEFINED;
    barrier.newLayout                   = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    barrier.srcQueueFamilyIndex         = VK_QUEUE_FAMILY_IGNORED;
    barrier.dstQueueFamilyIndex         = VK_QUEUE_FAMILY_IGNORED;
    barrier.image                       = tex.image;
    barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    barrier.subresourceRange.levelCount = 1;
    barrier.subresourceRange.layerCount = 1;
    barrier.srcAccessMask               = 0;
    barrier.dstAccessMask               = VK_ACCESS_TRANSFER_WRITE_BIT;
    vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT,
                         0, 0, nullptr, 0, nullptr, 1, &barrier);

    VkBufferImageCopy region{};
    region.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    region.imageSubresource.layerCount = 1;
    region.imageExtent                 = { static_cast<uint32_t>(w), static_cast<uint32_t>(h), 1 };
    vkCmdCopyBufferToImage(cmd, staging, tex.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

    barrier.oldLayout     = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    barrier.newLayout     = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                         0, 0, nullptr, 0, nullptr, 1, &barrier);

    endOneTimeCommands(cmd);

    vkDestroyBuffer(m_device, staging, nullptr);
    vkFreeMemory(m_device, stagingMem, nullptr);

    VkImageViewCreateInfo viewCI{};
    viewCI.sType                       = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewCI.image                       = tex.image;
    viewCI.viewType                    = VK_IMAGE_VIEW_TYPE_2D;
    viewCI.format                      = VK_FORMAT_R8G8B8A8_SRGB;
    viewCI.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewCI.subresourceRange.levelCount = 1;
    viewCI.subresourceRange.layerCount = 1;
    check(vkCreateImageView(m_device, &viewCI, nullptr, &tex.view), "vkCreateImageView(tex)");
    return tex;
}

void VulkanApp::createTextures() {
    const auto& texs = m_character.textures();
    m_gpuTextures.reserve(texs.size());
    for (const auto& t : texs)
        m_gpuTextures.push_back(uploadTexture(t.rgba.data(), t.width, t.height));

    // 1x1 white fallback for untextured parts.
    const uint8_t white[4] = { 255, 255, 255, 255 };
    m_whiteTexture = uploadTexture(white, 1, 1);
}

void VulkanApp::createLoginResources() {
    if (!m_loginAvailable) return;

    for (const auto& t : m_login.textures())
        m_loginTextures.push_back(uploadTexture(t.rgba.data(), t.width, t.height));

    // Vertex buffer.
    {
        const auto& verts = m_login.vertices();
        VkDeviceSize size = sizeof(UIVertex) * verts.size();
        VkBuffer staging; VkDeviceMemory stagingMem;
        createBuffer(size, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                     VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                     staging, stagingMem);
        void* data;
        vkMapMemory(m_device, stagingMem, 0, size, 0, &data);
        std::memcpy(data, verts.data(), static_cast<size_t>(size));
        vkUnmapMemory(m_device, stagingMem);

        createBuffer(size, VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT,
                     VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                     m_loginVertexBuffer, m_loginVertexBufferMemory);
        copyBuffer(staging, m_loginVertexBuffer, size);
        vkDestroyBuffer(m_device, staging, nullptr);
        vkFreeMemory(m_device, stagingMem, nullptr);
    }
    // Index buffer.
    {
        const auto& idx = m_login.indices();
        VkDeviceSize size = sizeof(uint32_t) * idx.size();
        VkBuffer staging; VkDeviceMemory stagingMem;
        createBuffer(size, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                     VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                     staging, stagingMem);
        void* data;
        vkMapMemory(m_device, stagingMem, 0, size, 0, &data);
        std::memcpy(data, idx.data(), static_cast<size_t>(size));
        vkUnmapMemory(m_device, stagingMem);

        createBuffer(size, VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_INDEX_BUFFER_BIT,
                     VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                     m_loginIndexBuffer, m_loginIndexBufferMemory);
        copyBuffer(staging, m_loginIndexBuffer, size);
        vkDestroyBuffer(m_device, staging, nullptr);
        vkFreeMemory(m_device, stagingMem, nullptr);
    }
}

void VulkanApp::createUniformBuffers() {
    for (int i = 0; i < kFramesInFlight; ++i) {
        createBuffer(sizeof(CameraUBO), VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,
                     VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                     m_cameraUBO[i], m_cameraUBOMemory[i]);
        vkMapMemory(m_device, m_cameraUBOMemory[i], 0, sizeof(CameraUBO), 0, &m_cameraUBOMapped[i]);

        createBuffer(sizeof(BoneUBO), VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,
                     VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                     m_boneUBO[i], m_boneUBOMemory[i]);
        vkMapMemory(m_device, m_boneUBOMemory[i], 0, sizeof(BoneUBO), 0, &m_boneUBOMapped[i]);
    }
}

void VulkanApp::createDescriptorPool() {
    // character textures + white fallback + login textures
    uint32_t texCount = static_cast<uint32_t>(m_gpuTextures.size()) + 1 +
                        static_cast<uint32_t>(m_loginTextures.size());

    std::array<VkDescriptorPoolSize, 2> sizes{};
    sizes[0].type            = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    sizes[0].descriptorCount = static_cast<uint32_t>(kFramesInFlight) * 2;
    sizes[1].type            = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    sizes[1].descriptorCount = texCount;

    VkDescriptorPoolCreateInfo ci{};
    ci.sType         = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    ci.poolSizeCount = static_cast<uint32_t>(sizes.size());
    ci.pPoolSizes    = sizes.data();
    ci.maxSets       = static_cast<uint32_t>(kFramesInFlight) + texCount;
    check(vkCreateDescriptorPool(m_device, &ci, nullptr, &m_descriptorPool),
          "vkCreateDescriptorPool");
}

void VulkanApp::createDescriptorSets() {
    // Per-frame sets (set 0).
    std::array<VkDescriptorSetLayout, kFramesInFlight> layouts;
    layouts.fill(m_setLayoutFrame);
    VkDescriptorSetAllocateInfo alloc{};
    alloc.sType              = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    alloc.descriptorPool     = m_descriptorPool;
    alloc.descriptorSetCount = static_cast<uint32_t>(kFramesInFlight);
    alloc.pSetLayouts        = layouts.data();
    check(vkAllocateDescriptorSets(m_device, &alloc, m_frameSets.data()),
          "vkAllocateDescriptorSets(frame)");

    for (int i = 0; i < kFramesInFlight; ++i) {
        VkDescriptorBufferInfo camInfo{ m_cameraUBO[i], 0, sizeof(CameraUBO) };
        VkDescriptorBufferInfo boneInfo{ m_boneUBO[i], 0, sizeof(BoneUBO) };

        std::array<VkWriteDescriptorSet, 2> writes{};
        writes[0].sType           = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        writes[0].dstSet          = m_frameSets[i];
        writes[0].dstBinding      = 0;
        writes[0].descriptorType  = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        writes[0].descriptorCount = 1;
        writes[0].pBufferInfo     = &camInfo;

        writes[1].sType           = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        writes[1].dstSet          = m_frameSets[i];
        writes[1].dstBinding      = 1;
        writes[1].descriptorType  = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        writes[1].descriptorCount = 1;
        writes[1].pBufferInfo     = &boneInfo;

        vkUpdateDescriptorSets(m_device, static_cast<uint32_t>(writes.size()),
                               writes.data(), 0, nullptr);
    }

    // Per-texture sets (set 1).
    auto allocTexSet = [&](GpuTexture& tex) {
        VkDescriptorSetAllocateInfo a{};
        a.sType              = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        a.descriptorPool     = m_descriptorPool;
        a.descriptorSetCount = 1;
        a.pSetLayouts        = &m_setLayoutTex;
        check(vkAllocateDescriptorSets(m_device, &a, &tex.set),
              "vkAllocateDescriptorSets(tex)");

        VkDescriptorImageInfo imgInfo{};
        imgInfo.sampler     = m_sampler;
        imgInfo.imageView   = tex.view;
        imgInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        VkWriteDescriptorSet w{};
        w.sType           = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        w.dstSet          = tex.set;
        w.dstBinding      = 0;
        w.descriptorType  = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        w.descriptorCount = 1;
        w.pImageInfo      = &imgInfo;
        vkUpdateDescriptorSets(m_device, 1, &w, 0, nullptr);
    };

    for (auto& tex : m_gpuTextures) allocTexSet(tex);
    allocTexSet(m_whiteTexture);
    for (auto& tex : m_loginTextures) allocTexSet(tex);
}

void VulkanApp::createCommandBuffers() {
    VkCommandBufferAllocateInfo alloc{};
    alloc.sType              = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    alloc.commandPool        = m_commandPool;
    alloc.level              = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    alloc.commandBufferCount = static_cast<uint32_t>(kFramesInFlight);
    check(vkAllocateCommandBuffers(m_device, &alloc, m_commandBuffers.data()),
          "vkAllocateCommandBuffers");
}

void VulkanApp::createSyncObjects() {
    VkSemaphoreCreateInfo semInfo{};
    semInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
    VkFenceCreateInfo fenceInfo{};
    fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT;

    for (int i = 0; i < kFramesInFlight; ++i) {
        check(vkCreateSemaphore(m_device, &semInfo, nullptr, &m_imageAvailable[i]),
              "vkCreateSemaphore");
        check(vkCreateSemaphore(m_device, &semInfo, nullptr, &m_renderFinished[i]),
              "vkCreateSemaphore");
        check(vkCreateFence(m_device, &fenceInfo, nullptr, &m_inFlight[i]), "vkCreateFence");
    }
}

// ---------------------------------------------------------------------------
// Frame loop
// ---------------------------------------------------------------------------
void VulkanApp::mainLoop() {
    while (!glfwWindowShouldClose(m_window)) {
        glfwPollEvents();
        drawFrame();
    }
    vkDeviceWaitIdle(m_device);
}

void VulkanApp::updateUniforms(uint32_t frameIndex) {
    float t = static_cast<float>(glfwGetTime() - m_startTime);

    // Frame the character from its bind-pose bounds.
    glm::vec3 bmin = m_character.boundsMin();
    glm::vec3 bmax = m_character.boundsMax();
    glm::vec3 center = (bmin + bmax) * 0.5f;
    float radius = glm::length(bmax - bmin) * 0.5f;
    if (radius < 1e-4f) radius = 1.0f;

    float dist = radius / std::tan(glm::radians(45.0f) * 0.5f) * 1.4f;
    glm::vec3 eye = center + glm::vec3(0.0f, 0.0f, dist);

    // Slowly rotate the character around its vertical axis.
    glm::mat4 spin = glm::translate(glm::mat4(1.0f), center) *
                     glm::rotate(glm::mat4(1.0f), t * 0.6f, glm::vec3(0, 1, 0)) *
                     glm::translate(glm::mat4(1.0f), -center);

    CameraUBO cam{};
    cam.view = glm::lookAt(eye, center, glm::vec3(0.0f, 1.0f, 0.0f)) * spin;

    float aspect = static_cast<float>(m_swapchainExtent.width) /
                   static_cast<float>(m_swapchainExtent.height);
    cam.proj = glm::perspective(glm::radians(45.0f), aspect, radius * 0.01f, radius * 20.0f);
    cam.proj[1][1] *= -1.0f; // Vulkan Y points down.

    cam.lightDir = glm::vec4(glm::normalize(glm::vec3(-0.4f, -1.0f, -0.5f)), 0.0f);
    cam.camPos   = glm::vec4(eye, 1.0f);
    std::memcpy(m_cameraUBOMapped[frameIndex], &cam, sizeof(cam));

    // Animation: loop the first animation's frame range.
    float fStart = m_character.frameStart();
    float fEnd   = m_character.frameEnd();
    float fps    = m_character.framesPerSec();
    float frame  = fStart;
    float span   = fEnd - fStart;
    if (span > 0.0f)
        frame = fStart + std::fmod(t * fps, span);

    BoneUBO bones{};
    auto skin = m_character.skinningMatrices(frame);
    int n = std::min(static_cast<int>(skin.size()), kMaxBones);
    for (int i = 0; i < n; ++i)
        std::memcpy(&bones.bones[i], skin[i].data(), sizeof(float) * 16);
    for (int i = n; i < kMaxBones; ++i)
        bones.bones[i] = glm::mat4(1.0f);
    std::memcpy(m_boneUBOMapped[frameIndex], &bones, sizeof(bones));
}

void VulkanApp::recordCommandBuffer(VkCommandBuffer cmd, uint32_t imageIndex, uint32_t frameIndex) {
    VkCommandBufferBeginInfo begin{};
    begin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    check(vkBeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");

    std::array<VkClearValue, 2> clears{};
    clears[0].color        = { { 0.08f, 0.10f, 0.14f, 1.0f } };
    clears[1].depthStencil = { 1.0f, 0 };

    VkRenderPassBeginInfo rp{};
    rp.sType             = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    rp.renderPass        = m_renderPass;
    rp.framebuffer       = m_framebuffers[imageIndex];
    rp.renderArea.extent = m_swapchainExtent;
    rp.clearValueCount   = static_cast<uint32_t>(clears.size());
    rp.pClearValues      = clears.data();

    vkCmdBeginRenderPass(cmd, &rp, VK_SUBPASS_CONTENTS_INLINE);

    VkViewport viewport{};
    viewport.width    = static_cast<float>(m_swapchainExtent.width);
    viewport.height   = static_cast<float>(m_swapchainExtent.height);
    viewport.maxDepth = 1.0f;
    vkCmdSetViewport(cmd, 0, 1, &viewport);

    VkRect2D scissor{ { 0, 0 }, m_swapchainExtent };
    vkCmdSetScissor(cmd, 0, 1, &scissor);

    if (m_mode == Mode::Login && m_loginAvailable)
        recordLogin(cmd);
    else
        recordCharacter(cmd, frameIndex);

    vkCmdEndRenderPass(cmd);
    check(vkEndCommandBuffer(cmd), "vkEndCommandBuffer");
}

void VulkanApp::recordCharacter(VkCommandBuffer cmd, uint32_t frameIndex) {
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_pipeline);

    VkDeviceSize offsets[] = { 0 };
    vkCmdBindVertexBuffers(cmd, 0, 1, &m_vertexBuffer, offsets);
    vkCmdBindIndexBuffer(cmd, m_indexBuffer, 0, VK_INDEX_TYPE_UINT32);

    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_pipelineLayout, 0, 1,
                            &m_frameSets[frameIndex], 0, nullptr);

    for (const auto& sm : m_character.subMeshes()) {
        VkDescriptorSet texSet = (sm.textureIndex >= 0 &&
                                  sm.textureIndex < static_cast<int>(m_gpuTextures.size()))
                                     ? m_gpuTextures[sm.textureIndex].set
                                     : m_whiteTexture.set;
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_pipelineLayout, 1, 1,
                                &texSet, 0, nullptr);
        vkCmdDrawIndexed(cmd, sm.indexCount, 1, sm.firstIndex, 0, 0);
    }
}

void VulkanApp::recordLogin(VkCommandBuffer cmd) {
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_uiPipeline);

    // Orthographic transform: fit the virtual canvas into the window (letterbox),
    // mapping canvas pixels (Y down) to Vulkan clip space (Y down).
    float winW = static_cast<float>(m_swapchainExtent.width);
    float winH = static_cast<float>(m_swapchainExtent.height);
    float cw   = m_login.canvasWidth();
    float ch   = m_login.canvasHeight();
    float s    = std::min(winW / cw, winH / ch);
    float ox   = (winW - cw * s) * 0.5f;
    float oy   = (winH - ch * s) * 0.5f;

    glm::mat4 ortho(1.0f);
    ortho[0][0] = 2.0f * s / winW;
    ortho[1][1] = 2.0f * s / winH;
    ortho[3][0] = 2.0f * ox / winW - 1.0f;
    ortho[3][1] = 2.0f * oy / winH - 1.0f;

    vkCmdPushConstants(cmd, m_uiPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT, 0,
                       sizeof(glm::mat4), &ortho);

    VkDeviceSize offsets[] = { 0 };
    vkCmdBindVertexBuffers(cmd, 0, 1, &m_loginVertexBuffer, offsets);
    vkCmdBindIndexBuffer(cmd, m_loginIndexBuffer, 0, VK_INDEX_TYPE_UINT32);

    for (const auto& sm : m_login.subMeshes()) {
        if (sm.textureIndex < 0 ||
            sm.textureIndex >= static_cast<int>(m_loginTextures.size()))
            continue;
        VkDescriptorSet texSet = m_loginTextures[sm.textureIndex].set;
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_uiPipelineLayout, 0, 1,
                                &texSet, 0, nullptr);
        vkCmdDrawIndexed(cmd, sm.indexCount, 1, sm.firstIndex, 0, 0);
    }
}

void VulkanApp::drawFrame() {
    vkWaitForFences(m_device, 1, &m_inFlight[m_currentFrame], VK_TRUE, UINT64_MAX);

    uint32_t imageIndex;
    VkResult acq = vkAcquireNextImageKHR(m_device, m_swapchain, UINT64_MAX,
                                         m_imageAvailable[m_currentFrame], VK_NULL_HANDLE,
                                         &imageIndex);
    if (acq == VK_ERROR_OUT_OF_DATE_KHR) {
        recreateSwapchain();
        return;
    }
    if (acq != VK_SUCCESS && acq != VK_SUBOPTIMAL_KHR)
        throw std::runtime_error("vkAcquireNextImageKHR failed");

    if (m_mode == Mode::Character)
        updateUniforms(m_currentFrame);

    vkResetFences(m_device, 1, &m_inFlight[m_currentFrame]);
    vkResetCommandBuffer(m_commandBuffers[m_currentFrame], 0);
    recordCommandBuffer(m_commandBuffers[m_currentFrame], imageIndex, m_currentFrame);

    VkSemaphore waitSems[]   = { m_imageAvailable[m_currentFrame] };
    VkSemaphore signalSems[] = { m_renderFinished[m_currentFrame] };
    VkPipelineStageFlags waitStages[] = { VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT };

    VkSubmitInfo submit{};
    submit.sType                = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit.waitSemaphoreCount   = 1;
    submit.pWaitSemaphores      = waitSems;
    submit.pWaitDstStageMask    = waitStages;
    submit.commandBufferCount   = 1;
    submit.pCommandBuffers      = &m_commandBuffers[m_currentFrame];
    submit.signalSemaphoreCount = 1;
    submit.pSignalSemaphores    = signalSems;
    check(vkQueueSubmit(m_graphicsQueue, 1, &submit, m_inFlight[m_currentFrame]),
          "vkQueueSubmit");

    VkPresentInfoKHR present{};
    present.sType              = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    present.waitSemaphoreCount = 1;
    present.pWaitSemaphores    = signalSems;
    present.swapchainCount     = 1;
    present.pSwapchains        = &m_swapchain;
    present.pImageIndices      = &imageIndex;

    VkResult pres = vkQueuePresentKHR(m_presentQueue, &present);
    if (pres == VK_ERROR_OUT_OF_DATE_KHR || pres == VK_SUBOPTIMAL_KHR || m_framebufferResized) {
        m_framebufferResized = false;
        recreateSwapchain();
    } else if (pres != VK_SUCCESS) {
        throw std::runtime_error("vkQueuePresentKHR failed");
    }

    m_currentFrame = (m_currentFrame + 1) % kFramesInFlight;
}

// ---------------------------------------------------------------------------
// Swapchain recreation / teardown
// ---------------------------------------------------------------------------
void VulkanApp::recreateSwapchain() {
    int w = 0, h = 0;
    glfwGetFramebufferSize(m_window, &w, &h);
    while (w == 0 || h == 0) {
        glfwGetFramebufferSize(m_window, &w, &h);
        glfwWaitEvents();
    }
    vkDeviceWaitIdle(m_device);

    cleanupSwapchain();

    createSwapchain();
    createImageViews();
    createDepthResources();
    createFramebuffers();
}

void VulkanApp::cleanupSwapchain() {
    vkDestroyImageView(m_device, m_depthImageView, nullptr);
    vkDestroyImage(m_device, m_depthImage, nullptr);
    vkFreeMemory(m_device, m_depthImageMemory, nullptr);

    for (auto fb : m_framebuffers)
        vkDestroyFramebuffer(m_device, fb, nullptr);
    m_framebuffers.clear();

    for (auto view : m_swapchainImageViews)
        vkDestroyImageView(m_device, view, nullptr);
    m_swapchainImageViews.clear();

    vkDestroySwapchainKHR(m_device, m_swapchain, nullptr);
    m_swapchain = VK_NULL_HANDLE;
}

void VulkanApp::cleanup() {
    if (m_device != VK_NULL_HANDLE) {
        vkDeviceWaitIdle(m_device);
        cleanupSwapchain();

        auto destroyTex = [&](GpuTexture& t) {
            if (t.view)   vkDestroyImageView(m_device, t.view, nullptr);
            if (t.image)  vkDestroyImage(m_device, t.image, nullptr);
            if (t.memory) vkFreeMemory(m_device, t.memory, nullptr);
            t = {};
        };
        for (auto& t : m_gpuTextures) destroyTex(t);
        m_gpuTextures.clear();
        destroyTex(m_whiteTexture);
        for (auto& t : m_loginTextures) destroyTex(t);
        m_loginTextures.clear();
        if (m_sampler) vkDestroySampler(m_device, m_sampler, nullptr);

        if (m_loginIndexBuffer) vkDestroyBuffer(m_device, m_loginIndexBuffer, nullptr);
        if (m_loginIndexBufferMemory) vkFreeMemory(m_device, m_loginIndexBufferMemory, nullptr);
        if (m_loginVertexBuffer) vkDestroyBuffer(m_device, m_loginVertexBuffer, nullptr);
        if (m_loginVertexBufferMemory) vkFreeMemory(m_device, m_loginVertexBufferMemory, nullptr);

        for (int i = 0; i < kFramesInFlight; ++i) {
            if (m_cameraUBO[i]) vkDestroyBuffer(m_device, m_cameraUBO[i], nullptr);
            if (m_cameraUBOMemory[i]) vkFreeMemory(m_device, m_cameraUBOMemory[i], nullptr);
            if (m_boneUBO[i]) vkDestroyBuffer(m_device, m_boneUBO[i], nullptr);
            if (m_boneUBOMemory[i]) vkFreeMemory(m_device, m_boneUBOMemory[i], nullptr);
        }

        if (m_descriptorPool) vkDestroyDescriptorPool(m_device, m_descriptorPool, nullptr);
        if (m_setLayoutTex) vkDestroyDescriptorSetLayout(m_device, m_setLayoutTex, nullptr);
        if (m_setLayoutFrame) vkDestroyDescriptorSetLayout(m_device, m_setLayoutFrame, nullptr);

        if (m_indexBuffer) vkDestroyBuffer(m_device, m_indexBuffer, nullptr);
        if (m_indexBufferMemory) vkFreeMemory(m_device, m_indexBufferMemory, nullptr);
        if (m_vertexBuffer) vkDestroyBuffer(m_device, m_vertexBuffer, nullptr);
        if (m_vertexBufferMemory) vkFreeMemory(m_device, m_vertexBufferMemory, nullptr);

        for (int i = 0; i < kFramesInFlight; ++i) {
            if (m_renderFinished[i]) vkDestroySemaphore(m_device, m_renderFinished[i], nullptr);
            if (m_imageAvailable[i]) vkDestroySemaphore(m_device, m_imageAvailable[i], nullptr);
            if (m_inFlight[i]) vkDestroyFence(m_device, m_inFlight[i], nullptr);
        }

        if (m_uiPipeline) vkDestroyPipeline(m_device, m_uiPipeline, nullptr);
        if (m_uiPipelineLayout) vkDestroyPipelineLayout(m_device, m_uiPipelineLayout, nullptr);
        if (m_pipeline) vkDestroyPipeline(m_device, m_pipeline, nullptr);
        if (m_pipelineLayout) vkDestroyPipelineLayout(m_device, m_pipelineLayout, nullptr);
        if (m_renderPass) vkDestroyRenderPass(m_device, m_renderPass, nullptr);
        if (m_commandPool) vkDestroyCommandPool(m_device, m_commandPool, nullptr);

        vkDestroyDevice(m_device, nullptr);
        m_device = VK_NULL_HANDLE;
    }

    if (m_debugMessenger) {
        auto fn = reinterpret_cast<PFN_vkDestroyDebugUtilsMessengerEXT>(
            vkGetInstanceProcAddr(m_instance, "vkDestroyDebugUtilsMessengerEXT"));
        if (fn) fn(m_instance, m_debugMessenger, nullptr);
        m_debugMessenger = VK_NULL_HANDLE;
    }
    if (m_surface) { vkDestroySurfaceKHR(m_instance, m_surface, nullptr); m_surface = VK_NULL_HANDLE; }
    if (m_instance) { vkDestroyInstance(m_instance, nullptr); m_instance = VK_NULL_HANDLE; }

    if (m_window) { glfwDestroyWindow(m_window); m_window = nullptr; }
    glfwTerminate();
}

} // namespace poc
