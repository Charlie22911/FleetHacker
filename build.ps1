[CmdletBinding()]
param([switch]$ManagedOnly,[string]$OutputName='Media Compliance Reviewer.exe')
$ErrorActionPreference='Stop'
if($OutputName -notmatch '^[^\\/:*?"<>|]+\.exe$'){throw 'OutputName must be an executable filename.'}
$root=$PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$work=New-Item -ItemType Directory -Force -Path "$root/.build"
$null=New-Item -ItemType Directory -Force -Path "$root/dist"
$references=@('System','System.Core','System.Xml','System.Xaml','System.Windows.Forms','System.IO.Compression','System.Runtime.Serialization','System.Web.Extensions' | ForEach-Object { "/r:$framework/$_.dll" })
$references+=@('WindowsBase','PresentationCore','PresentationFramework' | ForEach-Object { "/r:$framework/WPF/$_.dll" })
$sources=@(Get-ChildItem -LiteralPath "$root/Core","$root/App" -Filter *.cs -File | ForEach-Object FullName)
& "$framework/csc.exe" /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 "/out:$work/Application.exe" "/resource:$root/App/MainWindow.xaml,MainWindow.xaml" "/resource:$root/native/reviewer.ico,Reviewer.ico" "/resource:$root/native/COPYING.LGPLv2.1,COPYING.LGPLv2.1" "/resource:$root/native/COPYING.dav1d,COPYING.dav1d" "/resource:$root/native/COPYING.draft-ai,COPYING.draft-ai" "/resource:$root/native/COPYING.gemma-runtime,COPYING.gemma-runtime" @references @sources
if($LASTEXITCODE -ne 0){throw 'C# compilation failed.'}
if($ManagedOnly){return}
$linuxPath = & wsl.exe -d Ubuntu-24.04 -- wslpath -a -u "$root"
if($LASTEXITCODE -ne 0){throw 'Cannot locate the Windows project inside WSL.'}
& wsl.exe -d Ubuntu-24.04 -- bash "$linuxPath/native/build.sh"
if($LASTEXITCODE -ne 0){throw 'Native linking failed.'}
Move-Item -LiteralPath "$work/Reviewer-linked.exe" -Destination "$root/dist/$OutputName" -Force
