#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
root=$PWD
source "$root/native/dependencies.sh"
archive="$downloads/dav1d-1.5.4.tar.xz"
meson="$downloads/meson-1.9.2.whl"
pin sha256 686616b7c69eb88d44459391ab25cac13b6647a3b288835c5784e71c1514a5c5 \
    https://downloads.videolan.org/pub/videolan/dav1d/1.5.4/dav1d-1.5.4.tar.xz "$archive" /dev/null
pin sha256 1a284dc1912929098a6462401af58dc49ae3f324e94814a38a8f1020cee07cba \
    https://files.pythonhosted.org/packages/d7/ab/115470e7c6dcce024e43e2e00986864c56e48c59554bb19f4b02ed72814c/meson-1.9.2-py3-none-any.whl "$meson" /dev/null
work="$root/.build"
if [[ ! -f "$work/meson/mesonbuild/mesonmain.py" ]]; then python3 -m zipfile -e "$meson" "$work/meson"; fi
if [[ ! -f "$work/dav1d-1.5.4/meson.build" ]]; then tar xf "$archive" -C "$work"; fi
cat > "$work/dav1d-cross.ini" <<EOF
[binaries]
c = '$toolchain/x86_64-w64-mingw32-clang'
ar = '$toolchain/llvm-ar'
strip = '$toolchain/llvm-strip'
windres = '$toolchain/x86_64-w64-mingw32-windres'
[host_machine]
system = 'windows'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
[built-in options]
c_args = ['-D_WIN32_WINNT=0x0602', '-DWINVER=0x0602']
EOF
export PYTHONPATH="$work/meson${PYTHONPATH:+:$PYTHONPATH}"
meson_options=(--cross-file "$work/dav1d-cross.ini" --buildtype release --default-library static --prefix "$work/dav1d-install"
    -Denable_asm=false -Denable_tools=false -Denable_tests=false -Denable_examples=false -Dlogging=false)
python3 -m mesonbuild.mesonmain setup "$work/dav1d-build" "$work/dav1d-1.5.4" "${meson_options[@]}" > "$work/dav1d-setup.log" 2>&1
ninja -C "$work/dav1d-build" -j "${MCR_JOBS:-8}" install > "$work/dav1d-make.log" 2>&1
