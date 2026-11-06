#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
root=$PWD
source "$root/native/dependencies.sh"
work=${MCR_AI_WORK:-"$root/.build/ai"}
mkdir -p "$work"
archive="$work/opencv-5.0.0.tar.gz"
pin sha256 b0528f5a1d379d59d4701cb28c36e22214cc51cf64594e5b56f2d3e6c0233095 \
    https://codeload.github.com/opencv/opencv/tar.gz/refs/tags/5.0.0 "$archive" /dev/null
if [[ ! -f "$work/opencv-5.0.0/CMakeLists.txt" ]]; then tar xf "$archive" -C "$work"; fi
src="$work/opencv-5.0.0"
# OpenCV 5's vendored MLAS selects the POSIX allocator under clang MinGW.
# Use the built-in CPU kernels on this toolchain.
mlas="$src/3rdparty/mlas/CMakeLists.txt"
if ! grep -q 'FleetHacker MinGW' "$mlas"; then
    sed -i '1i if(MINGW) # FleetHacker MinGW\n  return()\nendif()' "$mlas"
fi
cat > "$work/windows.cmake" <<EOF
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER "$toolchain/x86_64-w64-mingw32-clang")
set(CMAKE_CXX_COMPILER "$toolchain/x86_64-w64-mingw32-clang++")
set(CMAKE_RC_COMPILER "$toolchain/x86_64-w64-mingw32-windres")
EOF
cmake -S "$src" -B "$work/cv-build" -G Ninja -DCMAKE_TOOLCHAIN_FILE="$work/windows.cmake" -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$work/cv-install" -DBUILD_LIST=dnn -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTS=OFF \
    -DBUILD_PERF_TESTS=OFF -DBUILD_EXAMPLES=OFF -DBUILD_opencv_apps=OFF -DWITH_IPP=OFF -DWITH_ITT=OFF \
    -DWITH_OPENCL=OFF -DWITH_OPENMP=OFF -DWITH_WIN32UI=OFF -DWITH_FFMPEG=OFF -DWITH_FLATBUFFERS=OFF \
    -DWITH_ADE=OFF -DWITH_TBB=OFF -DWITH_LAPACK=OFF > "$work/cv-config.log" 2>&1 || { tail -60 "$work/cv-config.log"; exit 1; }
ninja -C "$work/cv-build" -j "${MCR_JOBS:-8}" install > "$work/cv-build.log" 2>&1 || { tail -60 "$work/cv-build.log"; exit 1; }
