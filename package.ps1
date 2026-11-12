[CmdletBinding()]
param(
    [string]$Application=(Join-Path $PSScriptRoot 'dist/Media Compliance Reviewer.exe'),
    [Parameter(Mandatory)][string]$Companion,
    [Parameter(Mandatory)][string]$MarqoModel,
    [Parameter(Mandatory)][string]$SteganographySource,
    [Parameter(Mandatory)][string]$SqliteCipherSource,
    [string]$Symbols=(Join-Path $PSScriptRoot '.build/Application.pdb'),
    [string]$ManagedAssembly=(Join-Path $PSScriptRoot '.build/Application.exe'),
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist/packages')
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function AddFile($Zip,[string]$Path,[string]$Name){$null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Zip,$Path,$Name,[IO.Compression.CompressionLevel]::Optimal)}
function AddText($Zip,[string]$Name,[string]$Text){$stream=$Zip.CreateEntry($Name).Open();try{$bytes=[Text.Encoding]::UTF8.GetBytes($Text);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}}
foreach($inputFile in @($Application,$Companion,$MarqoModel,$SteganographySource,$SqliteCipherSource,$Symbols,$ManagedAssembly)){if(!(Test-Path -LiteralPath $inputFile -PathType Leaf)){throw "Missing package input: $inputFile"}}
$Application=(Resolve-Path -LiteralPath $Application).Path;$Companion=(Resolve-Path -LiteralPath $Companion).Path;$MarqoModel=(Resolve-Path -LiteralPath $MarqoModel).Path
$SteganographySource=(Resolve-Path -LiteralPath $SteganographySource).Path;$SqliteCipherSource=(Resolve-Path -LiteralPath $SqliteCipherSource).Path;$Symbols=(Resolve-Path -LiteralPath $Symbols).Path;$ManagedAssembly=(Resolve-Path -LiteralPath $ManagedAssembly).Path
$pinPath=Join-Path $PSScriptRoot '.build/Steganography.sha256'
if(!(Test-Path -LiteralPath $pinPath) -or (Hash $Companion) -ne ([IO.File]::ReadAllText($pinPath)).Trim()){throw 'The companion must match the identity embedded by the application build.'}
$modelSource=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Core/DraftModelWorker.cs'))
$modelHash=[regex]::Match($modelSource,'internal const string Sha256="([a-f0-9]{64})"').Groups[1].Value
if(!$modelHash -or (Hash $MarqoModel) -ne $modelHash){throw 'The Marqo model does not match the approved application model.'}
$cipherBuild=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'native/build-database.sh'))
$cipherHash=[regex]::Match($cipherBuild,'pin sha256 ([a-f0-9]{64})').Groups[1].Value
if(!$cipherHash -or (Hash $SqliteCipherSource) -ne $cipherHash){throw 'The encrypted-SQLite source does not match the native build.'}
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Core/Product.cs')),'DotNetVersion="([^"]+)"').Groups[1].Value
if(!$version){throw 'Application version is unavailable.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
$runtime=Join-Path $output 'FleetHacker-Windows-Beta.zip'
$source=Join-Path $output 'FleetHacker-Source-Beta.zip'
if((Test-Path -LiteralPath $runtime) -or (Test-Path -LiteralPath $source)){throw 'Package outputs already exist; choose a new output directory.'}
Push-Location $PSScriptRoot
try{
    if((git status --porcelain)){throw 'Package only committed, clean source.'}
    $commit=(git rev-parse HEAD).Trim();if($LASTEXITCODE -ne 0){throw 'Cannot identify the source commit.'}
    $null=New-Item -ItemType Directory -Force -Path $output
    $work=Join-Path $PSScriptRoot ('.build/package-'+[guid]::NewGuid().ToString('N'))
    $null=New-Item -ItemType Directory -Path $work
    $repository=Join-Path $work 'repository.zip'
    git archive --format=zip --prefix=FleetHacker/ --output=$repository HEAD
    if($LASTEXITCODE -ne 0){throw 'Committed source archive failed.'}
    $runtimeFiles=[ordered]@{'Media Compliance Reviewer.exe'=$Application;'FleetHacker.Steganography.exe'=$Companion;'Marqo-NSFW-384.onnx'=$MarqoModel}
    foreach($guide in @('Database-Protection.md','File-Properties.md','Historical-Evidence.md')){$runtimeFiles['Help/'+$guide]=Join-Path $PSScriptRoot ('docs/'+$guide)}
    $runtimeManifest=[ordered]@{Version=$version;GitCommit=$commit;Files=[ordered]@{}}
    foreach($entry in $runtimeFiles.GetEnumerator()){$runtimeManifest.Files[$entry.Key]=Hash $entry.Value}
    $runtimePart=Join-Path $work 'runtime.partial.zip';$sourcePart=Join-Path $work 'source.partial.zip'
    $zip=[IO.Compression.ZipFile]::Open($runtimePart,[IO.Compression.ZipArchiveMode]::Create)
    try{
        foreach($entry in $runtimeFiles.GetEnumerator()){AddFile $zip $entry.Value $entry.Key}
        AddText $zip 'Read me.txt' @"
FleetHacker for Windows $version

Extract the entire ZIP into a folder. Open Media Compliance Reviewer.exe.
Keep both executables and Marqo-NSFW-384.onnx together.
Marqo AI review and hidden-data scanning are included. No source package is needed.
Use x64 Windows with .NET Framework 4.7.2 or later.

Create or open a database at the welcome screen. New databases are encrypted.
Choose a database password and a different Supervisor password, confirming both.
Use at least 12 characters for each. There is no password recovery.
Supervisor access protects Evidence, including historical records whose original
files are unavailable. Saved file properties remain readable when sources are offline.

This is an unsigned beta. Evaluate with a copy of an existing database.
For updates, close FleetHacker and replace these package files. Keep your databases
and optional Gemma files. Gemma is an optional advanced model; its separate model
downloads are linked in the project README at:
https://github.com/Charlie22911/FleetHacker#download-gemma-for-advanced-ai-review

Help contains protection, ownership and historical Evidence guides and checksums.
The separate FleetHacker-Source-Beta.zip is for developers and contains all source
bundles and diagnostic symbols. It is not required to run this application.
"@
        AddText $zip 'Help/Package.json' ($runtimeManifest|ConvertTo-Json -Depth 5)
    }finally{$zip.Dispose()}
    $dependencyFiles=[ordered]@{'Dependencies/Steganography-Third-Party-Source.zip'=$SteganographySource;'Dependencies/SQLite3-Multiple-Ciphers-2.6.1-Source.zip'=$SqliteCipherSource;'Diagnostics/Media Compliance Reviewer.exe.pdb'=$Symbols}
    $sourceManifest=[ordered]@{Version=$version;GitCommit=$commit;ExecutableSHA256=(Hash $Application);ManagedSHA256=(Hash $ManagedAssembly);Files=[ordered]@{}}
    foreach($entry in $dependencyFiles.GetEnumerator()){$sourceManifest.Files[$entry.Key]=Hash $entry.Value}
    $zip=[IO.Compression.ZipFile]::Open($sourcePart,[IO.Compression.ZipArchiveMode]::Create)
    try{
        $repo=[IO.Compression.ZipFile]::OpenRead($repository)
        try{foreach($entry in $repo.Entries){if($entry.FullName.EndsWith('/')){continue};$inputStream=$entry.Open();$outputStream=$zip.CreateEntry($entry.FullName,[IO.Compression.CompressionLevel]::Optimal).Open();try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose();$inputStream.Dispose()}}}finally{$repo.Dispose()}
        foreach($entry in $dependencyFiles.GetEnumerator()){AddFile $zip $entry.Value $entry.Key}
        AddText $zip 'Diagnostics/matching-build.json' ($sourceManifest|ConvertTo-Json -Depth 5)
        AddText $zip 'Read me.txt' @"
FleetHacker $version - source package

FleetHacker/ contains the complete committed project, including README.md and
build.ps1. Run build commands from that folder. The HTML source is deprecated.
Dependencies/ contains corresponding source bundles for the hidden-data companion
and SQLite3 Multiple Ciphers. Other pinned dependency recipes and upstream source
links are documented in FleetHacker/README.md.
Diagnostics/ contains symbols for the published Windows executable and its hashes.
To diagnose that exact build, copy Media Compliance Reviewer.exe.pdb beside it.

Source commit: $commit
This package is for development. Download FleetHacker-Windows-Beta.zip to run the app.
"@
    }finally{$zip.Dispose()}
    foreach($file in @($runtimePart,$sourcePart)){if((Get-Item -LiteralPath $file).Length -ge 2GB){throw 'A ZIP exceeds the GitHub release asset limit.'}}
    Move-Item -LiteralPath $runtimePart -Destination $runtime
    Move-Item -LiteralPath $sourcePart -Destination $source
    Remove-Item -LiteralPath $repository
    Remove-Item -LiteralPath $work
    Get-Item -LiteralPath $runtime,$source | Select-Object Name,Length
}finally{Pop-Location}
