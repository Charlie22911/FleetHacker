[CmdletBinding()]
param([Parameter(Mandatory)][string]$Assembly)
$ErrorActionPreference='Stop'
$assemblyPath=(Resolve-Path -LiteralPath $Assembly).Path
if($PSVersionTable.PSEdition -eq 'Core'){
    & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -STA -File $PSCommandPath -Assembly $assemblyPath
    if($LASTEXITCODE -ne 0){throw 'Managed regression checks failed.'}
    return
}
$pdb=[IO.Path]::ChangeExtension($assemblyPath,'.pdb')
if(!(Test-Path -LiteralPath $pdb)){throw 'The matching managed PDB is required.'}
$loaded=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assemblyPath),[IO.File]::ReadAllBytes($pdb))
$entry=$loaded.GetType('MediaReviewer.ManagedSmoke',$true).GetMethod('Run',[Reflection.BindingFlags]'Static,NonPublic')
$scratch=Join-Path $PSScriptRoot ('../.build/managed-check-'+[guid]::NewGuid().ToString('N'))
try{$result=$entry.Invoke($null,[object[]]@([IO.Path]::GetFullPath($scratch)));if($result -ne 0){throw "Managed checks returned $result"}}catch [Reflection.TargetInvocationException]{throw $_.Exception.InnerException}
