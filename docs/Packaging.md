# Beta packages

Each beta release has two uploaded ZIP files:

- **FleetHacker-Windows-Beta.zip** is the runnable Windows package. It contains the application, matching hidden-data companion, approved Marqo model, a start guide and a Help folder. It contains no source archives, build tools or diagnostic symbols.
- **FleetHacker-Source-Beta.zip** is the developer package. `FleetHacker/` contains the complete committed repository. `Dependencies/` contains the bundled companion and encrypted-SQLite source archives. `Diagnostics/` contains the matching PDB and build identities.

Gemma is an optional advanced model. Its two weights total about 3.4 GB and are separate model downloads linked in the README. The Windows package includes Marqo for AI review without those files.

## Create packages

Build and verify the executable and companion first. Commit the source and documentation, then run `package.ps1` from a clean checkout. Supply the matching companion, approved Marqo model and corresponding dependency source bundles:

```powershell
.\package.ps1 `
    -Companion '.\dist\FleetHacker.Steganography.exe' `
    -MarqoModel 'C:\Models\Marqo-NSFW-384.onnx' `
    -SteganographySource 'C:\Sources\Steganography-Third-Party-Source.zip' `
    -SqliteCipherSource '.\.build\downloads\sqlite3mc-2.6.1-source.zip'
```

The default application input is `dist/Media Compliance Reviewer.exe`; symbols and the managed intermediate come from `.build/`. Override `-Application`, `-Symbols` and `-ManagedAssembly` for another verified build. Outputs go to `dist/packages/`, or `-OutputDirectory`. Existing output ZIPs are preserved.

The script checks the companion identity, approved Marqo hash, pinned encrypted-SQLite source hash, and clean source commit. It records package hashes inside the ZIPs. Validate the resulting contents and packaged executable before uploading. Upload only these two ZIPs. GitHub also adds its automatic repository source links; those do not include the extra dependency bundles or matching symbols in the developer package.
