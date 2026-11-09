[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable,[string]$OutputDirectory,[hashtable]$SourceOverrides=@{},[switch]$Benchmark,[string]$Case)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('.build/identity-'+[guid]::NewGuid().ToString('N'))}
$work=New-Item -ItemType Directory -Force -Path $OutputDirectory
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$references=@('System','System.Core','System.Xml','System.Xaml','System.Windows.Forms','System.IO.Compression','System.Runtime.Serialization','System.Web.Extensions' | ForEach-Object {"/r:$framework/$_.dll"})
$references+=@('WindowsBase','PresentationCore','PresentationFramework' | ForEach-Object {"/r:$framework/WPF/$_.dll"})
$sources=@(Get-ChildItem -LiteralPath "$root/Core","$root/App" -Filter *.cs -File | ForEach-Object FullName)
foreach($entry in $SourceOverrides.GetEnumerator()){$replace=[IO.Path]::GetFullPath((Join-Path $root $entry.Key));$sources=@($sources | Where-Object {$_ -ne $replace});$sources+=[IO.Path]::GetFullPath($entry.Value)}
$sources+=Join-Path $PSScriptRoot 'IdentityIntegrityFixture.cs'
$managed=Join-Path $work.FullName 'IdentityIntegrity.exe'
& "$framework/csc.exe" /nologo /target:exe /main:IdentityIntegrityFixture /langversion:5 /platform:x64 /optimize+ /codepage:65001 "/out:$managed" "/resource:$root/App/MainWindow.xaml,MainWindow.xaml" "/resource:$root/native/reviewer.ico,Reviewer.ico" @references @sources
if($LASTEXITCODE -ne 0){throw 'Identity fixture compilation failed.'}
$copy=Join-Path $work.FullName 'IdentityIntegrity-host.exe'
Copy-Item -LiteralPath $Executable -Destination $copy
if(-not ('IdentityResourceUpdate' -as [type])){Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IdentityResourceUpdate {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr BeginUpdateResource(string path,bool deleteExisting);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool UpdateResource(IntPtr handle,IntPtr type,IntPtr name,ushort language,byte[] bytes,uint length);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool EndUpdateResource(IntPtr handle,bool discard);
}
'@}
# Replace only the managed application resource in a private host copy. The native
# production libraries and the supplied executable remain untouched.
$handle=[IdentityResourceUpdate]::BeginUpdateResource($copy,$false)
if($handle -eq [IntPtr]::Zero){throw 'Cannot open private identity fixture resource.'}
$commit=$false
try{$bytes=[IO.File]::ReadAllBytes($managed);if(-not [IdentityResourceUpdate]::UpdateResource($handle,[IntPtr]10,[IntPtr]101,1033,$bytes,$bytes.Length)){throw 'Cannot embed identity fixture.'};$commit=$true}
finally{if(-not [IdentityResourceUpdate]::EndUpdateResource($handle,-not $commit)){throw 'Cannot finish identity fixture resource.'}}
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$copy;$info.Arguments='--self-test';$info.UseShellExecute=$false
$data=Join-Path $work.FullName 'data';$info.EnvironmentVariables['FLEETHACKER_IDENTITY_TEST_ROOT']=$data
$info.EnvironmentVariables['FLEETHACKER_IDENTITY_BENCHMARK']=$(if($Benchmark){'1'}else{'0'})
$info.EnvironmentVariables['FLEETHACKER_IDENTITY_CASE']=$Case
$process=[Diagnostics.Process]::Start($info)
try{if(!$process.WaitForExit(180000)){$process.Kill();throw 'Identity fixture timed out.'};Get-Content -LiteralPath (Join-Path $data 'result.txt');if($process.ExitCode -ne 0){throw "Identity regression failed: $($process.ExitCode)"}}
finally{$process.Dispose()}
Write-Output "Identity integrity passed: $($work.FullName)"
