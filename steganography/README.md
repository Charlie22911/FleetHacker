# Windows hidden data analysis

Place the matching `FleetHacker.Steganography.exe` beside `Media Compliance Reviewer.exe` on x64 Windows 10 or later. The **Hidden data analysis** control appears when the file is present. Each application build verifies its companion's SHA-256 before starting it. The companion contains its own Python, Ruby and app-local Microsoft runtime files, model weights, and native Windows Binwalk. It needs no installed interpreter, installer, administrator access, or runtime downloads.

Select up to 200 ordinary items, open **Hidden data analysis**, and choose **Analyze selected**. Analysis uses original PNG, JPEG and BMP bytes, including connected ZIP members. It checks the recorded source identity, makes a private copy, computes full SHA-256, and checks the result's identity before saving. Originals and compliance decisions are unchanged. Evidence items are excluded. Cancel stops the owned process tree; completed database results remain available.

**Saved results** shows engine outcomes and findings for the selected item's current identity. **Export latest result as JSON** includes raw detector output, model scores, engine configurations, limitations, the original fingerprint, package fingerprint, and timestamp. Old runs remain in the database. These metadata and results are not encrypted.

## Coverage and limits

- **Aletheia:** pinned original color-model weights, original top-left crop/zero padding, float32 inputs and the 32 primary DCI transforms. JPEG uses OutGuess, Steghide, nsF5 and J-UNIWARD models; PNG/BMP uses LSBR, LSBM, HILL and S-UNIWARD models. Full Octave WS and DCI reliability coverage is incomplete and is reported as **partial**. Grayscale images are outside these color classifiers. Model outputs are not calibrated real-world probabilities.
- **zsteg:** original `--all --no-color` scan with the pinned `file` identification helper for PNG/BMP. The upstream byte-check default is 256. Targeted ZIP payload recovery validates structure and entry CRCs without executing or writing recovered files.
- **Binwalk:** pinned native Windows signature scan with `--search-all`, one worker, and extraction disabled. Ordinary image/compression headers are informational. Embedded ZIP candidates can receive the same structural and CRC validation.

The suspicion index is a versioned heuristic, not a probability: validated ZIP payloads contribute 80, statistical leads 40, and weak candidates 5. The strongest correlated contribution determines the index; the same recovered payload is not counted twice. A low index or absence of indicators does not establish that hidden data is absent. Partial, unavailable, failed and timed-out engines remain explicit. This package has not established detection parity across the full upstream toolchains.

Each original is limited to 256 MiB and 32 million pixels. Original reads run in a separate owned process with a 60-second limit, including network and ZIP sources. Engine output, findings, processing time, process count, and memory are bounded. Reaching a result limit makes coverage partial. A session unpacks about 2.3 GB into a private, nonce-marked local temporary directory; allow at least 4 GB of free local space. It removes that directory after its owned processes exit. An interrupted application can leave a temporary directory for manual cleanup.

Database schema 14 adds analysis history. Opening an older native database makes a pre-upgrade backup. Older Windows builds cannot open the upgraded copy; retain that backup when older versions are needed.

## Build

The ordinary application build works without prepared analysis assets. It embeds an empty companion identity in that case. Build the companion first, then rebuild the application to embed its matching identity:

```powershell
.\steganography\prepare.ps1
.\build.ps1
```

Preparation uses private build directories, Git for Windows with the exact helper files pinned in `file-helper-lock.json`, 7-Zip, and Ubuntu 24.04 under WSL with the LLVM-MinGW toolchain used by `native/build.sh`. The cross-build cache defaults to `/root/.cache/fleethacker-stego`; override it with `MCR_STEGO_CACHE` inside WSL. Runtime archives, Python wheels, Ruby gems, Rust components, upstream commits and Cargo dependencies are pinned. Start with an empty `.build/stego` when changing dependency locks. Downloads are needed only during preparation.

`prepare-payload.py` hashes every staged file. `package-payload.py` creates the embedded resource. `build.ps1` rejects analysis scripts that differ from the staged manifest. The companion verifies every extracted file against that manifest. Binwalk's build disables its unused Kaleido download feature; signature detection code is unchanged.

Keep the matching third-party source archive with redistributed companion binaries. `prepare-notices.py` collects the file helper's source packages and exact MSYS runtime revision, original engine source, and the source crates gathered by the native build. Runtime and dependency notices are included in the package.

Tests are under `steganography/tests` and `tests/Steganography.ps1`. Use generated labeled fixtures for payload checks. Ordinary photos without verified labels provide functional coverage, not a detection-rate measurement.
