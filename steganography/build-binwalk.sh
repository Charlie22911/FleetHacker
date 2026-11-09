#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
work="$PWD/.build/stego"
cache=${MCR_STEGO_CACHE:-/root/.cache/fleethacker-stego}
mkdir -p "$cache/rust" "$cache/rust-unpack" "$cache/cargo" "$work/runtime/binwalk"
for name in rustc-x86_64-unknown-linux-gnu cargo-x86_64-unknown-linux-gnu rust-std-x86_64-unknown-linux-gnu rust-std-x86_64-pc-windows-gnullvm; do
  if [[ ! -f "$cache/rust-unpack/$name.done" ]]; then
    mkdir -p "$cache/rust-unpack/$name"
    tar -xJf "$work/downloads/$name.tar.xz" -C "$cache/rust-unpack/$name" --strip-components=1
    bash "$cache/rust-unpack/$name/install.sh" --prefix="$cache/rust" --disable-ldconfig
    touch "$cache/rust-unpack/$name.done"
  fi
done
export PATH="$cache/rust/bin:/root/.cache/fleethacker-ai/toolchain/bin:$PATH"
export CARGO_HOME="$cache/cargo"
unset CARGO_TARGET_DIR
export CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_LINKER=/root/.cache/fleethacker-ai/toolchain/bin/x86_64-w64-mingw32-clang
export CC_x86_64_pc_windows_gnullvm="$CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_LINKER"
export AR_x86_64_pc_windows_gnullvm=/root/.cache/fleethacker-ai/toolchain/bin/llvm-ar
export RUSTFLAGS='-C target-feature=+crt-static -C link-arg=-static'
cd "$work/binwalk-source"
# Kaleido's build-time downloader is unrelated to signature scanning and starts
# a nested Cargo build. Keep the rendering API, but do not fetch a browser/tool
# that the scan-only companion never invokes.
python3 - <<'PY'
from pathlib import Path
p=Path('Cargo.toml');s=p.read_text()
s=s.replace('features = ["kaleido", "kaleido_download"]','features = ["kaleido"]')
p.write_text(s)
PY
cp "$work/../../steganography/binwalk.Cargo.lock" Cargo.lock
cargo build --locked --release --target x86_64-pc-windows-gnullvm --target-dir "$cache/binwalk-target"
cp "$cache/binwalk-target/x86_64-pc-windows-gnullvm/release/binwalk.exe" "$work/runtime/binwalk/binwalk.exe"
cd "$work/../.."
python3 steganography/collect-binwalk-notices.py
