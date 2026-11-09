[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable,[switch]$InterfaceOnly,[switch]$NotesOnly)
$ErrorActionPreference='Stop'
$path=(Resolve-Path -LiteralPath $Executable).Path
$directory=New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('../.build/stego-queue-'+[guid]::NewGuid().ToString('N')))
$mode=if($NotesOnly){'--verify-stego-notes'}elseif($InterfaceOnly){'--verify-stego-interface'}else{'--verify-stego-queue'}
$info=[Diagnostics.ProcessStartInfo]::new($path,($mode+' "'+$directory.FullName+'"'))
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
$process=[Diagnostics.Process]::Start($info)
try{
    $errorText=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync()
    if(!$process.WaitForExit(360000)){$process.Kill();throw 'Scan workflow check timed out.'}
    if($process.ExitCode -ne 0){throw $errorText.Result}
    $output.Result
    'Rendered window: '+(Join-Path $directory.FullName 'hidden-data-scan.png')
}finally{$process.Dispose()}
