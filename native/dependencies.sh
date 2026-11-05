#!/usr/bin/env bash
# Sourced by the developer build. The application itself never fetches files.
reference="$root/../Media-Compliance-Reviewer-Windows/.build"
downloads="$root/.build/downloads"
mkdir -p "$downloads"

pin() {
    local algorithm=$1 digest=$2 url=$3 local_archive=$4 previous_archive=$5 actual
    if [[ ! -f "$local_archive" ]]; then
        if [[ -f "$previous_archive" ]]; then cp -- "$previous_archive" "$local_archive.partial"
        else curl --fail --silent --show-error --location --retry 3 --output "$local_archive.partial" "$url"; fi
        mv -- "$local_archive.partial" "$local_archive"
    fi
    actual=$(openssl dgst "-$algorithm" -r "$local_archive" | cut -d' ' -f1)
    if [[ "$actual" != "$digest" ]]; then printf 'Dependency integrity check failed: %s\n' "$local_archive" >&2; return 1; fi
}

toolchain_name=llvm-mingw-20261006-ucrt-ubuntu-22.04-x86_64
toolchain=${MCR_MINGW_BIN:-"$downloads/$toolchain_name/bin"}
if [[ -z ${MCR_MINGW_BIN:-} && ! -x "$toolchain/x86_64-w64-mingw32-clang" ]]; then
    pin sha256 5f9c6ed95b2d4bdb2869a488c5fd5857fbdabcf288a0aa3eb1da43f6a08d8ab4 \
        "https://github.com/mstorsjo/llvm-mingw/releases/download/20261006/$toolchain_name.tar.xz" \
        "$downloads/llvm.tar.xz" "$reference/ffmpeg-build/llvm.tar.xz"
    tar -xf "$downloads/llvm.tar.xz" -C "$downloads"
fi
