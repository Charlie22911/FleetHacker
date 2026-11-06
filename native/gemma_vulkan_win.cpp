#include <windows.h>
#include <vulkan/vulkan.h>

namespace {
HMODULE system_vulkan() {
    static const HMODULE loader = LoadLibraryExW(L"vulkan-1.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    return loader;
}

template<typename T> T load(const char* name) {
    const auto loader = system_vulkan();
    return loader ? reinterpret_cast<T>(GetProcAddress(loader, name)) : nullptr;
}

PFN_vkGetPhysicalDeviceFeatures2 physical_features() {
    static const auto api = load<PFN_vkGetPhysicalDeviceFeatures2>("vkGetPhysicalDeviceFeatures2");
    return api;
}

PFN_vkGetDeviceProcAddr device_resolver() {
    static const auto api = load<PFN_vkGetDeviceProcAddr>("vkGetDeviceProcAddr");
    return api;
}

PFN_vkCmdCopyBuffer copy_buffer() {
    static const auto api = load<PFN_vkCmdCopyBuffer>("vkCmdCopyBuffer");
    return api;
}
}

PFN_vkGetInstanceProcAddr mcr_vk_resolver() {
    static const auto api = load<PFN_vkGetInstanceProcAddr>("vkGetInstanceProcAddr");
    return physical_features() && device_resolver() && copy_buffer() ? api : nullptr;
}

// The pinned backend also calls these core symbols directly after initialization.
extern "C" VKAPI_ATTR void VKAPI_CALL vkGetPhysicalDeviceFeatures2(VkPhysicalDevice device, VkPhysicalDeviceFeatures2* features) {
    physical_features()(device, features);
}

extern "C" VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetDeviceProcAddr(VkDevice device, const char* name) {
    return device_resolver()(device, name);
}

extern "C" VKAPI_ATTR void VKAPI_CALL vkCmdCopyBuffer(VkCommandBuffer commands, VkBuffer source, VkBuffer destination,
                                                      uint32_t count, const VkBufferCopy* regions) {
    copy_buffer()(commands, source, destination, count, regions);
}
