#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
root=$PWD
source "$root/native/dependencies.sh"
export PATH="$toolchain:$PATH"
codecs=${MCR_FFMPEG_SOURCE:-"$root/.build/ffmpeg-9.0.2"}
if [[ -z ${MCR_FFMPEG_SOURCE:-} ]]; then
    archive="$downloads/ffmpeg-9.0.2.tar.xz"
    pin sha256 8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e \
        https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz "$archive" "$reference/ffmpeg-build/ffmpeg.tar.xz"
    if [[ ! -f "$codecs/configure" ]]; then tar xf "$archive" -C "$root/.build"; fi
fi
cd "$codecs"
./configure --target-os=mingw32 --arch=x86_64 --enable-cross-compile \
    --cross-prefix=x86_64-w64-mingw32- --cc=x86_64-w64-mingw32-clang \
    --ar=llvm-ar --ranlib=llvm-ranlib --nm=llvm-nm --strip=llvm-strip \
    --extra-cflags='-D_WIN32_WINNT=0x0602 -DWINVER=0x0602' \
    --disable-autodetect --disable-shared --enable-static --disable-everything \
    --disable-programs --disable-doc --disable-debug --disable-network --disable-x86asm \
    --disable-avdevice --disable-avfilter --disable-swresample --disable-hwaccels \
    --enable-demuxer=avi,asf,mov,matroska,mpegps,mpegts,mpegvideo,image_webp_pipe \
    --enable-decoder=av1,h264,hevc,mpeg1video,mpeg2video,mpeg4,msmpeg4v1,msmpeg4v2,msmpeg4v3,vc1,wmv1,wmv2,wmv3,vp6,vp6a,vp6f,vp8,vp9,mjpeg,rawvideo,webp,gif \
    --enable-parser=av1,h264,hevc,mpegvideo,mpeg4video,vc1,vp8,vp9,mjpeg \
    > configure.log 2>&1
make -j "${MCR_JOBS:-8}" > make.log 2>&1
