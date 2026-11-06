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
    --disable-programs --enable-ffmpeg --disable-doc --disable-debug --disable-network --disable-x86asm \
    --disable-avdevice --disable-hwaccels --enable-protocol=file \
    --enable-demuxer=avi,asf,mov,matroska,mpegps,mpegts,mpegvideo,image_webp_pipe,flv,gif \
    --enable-muxer=asf --enable-encoder=wmv2,wmav2 \
    --enable-filter=scale,setsar,format,aresample,abuffer,abuffersink,buffer,buffersink,fps \
    --enable-decoder=av1,h264,hevc,mpeg1video,mpeg2video,mpeg4,msmpeg4v1,msmpeg4v2,msmpeg4v3,vc1,wmv1,wmv2,wmv3,vp6,vp6a,vp6f,vp8,vp9,mjpeg,rawvideo,webp,gif,flv,flashsv,flashsv2,aac,mp3,nellymoser,speex,opus,vorbis,pcm_s16le,pcm_u8,adpcm_swf \
    --enable-parser=av1,h264,hevc,mpegvideo,mpeg4video,vc1,vp8,vp9,mjpeg,gif \
    > configure.log 2>&1
make -j "${MCR_JOBS:-8}" > make.log 2>&1
# Build the same frontend for embedding. It receives a fixed UTF-8 argument
# list from FleetHacker's native worker rather than parsing the host argv.
printf '\nmcr-objects:\n\t@printf "%%s\\n" $(filter %%.o,$(OBJS-ffmpeg))\n' > mcr-objects.mk
mapfile -t cli_objects < <(make -s -f Makefile -f mcr-objects.mk mcr-objects)
test "${#cli_objects[@]}" -gt 10
mkdir -p mcr-cli
sed '/^static void prepare_app_arguments(int \*argc_ptr, char \*\*\*argv_ptr)$/ {n; a\    if ((*argv_ptr)[0] \&\& !strcmp((*argv_ptr)[0], "mcr-playback")) return;
}' fftools/cmdutils.c > mcr-cli/cmdutils.c
grep -q mcr-playback mcr-cli/cmdutils.c
read -ra cflags <<< "$(sed -n 's/^CFLAGS=//p' ffbuild/config.mak)"
x86_64-w64-mingw32-clang "${cflags[@]}" -I. -Ifftools -c mcr-cli/cmdutils.c -o mcr-cli/cmdutils.o
llvm-objcopy --redefine-sym main=mcr_ffmpeg_main fftools/ffmpeg.o mcr-cli/ffmpeg.o
for i in "${!cli_objects[@]}"; do
    case ${cli_objects[i]} in
        fftools/cmdutils.o) cli_objects[i]=mcr-cli/cmdutils.o ;;
        fftools/ffmpeg.o) cli_objects[i]=mcr-cli/ffmpeg.o ;;
    esac
done
llvm-ar rcs mcr-cli/libmcr-ffmpeg.a "${cli_objects[@]}"
