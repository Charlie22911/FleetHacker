#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
root=$PWD
work="$root/.build"
source "$root/native/dependencies.sh"
archive="$downloads/sqlite-amalgamation-3540100.zip"
pin sha3-256 92281c86e2957f9b9305890f9ac04204caa7b677cc868dc326d942689df28e30 \
    https://www.sqlite.org/2026/sqlite-amalgamation-3540100.zip "$archive" "$reference/dotnet-native/sqlite-amalgamation.zip"
sqlite="$work/sqlite-amalgamation-3540100"
if [[ ! -f "$sqlite/sqlite3.c" ]]; then python3 -m zipfile -e "$archive" "$work"; fi
cd "$work"
printf '101 RCDATA "Application.exe"\n1 24 "../native/application.manifest"\n1 ICON "../native/reviewer.ico"\n' > application.rc
"$toolchain/x86_64-w64-mingw32-windres" application.rc -O coff -o application.res
if [[ ! -f sqlite.o || "$sqlite/sqlite3.c" -nt sqlite.o ]]; then
    "$toolchain/x86_64-w64-mingw32-clang" -O2 -DSQLITE_OMIT_LOAD_EXTENSION -DSQLITE_THREADSAFE=1 \
        -c "$sqlite/sqlite3.c" -o sqlite.o
fi
codecs=${MCR_FFMPEG_SOURCE:-"$work/ffmpeg-9.0.2"}
if [[ ! -f "$codecs/libavformat/libavformat.a" ]]; then
    printf '%s\n' 'Build the static codecs with native/build-codecs.sh before linking.' >&2
    exit 1
fi
dav1d="$work/dav1d-install"
if [[ ! -f "$dav1d/lib/libdav1d.a" ]]; then
    printf '%s\n' 'Build the AV1 decoder with native/build-av1.sh before linking.' >&2
    exit 1
fi
# Compile FFmpeg's upstream dav1d adapter against the pinned FFmpeg tree. The
# gallery decoder selects this software adapter explicitly for AV1 streams.
"$toolchain/x86_64-w64-mingw32-clang" -O2 -DHAVE_AV_CONFIG_H -I"$codecs" -I"$dav1d/include" \
    -c "$codecs/libavcodec/libdav1d.c" -o ffmpeg-dav1d.o
"$toolchain/x86_64-w64-mingw32-clang" -O2 -static -municode -mwindows -I"$codecs" \
    -Wl,--dynamicbase,--nxcompat,--high-entropy-va \
    "$root/native/host.c" "$root/native/decode.c" "$root/native/exports.def" application.res sqlite.o ffmpeg-dav1d.o \
    -L"$codecs/libavformat" -L"$codecs/libavcodec" -L"$codecs/libswscale" -L"$codecs/libavutil" \
    -lavformat -lavcodec -lswscale -lavutil "$dav1d/lib/libdav1d.a" -lole32 -loleaut32 -lbcrypt \
    -o "$work/Reviewer-linked.exe"
