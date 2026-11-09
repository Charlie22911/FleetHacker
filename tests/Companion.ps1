[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
$appPath=(Resolve-Path -LiteralPath $Executable).Path
$directory=New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('../.build/companion-'+[guid]::NewGuid().ToString('N')))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /platform:x64 ("/out:"+(Join-Path $directory.FullName 'fault.exe')) (Join-Path $PSScriptRoot 'CompanionFixture.cs')
if($LASTEXITCODE -ne 0){throw 'Cannot build companion failure fixtures'}
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$appPath;$info.Arguments='--verify-companion "'+$directory.FullName+'"'
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
$process=[Diagnostics.Process]::Start($info)
try{
    $errors=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync()
    if(!$process.WaitForExit(60000)){$process.Kill();throw 'Companion checks timed out.'}
    if($process.ExitCode -ne 0){throw $errors.Result}
    $output.Result
}finally{$process.Dispose()}
