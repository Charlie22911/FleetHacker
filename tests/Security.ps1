[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
$app=(Resolve-Path -LiteralPath $Executable).Path
$root=New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('../.build/security-run-'+[guid]::NewGuid().ToString('N')))
function Check-App([string]$Directory){
    $info=[Diagnostics.ProcessStartInfo]::new($app,('--verify-security "'+$Directory+'"'))
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
    $process=[Diagnostics.Process]::Start($info)
    try{$errorText=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync();if(!$process.WaitForExit(120000)){$process.Kill();throw 'Security checks timed out.'};if($process.ExitCode -ne 0){throw $errorText.Result};$output.Result}finally{$process.Dispose()}
}
Check-App (Join-Path $root.FullName 'direct')
$physical=New-Item -ItemType Directory -Path (Join-Path $root.FullName 'physical')
$alias=Join-Path $root.FullName 'folder-alias'
$null=New-Item -ItemType Junction -Path $alias -Target $physical.FullName
Check-App $alias
Write-Output ('Security checks passed for direct and folder-alias paths: '+$root.FullName)
