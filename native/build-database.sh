#!/usr/bin/env bash
set -euo pipefail
# Sourced by build.sh after verifying the upstream SQLite amalgamation.
mc_archive="$downloads/sqlite3mc-2.6.1-source.zip"
pin sha256 3f43e7fa28d53c5083ba33b3ac8311e3115d2f2297a61faddf357563f5c6d121 \
    https://github.com/utelle/SQLite3MultipleCiphers/archive/refs/tags/v2.6.1.zip "$mc_archive" "$mc_archive"
mc="$work/SQLite3MultipleCiphers-2.6.1"
if [[ ! -f "$mc/src/sqlite3mc.c" ]]; then python3 -m zipfile -e "$mc_archive" "$work"; fi
patched="$mc/src/sqlite3patched.c"
if [[ ! -f "$work/sqlite-mc.o" || "$root/native/build-database.sh" -nt "$work/sqlite-mc.o" || "$sqlite/sqlite3.c" -nt "$work/sqlite-mc.o" ]]; then
    sh "$mc/scripts/patchsqlite3.sh" "$sqlite/sqlite3.c" > "$patched"
    "$toolchain/x86_64-w64-mingw32-clang" -O2 -DSQLITE_OMIT_LOAD_EXTENSION -DSQLITE_THREADSAFE=1 \
        -DSQLITE_TEMP_STORE=3 -DSQLITE3MC_SECURE_MEMORY=1 -DSQLITE3MC_MAX_PATHNAME=32768 \
        -DHAVE_CIPHER_AES_128_CBC=0 -DHAVE_CIPHER_AES_256_CBC=0 -DHAVE_CIPHER_SQLCIPHER=0 \
        -DHAVE_CIPHER_RC4=0 -DHAVE_CIPHER_ASCON128=0 -DHAVE_CIPHER_AEGIS=0 \
        -I"$mc/src" -c "$mc/src/sqlite3mc.c" -o "$work/sqlite-mc.o"
fi
