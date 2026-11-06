#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
root=$PWD
source "$root/native/dependencies.sh"
work=${MCR_GEMMA_WORK:-"$root/.build/gemma"}
mkdir -p "$work"
archive="$work/llama-b12141.tar.gz"
pin sha256 6dad0746798a4a9d356bf58eb87bc0ad26699d51f0697cf32b2f2f2a57e965b9 \
    https://codeload.github.com/ggml-org/llama.cpp/tar.gz/refs/tags/b12141 "$archive" /dev/null
if [[ ! -f "$work/llama.cpp-b12141/CMakeLists.txt" ]]; then tar xf "$archive" -C "$work"; fi
sdk=vulkan-sdk-1.4.350.0
pin sha256 70270d10bf2c1e074a06ee37a50b75d332993d1b80a1d9526eeed2da6d82ed22 \
    "https://codeload.github.com/KhronosGroup/Vulkan-Headers/tar.gz/refs/tags/$sdk" "$work/Vulkan-Headers-$sdk.tgz" /dev/null
pin sha256 9905d9341f20388adb852c77dd982f2c4d539fd68e6c1f1bcebf034715f2d1d5 \
    "https://codeload.github.com/KhronosGroup/SPIRV-Headers/tar.gz/refs/tags/$sdk" "$work/SPIRV-Headers-$sdk.tgz" /dev/null
for headers in Vulkan-Headers SPIRV-Headers; do
    if [[ ! -d "$work/$headers-$sdk/include" ]]; then tar xf "$work/$headers-$sdk.tgz" -C "$work"; fi
done
glslc=${MCR_GLSLC:-$(command -v glslc || true)}
if [[ ! -x "$glslc" ]]; then printf '%s\n' 'Install glslc on the build host or set MCR_GLSLC to its path.' >&2; exit 1; fi
python3 "$root/native/prepare-gemma-vulkan.py" "$work/llama.cpp-b12141"
cat > "$work/windows.cmake" <<EOF
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER "$toolchain/x86_64-w64-mingw32-clang")
set(CMAKE_CXX_COMPILER "$toolchain/x86_64-w64-mingw32-clang++")
set(CMAKE_RC_COMPILER "$toolchain/x86_64-w64-mingw32-windres")
EOF
cmake -S "$work/llama.cpp-b12141" -B "$work/win" -G Ninja -DCMAKE_TOOLCHAIN_FILE="$work/windows.cmake" -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_SHARED_LIBS=OFF -DLLAMA_BUILD_TOOLS=ON -DLLAMA_BUILD_TESTS=OFF -DLLAMA_BUILD_EXAMPLES=OFF \
    -DLLAMA_BUILD_SERVER=OFF -DLLAMA_CURL=OFF -DLLAMA_OPENSSL=OFF -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF \
    -DGGML_AVX=ON -DGGML_AVX2=ON -DGGML_F16C=ON -DGGML_FMA=ON -DGGML_BMI2=OFF -DGGML_VULKAN=ON \
    -DVulkan_GLSLC_EXECUTABLE="$glslc" -DMCR_VULKAN_HEADERS="$work/Vulkan-Headers-$sdk/include" -DMCR_SPIRV_HEADERS="$work/SPIRV-Headers-$sdk/include" \
    > "$work/configure.log" 2>&1 || { tail -60 "$work/configure.log"; exit 1; }
cmake --build "$work/win" --target mtmd --parallel "${MCR_JOBS:-8}" > "$work/build.log" 2>&1 || { tail -65 "$work/build.log"; exit 1; }
