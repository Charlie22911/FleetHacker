# Media Compliance Reviewer for Windows

Media Compliance Reviewer for Windows is an offline C#/.NET desktop tool for finding and reviewing images and videos in large folder collections. The light and dark WPF workspace has List, Thumbnails and Quick Review views, a review sidebar, and a preview pane.

Open `Media Compliance Reviewer.exe` directly on x64 Windows with .NET Framework 4.7.2 or later. Copying the executable is sufficient on a machine with that Windows component. The application runs offline. SQLite, FFmpeg and dav1d are statically linked, the WPF application and licenses are embedded, and preview isolation runs a second instance of the same executable. The application uses Windows-supplied WPF and Windows Imaging Component. Review databases and their SQLite sidecars live at the selected local destination. Optional ZIP previews can use a temporary media file.

## Open a catalog and scan

1. At startup, choose **Create catalog** or **Open catalog** and select a SQLite database on a local drive. Choose **Later** to go to the main window. When opening a final HTML database, choose a new local filename for the Windows copy.
2. Enter a local folder, mapped drive or UNC media share under **Source root**. In **Maintenance**, use **Use current source path as default** to save it in the database. You can temporarily enter a different path on another machine. Use **Reconnect all sources** to verify a replacement root for previously recorded locations.
3. Start or resume a scan, then choose a review queue from the sidebar or compact **View** dropdown. Use **Options** to enable renamed-media detection or full SHA-256 for the scan. Renamed-media detection checks headers on ordinary files with unknown extensions.
4. Click a List column to sort the current queue or choose **Thumbnails** for an adjustable grid. Double-click an item or choose **Quick Review** to review one item at a time. Swipe or drag the image left for Non-compliant or right for Compliant. With the image focused, the left and right arrow keys do the same. Quick Review advances when the decision is saved. Press **C** or **N** outside an editor for the corresponding decision in any view.
5. Use **Details / notes** or **Inspect** to open an item while scanning. Scroll over a photo to zoom and drag to pan when zoomed. **Play video** in Inspect uses Windows playback when a codec is available. Saved posters remain usable without Windows playback. Use the vault to retain originals and capture Evidence.

The left sidebar holds the review queues, view modes, and catalog tools. Click the arrow at the middle of the sidebar’s right edge, the preview’s left edge, or the top controls’ bottom edge to collapse or reopen that section. The **View** dropdown remains available when the sidebar is closed. In List or Thumbnails, drag the preview divider above or below its arrow to resize the pane. You can also focus the divider with Tab and use the arrow keys. The app remembers the width when it closes.

Use **Maintenance > Sources / reconnect** to check and apply a replacement folder for one root. **Reconnect all sources** handles bulk checks. **Preview tasks** shows the outcome for each item and lets you resume or discard unfinished work. **Export review report** offers CSV and offline HTML, with optional plaintext Evidence details after vault unlock. Select a Non-compliant item for its **Case sheet**, which keeps reviewer and Windows file owner distinct.

This is an unsigned development preview. Use a copy of an existing catalog for evaluation. SVG files can be cataloged and fingerprinted. Windows playback depends on the encoding and installed Windows media components.

## Command-line use

Run the executable with `--help` for the options. Use `--database PATH` to open a catalog, `--create-database PATH` to create one, and `--import-html SOURCE DESTINATION` to convert a final HTML catalog. Use `--open-evidence-package PATH` to open a protected package. Use `--verify-dependencies` to check startup, the isolated image decoder, and Windows cryptography.

For unattended scans, pass `--database PATH --scan-root ROOT` with any desired scan options. Use `--database PATH --resume-scan latest` to resume using the stored scan options. The scan exit codes are `0` for clean completion, `1` for failure, `2` for cancellation, and `3` for completion with file errors. Wait for the process to exit before checking the result.

## Developer build

Extract the source ZIP and open `MediaComplianceReviewer.csproj` to edit the project. The distributable is a native x64 host containing the managed output, so the managed build by itself is an intermediate file. The build uses Windows .NET Framework C# tooling and a developer WSL environment with Bash, OpenSSL, Python 3, curl, tar, make, Ninja and the pinned LLVM-MinGW toolchain. The dependency recipes verify the archives before using them.

Build the codecs once or after changing their configuration. Then link the application from Windows. The commands assume the Ubuntu-24.04 WSL distribution and the project as the current directory.

```powershell
wsl.exe -d Ubuntu-24.04 -- bash native/build-codecs.sh
wsl.exe -d Ubuntu-24.04 -- bash native/build-av1.sh
.\build.ps1
.\tests\Smoke.ps1 -Executable '.\dist\Media Compliance Reviewer.exe'
```

The smoke test copies and renames the executable, checks the catalog and review lifecycle with generated media, opens the WPF gallery, and checks the application and redirected temp directories for extra files. Pass generated media via `-PreviewFixtures` to test additional formats.

## Third-party source and licenses

- SQLite 3.54.1 is public domain. The native build pins the official amalgamation. [SQLite source](https://www.sqlite.org/2026/sqlite-amalgamation-3540100.zip)
- FFmpeg 9.0.2 is built as a static LGPL 2.1-or-later decoder subset. About includes the license. For relinking, use the source, native build recipes and C#/WPF source in this project. The build recipe accepts an explicitly selected local FFmpeg source tree through `MCR_FFMPEG_SOURCE` so that you can rebuild with your changes. [FFmpeg corresponding source](https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz)
- dav1d 1.5.4 supplies the software AV1 decoder under the BSD 2-Clause license. [dav1d source](https://downloads.videolan.org/pub/videolan/dav1d/1.5.4/)
