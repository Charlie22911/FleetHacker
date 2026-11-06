@ffmpeg/core 0.12.10 is the single-thread FFmpeg WebAssembly decoder used to capture video posters when browser decoding fails. Upstream package: https://registry.npmjs.org/@ffmpeg/core/-/core-0.12.10.tgz.

See COPYING for the FFmpeg core's GPL-2.0-or-later license and LICENSE for the JavaScript wrapper's MIT notice. The [FFmpeg source](https://github.com/FFmpeg/FFmpeg/tree/n5.1.4) and the [ffmpeg.wasm v12.15 release sources and build scripts](https://github.com/ffmpegwasm/ffmpeg.wasm/tree/71aa99d37c02a7b4c435275ca9ef50e612f6efa1) are available upstream. The Dockerfile in that source snapshot references the bundled codec library sources.

scripts/build-media-preview.cjs embeds the JavaScript and gzip-compressed WASM directly in the HTML. It changes only the WASM memory maximum to 256 MiB; the files under dist retain the upstream bytes. Run this script or scripts/assemble-media-reviewer.cjs to reproduce the embedded build.

The decoder runs in a dedicated worker and reads selected videos through WORKERFS, which supports seeking without copying an entire video into its memory filesystem. It writes one scaled frame, then removes the temporary output and unmounts the original. Captured posters are re-encoded by the browser and stored in SQLite.
