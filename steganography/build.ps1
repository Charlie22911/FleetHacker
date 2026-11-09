$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$null=New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist'),(Join-Path $root '.build')
$destination=Join-Path $root 'dist/FleetHacker.Steganography.exe'
$companionSource=Join-Path (Join-Path $root 'steganography') 'Program.cs'
& "$framework/csc.exe" /nologo /target:exe /platform:x64 /optimize+ /r:System.Web.Extensions.dll "/out:$destination" $companionSource
if($LASTEXITCODE -ne 0){throw 'Companion compilation failed.'}
$pin=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $root '.build/Steganography.sha256'),$pin,[Text.UTF8Encoding]::new($false))
