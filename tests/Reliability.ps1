[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
$path=(Resolve-Path -LiteralPath $Executable).Path
$directory=New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('../.build/reliability-'+[guid]::NewGuid().ToString('N')))
$info=[Diagnostics.ProcessStartInfo]::new($path,('--verify-reliability "'+$directory.FullName+'"'))
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
$process=[Diagnostics.Process]::Start($info)
try{
    $errors=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync()
    if(!$process.WaitForExit(90000)){$process.Kill();throw 'Reliability checks timed out.'}
    if($process.ExitCode -ne 0){throw $errors.Result}
    $output.Result
}finally{$process.Dispose()}
