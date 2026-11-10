# Windows hidden data analysis

Place the matching `FleetHacker.Steganography.exe` beside `Media Compliance Reviewer.exe` on x64 Windows 10 or later. The **Hidden data scan** control appears when the file is present. Each application build verifies its companion's SHA-256 before starting it. The companion contains its own Python, Ruby and app-local Microsoft runtime files, model weights, and native Windows Binwalk. It needs no installed interpreter, installer, administrator access, or runtime downloads.

Open **Hidden data scan** and choose **Start scan**. Choose **Scan all unchecked photos** to find unchecked photos across review queues and pages, or **Scan selected photos** to check the captured selection again. Reopen the scan window to update that selection. It uses original PNG, JPEG and BMP bytes, including connected ZIP members. Other photo types receive a not-supported result. Evidence items and retired source locations are excluded.

Finished checks are saved for each photo's current identity and scan package. All-unchecked mode skips those photos and retries failed, interrupted or unavailable checks. Selected mode checks the captured selection again. Cancel preserves finished results so the next scan can continue with the remaining photos. A changed photo identity or matching package version requires a new check. Checks that finish with known coverage limits remain labeled as limited.

Findings that qualify for automatic classification appear in the **Steganography** review bucket, highest suspicion index first. The scan opens that bucket when it finishes. Existing saved findings are integrated when an older database opens, without rerunning detectors. Each suspect photo receives notes with its check time, evidence confidence, possible method or embedded file type, detector scores and coverage limits. Confidence describes evidence strength; it is not a calibrated probability. Existing notes are preserved. If a notes field is full or contains preserved imported text, the findings remain available in a separate read-only section of Details / notes.

**Automatic classification confidence** defaults to **90%** and is saved in the database. **Apply to saved findings** reevaluates existing evidence without reading originals or rerunning detectors. Verified embedded ZIP payloads qualify as verified evidence independently of a statistical percentage.

Weak patterns and current Aletheia model outputs remain saved leads, because no reliable confidence calibration is approved for these partial models. They remain inspectable with their notes and original detector scores at every slider setting. Raw images hidden in pixel bit planes can remain saved leads even when their hidden content can be visually reconstructed. Automatic qualification currently verifies embedded ZIP structure and entry CRCs; it does not validate every form of hidden content. The confidence control never converts the heuristic suspicion index into a probability or changes a compliance decision.

Marking a suspect photo Compliant, Non-compliant or Back to review resolves its steganography review flag. Undo restores the flag for that review. A repeated check with the same evidence preserves a completed human review; new evidence or a different analysis package can flag the photo again.

The scan window shows progress and a paged list of photo names and results. Select a row and choose **View result** for a plain-language explanation. **Technical details** contains engine availability and **Export result as JSON**, including raw detector output, model scores, configurations, limitations and fingerprints. Originals and review decisions are unchanged. Results and metadata are not encrypted.

## Coverage and limits

- **Aletheia:** pinned original color-model weights, original top-left crop/zero padding, float32 inputs and the 32 primary DCI transforms. JPEG uses OutGuess, Steghide, nsF5 and J-UNIWARD models; PNG/BMP uses LSBR, LSBM, HILL and S-UNIWARD models. Full Octave WS and DCI reliability coverage is incomplete and is reported as **partial**. Grayscale images are outside these color classifiers. Model outputs are not calibrated real-world probabilities.
- **zsteg:** original `--all --no-color` scan with the pinned `file` identification helper for PNG/BMP. The upstream byte-check default is 256. Targeted ZIP payload recovery validates structure and entry CRCs without executing or writing recovered files.
- **Binwalk:** pinned native Windows signature scan with `--search-all`, one worker, and extraction disabled. Ordinary image/compression headers are informational. Embedded ZIP candidates can receive the same structural and CRC validation.

The suspicion index is a versioned heuristic, not a probability: validated ZIP payloads contribute 80, statistical leads 40, and weak candidates 5. The strongest correlated contribution determines the index; the same recovered payload is not counted twice. A low index or absence of indicators does not establish that hidden data is absent. Partial, unavailable, failed and timed-out engines remain explicit. This package has not established detection parity across the full upstream toolchains.

Each original is limited to 256 MiB and 32 million pixels. Original reads run in a separate owned process with a 60-second limit, including network and ZIP sources. Engine output, findings, processing time, process count, and memory are bounded. Reaching a result limit makes coverage partial. A session unpacks about 2.3 GB into a private, nonce-marked local temporary directory; allow at least 4 GB of free local space. It removes that directory after its owned processes exit. An interrupted application can leave a temporary directory for manual cleanup.

Database schema 16 records automatic qualification alongside analysis history and marks legacy sampled reconnects for explicit review. Opening an older native database makes a pre-upgrade backup. Older Windows builds cannot open the upgraded copy; retain that backup when older versions are needed.

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
