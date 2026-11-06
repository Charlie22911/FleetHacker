import pathlib
import sys


def replace(path, original, replacement):
    source = path.read_text(encoding="utf-8")
    if replacement in source:
        return
    if source.count(original) != 1:
        raise RuntimeError(f"Unexpected pinned Vulkan source in {path}")
    path.write_text(source.replace(original, replacement), encoding="utf-8")


backend = pathlib.Path(sys.argv[1]) / "ggml/src/ggml-vulkan"
registry = backend.parent / "ggml-backend-reg.cpp"
replace(registry, '''#ifdef GGML_USE_VULKAN
    // Add runtime disable check
    if (getenv("GGML_DISABLE_VULKAN") == nullptr) {
        register_backend(ggml_backend_vk_reg());
    } else {
        GGML_LOG_DEBUG("Vulkan backend disabled by GGML_DISABLE_VULKAN environment variable\\n");
    }
#endif''', '    // FleetHacker registers Vulkan explicitly inside the Auto GPU worker.')
cmake = backend / "CMakeLists.txt"
replace(cmake, '''find_package(Vulkan COMPONENTS glslc REQUIRED)

if (DEFINED ENV{VULKAN_SDK})
    list(APPEND CMAKE_PREFIX_PATH "$ENV{VULKAN_SDK}")
endif()
find_package(SPIRV-Headers CONFIG REQUIRED)''', '''# FleetHacker uses headers and a build-time shader compiler. Resolve the Windows
# system loader inside the opt-in GPU worker instead of importing it at startup.
set(Vulkan_FOUND TRUE)''')
replace(cmake, '    target_link_libraries(ggml-vulkan PRIVATE Vulkan::Vulkan)',
        '    target_include_directories(ggml-vulkan PRIVATE "${MCR_VULKAN_HEADERS}" "${MCR_SPIRV_HEADERS}")')
replace(cmake, '        ERROR_VARIABLE glslc_error\n',
        '        ERROR_VARIABLE glslc_error\n        RESULT_VARIABLE glslc_result\n')
replace(cmake, '    if (${glslc_error} MATCHES ".*extension not supported: ${EXTENSION_NAME}.*")',
        '    if (NOT glslc_result EQUAL 0)')

source = backend / "ggml-vulkan.cpp"
replace(source, '#include "ggml-vulkan-common.h"\n', '''#include "ggml-vulkan-common.h"
PFN_vkGetInstanceProcAddr mcr_vk_resolver();
#define vkGetInstanceProcAddr mcr_vk_resolver()
''')
replace(source, '    ggml_vk_default_dispatcher_instance.init(vkGetInstanceProcAddr);', '''    auto resolver = mcr_vk_resolver();
    if (!resolver) throw std::runtime_error("The Windows Vulkan loader is unavailable");
    ggml_vk_default_dispatcher_instance.init(resolver);''')
replace(source, 'bool ggml_vk_device_is_supported(const vk::PhysicalDevice & vkdev) {', '''bool ggml_vk_device_is_supported(const vk::PhysicalDevice & vkdev) {
    // A software Vulkan implementation is not a hardware acceleration option.
    if (vkdev.getProperties().deviceType == vk::PhysicalDeviceType::eCpu) return false;''')
