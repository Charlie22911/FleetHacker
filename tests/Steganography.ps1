[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$Fixture)
$ErrorActionPreference='Stop'
$appPath=(Resolve-Path -LiteralPath $Executable).Path
$fixturePath=(Resolve-Path -LiteralPath $Fixture).Path
$directory=New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('../.build/stego-integration-'+[guid]::NewGuid().ToString('N')))
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$appPath;$info.Arguments='--verify-stego "'+$directory.FullName+'" "'+$fixturePath+'"'
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
$process=[Diagnostics.Process]::Start($info)
try{
    $errors=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync()
    if(!$process.WaitForExit(960000)){$process.Kill();throw 'Analysis checks timed out.'}
    if($process.ExitCode -ne 0){throw $errors.Result}
    $output.Result
}finally{$process.Dispose()}
