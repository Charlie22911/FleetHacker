[CmdletBinding()]
param([switch]$ManagedOnly,[switch]$ReuseCompanion,[string]$ManagedOutputDirectory,[string]$OutputName='Media Compliance Reviewer.exe')
$ErrorActionPreference='Stop'
if($OutputName -notmatch '^[^\\/:*?"<>|]+\.exe$'){throw 'OutputName must be an executable filename.'}
$root=$PSScriptRoot
if($ManagedOutputDirectory -and !$ManagedOnly){throw 'ManagedOutputDirectory is only available with ManagedOnly.'}
if(!$ManagedOnly -and !$ReuseCompanion){& "$root/steganography/build.ps1" -Optional}
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$workPath=if($ManagedOutputDirectory){[IO.Path]::GetFullPath($ManagedOutputDirectory)}else{Join-Path $root '.build'}
$work=New-Item -ItemType Directory -Force -Path $workPath
$null=New-Item -ItemType Directory -Force -Path "$root/dist"
$references=@('System','System.Core','System.Xml','System.Xaml','System.Windows.Forms','System.IO.Compression','System.Runtime.Serialization','System.Web.Extensions' | ForEach-Object { "/r:$framework/$_.dll" })
$references+=@('WindowsBase','PresentationCore','PresentationFramework' | ForEach-Object { "/r:$framework/WPF/$_.dll" })
$sources=@(Get-ChildItem -LiteralPath "$root/Core","$root/App" -Filter *.cs -File | ForEach-Object FullName)
$pinPath=Join-Path $root '.build/Steganography.sha256'
if(!(Test-Path -LiteralPath $pinPath)){$pinPath=Join-Path $work.FullName 'Steganography.sha256';[IO.File]::WriteAllText($pinPath,'',[Text.UTF8Encoding]::new($false))}
if($ReuseCompanion){$pin=([IO.File]::ReadAllText($pinPath)).Trim();if($pin -and (!(Test-Path -LiteralPath "$root/dist/FleetHacker.Steganography.exe") -or (Get-FileHash -LiteralPath "$root/dist/FleetHacker.Steganography.exe" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin)){throw 'The existing companion does not match its pinned identity.'}}
$references+="/resource:$pinPath,Steganography.sha256"
& "$framework/csc.exe" /nologo /target:exe /platform:x64 /langversion:5 /optimize+ /debug:full /codepage:65001 "/out:$work/Application.exe" "/resource:$root/App/MainWindow.xaml,MainWindow.xaml" "/resource:$root/native/reviewer.ico,Reviewer.ico" "/resource:$root/native/COPYING.LGPLv2.1,COPYING.LGPLv2.1" "/resource:$root/native/COPYING.dav1d,COPYING.dav1d" "/resource:$root/native/COPYING.draft-ai,COPYING.draft-ai" "/resource:$root/native/COPYING.gemma-runtime,COPYING.gemma-runtime" @references @sources
if($LASTEXITCODE -ne 0){throw 'C# compilation failed.'}
if($ManagedOnly){Write-Output "Managed assembly and matching symbols: $($work.FullName). Native linking and runtime checks were not run.";return}
$linuxPath = & wsl.exe -d Ubuntu-24.04 -- wslpath -a -u "$root"
if($LASTEXITCODE -ne 0){throw 'Cannot locate the Windows project inside WSL.'}
& wsl.exe -d Ubuntu-24.04 -- bash "$linuxPath/native/build.sh"
if($LASTEXITCODE -ne 0){throw 'Native linking failed.'}
Move-Item -LiteralPath "$work/Reviewer-linked.exe" -Destination "$root/dist/$OutputName" -Force
$symbols=New-Item -ItemType Directory -Force -Path "$work/diagnostic-symbols"
Copy-Item -LiteralPath "$work/Application.pdb" -Destination (Join-Path $symbols.FullName ($OutputName+'.pdb')) -Force
$hashes=[ordered]@{Executable=$OutputName;ExecutableSHA256=(Get-FileHash -LiteralPath "$root/dist/$OutputName" -Algorithm SHA256).Hash;ManagedSHA256=(Get-FileHash -LiteralPath "$work/Application.exe" -Algorithm SHA256).Hash;SymbolsSHA256=(Get-FileHash -LiteralPath "$work/Application.pdb" -Algorithm SHA256).Hash}
[IO.File]::WriteAllText((Join-Path $symbols.FullName 'matching-build.json'),($hashes|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Compress-Archive -LiteralPath (Join-Path $symbols.FullName ($OutputName+'.pdb')),(Join-Path $symbols.FullName 'matching-build.json') -DestinationPath "$work/diagnostic-symbols.zip" -Force
