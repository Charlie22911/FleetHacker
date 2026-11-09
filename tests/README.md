# Windows regression checks

Build the optimized managed intermediate with the Windows .NET Framework compiler, then run the managed checks:

```powershell
.\build.ps1 -ManagedOnly
.\tests\Run.ps1 -ManagedOnly
```

The managed build uses C# 5 and emits matching full PDB symbols while keeping release optimization enabled. Managed checks exercise full-file SHA-256, bounded result rejection, conservative evidence classification, and source-line diagnostics. Windows CI runs this path. This job does not link or validate the native host, codecs, optional analysis companion, GPU inference, or interactive WPF behavior.

After building the complete portable executable, run the release checks on a Windows desktop:

```powershell
.\tests\Run.ps1 -Executable '.\dist\Media Compliance Reviewer.exe' `
  -SteganographyFixture 'C:\fixtures\generated-steganography.png' `
  -VideoFixture 'C:\fixtures\generated-320x180.mp4' `
  -PlaybackFixtures 'C:\fixtures\playback'
```

The entry point includes identity-integrity, Smoke, Workflow, Companion, StegoScan, generated-payload Steganography, VideoPlayback, gallery, review UI, confidence policy, identity/migration/reconnect integrity, and failure-recovery checks. Supply the fixtures and optional model needed for the features being checked. VideoPlayback expects a generated 320 × 180 video lasting at least five seconds. Use `-PreviewFixtures`, `-HtmlFixture`, and `-DraftModel` for the corresponding optional Smoke checks. Use `-NonInteractive` to skip checks that require a Windows desktop.

Each unavailable prerequisite is reported as `SKIP`, with its reason. Skipped checks are not counted as passes. A failing suite stops the run and remains recorded in `.build/regression-*/results.json`. Native linking and the complete interactive suite remain release checks; the managed CI job does not replace them.

## Diagnostic symbols

A complete build creates `.build/diagnostic-symbols.zip` separately from the portable executable. It contains the matching PDB and SHA-256 identities for the executable, embedded managed assembly, and symbols. Keep that archive with the exact executable it describes. To diagnose that build, extract its PDB beside the executable, preserving the name `<executable filename>.pdb`, then run:

```powershell
& '.\dist\Media Compliance Reviewer.exe' --verify-symbols
```

The native host passes this optional symbol file to the CLR together with its embedded managed bytes. The check requires a source filename and line number from an actual exception. Symbols from another build are not interchangeable. The portable application runs without this separate diagnostic file.
