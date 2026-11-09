[CmdletBinding()]
param([switch]$Optional)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$null=New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist'),(Join-Path $root '.build')
$destination=Join-Path $root 'dist/FleetHacker.Steganography.exe'
$companionSource=Join-Path (Join-Path $root 'steganography') 'Program.cs'
$payload=Join-Path $root '.build/stego/engine-payload.zip'
$manifest=Join-Path $root '.build/stego/engine-manifest.json'
if(!(Test-Path -LiteralPath $payload) -or !(Test-Path -LiteralPath $manifest)){
    if($Optional){[IO.File]::WriteAllText((Join-Path $root '.build/Steganography.sha256'),'');return}
    throw 'Prepare the analysis runtimes and package-payload.py before building the companion. See steganography/README.md.'
}
$records=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
foreach($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'fh_*.py' -File){
    $record=$records.Files|Where-Object path -eq ('scripts/'+$script.Name)
    if(!$record -or (Get-FileHash -LiteralPath $script.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record.sha256){throw 'Analysis scripts changed. Stage and package the payload again before building.'}
}
& "$framework/csc.exe" /nologo /target:exe /platform:x64 /optimize+ /r:System.Web.Extensions.dll /r:System.IO.Compression.dll "/resource:$payload,EnginePayload.zip" "/resource:$manifest,EngineManifest.json" "/resource:$root/.build/stego/engine-capabilities.json,EngineCapabilities.json" "/out:$destination" $companionSource
if($LASTEXITCODE -ne 0){throw 'Companion compilation failed.'}
$pin=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $root '.build/Steganography.sha256'),$pin,[Text.UTF8Encoding]::new($false))
