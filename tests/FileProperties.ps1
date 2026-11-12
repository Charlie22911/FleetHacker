[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
$directory=New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('../.build/file-properties-'+[guid]::NewGuid().ToString('N')))
$info=[Diagnostics.ProcessStartInfo]::new((Resolve-Path -LiteralPath $Executable).Path,('--verify-file-properties "'+$directory.FullName+'"'))
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
$process=[Diagnostics.Process]::Start($info)
try{$errorText=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync();if(!$process.WaitForExit(120000)){$process.Kill();throw 'File properties checks timed out.'};if($process.ExitCode -ne 0){throw $errorText.Result};$output.Result;Write-Output $directory.FullName}finally{$process.Dispose()}
