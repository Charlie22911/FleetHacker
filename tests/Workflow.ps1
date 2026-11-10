[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable,[ValidateSet('selection','gallery','pagination','preview','archive','status')][string[]]$Checks=@('selection','gallery','pagination','preview','archive','status'))
$ErrorActionPreference='Stop'
$executablePath=(Resolve-Path -LiteralPath $Executable).Path
foreach($check in $Checks){
    $directory=New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot ('../.build/workflow-'+$check+'-'+[guid]::NewGuid().ToString('N')))
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$executablePath
    $info.Arguments='--verify-workflow "'+$directory.FullName+'" '+$check
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
    $process=[Diagnostics.Process]::Start($info)
    try{
        $errors=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync()
        if(!$process.WaitForExit(60000)){$process.Kill();throw "Workflow $check timed out."}
        if($process.ExitCode -ne 0){throw "Workflow $check failed: $($errors.Result)"}
        $output.Result
    }finally{$process.Dispose()}
}
