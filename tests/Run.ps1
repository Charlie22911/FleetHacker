[CmdletBinding()]
param([string]$Executable,[string]$Assembly=(Join-Path $PSScriptRoot '../.build/Application.exe'),[switch]$ManagedOnly,[switch]$NonInteractive,[string]$SteganographyFixture,[string]$VideoFixture,[string]$PlaybackFixtures,[string[]]$PreviewFixtures,[string]$HtmlFixture,[string]$DraftModel)
$ErrorActionPreference='Stop'
$summary=[Collections.Generic.List[object]]::new()
$report=New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot ('../.build/regression-'+[guid]::NewGuid().ToString('N')))
function Suite([string]$Name,[scriptblock]$Body,[string]$SkipReason=''){
    if($SkipReason){$summary.Add([pscustomobject]@{Suite=$Name;Result='SKIP';Reason=$SkipReason});Write-Output "SKIP $Name`: $SkipReason";return}
    try{& $Body;$summary.Add([pscustomobject]@{Suite=$Name;Result='PASS';Reason=''})}
    catch{$summary.Add([pscustomobject]@{Suite=$Name;Result='FAIL';Reason=$_.Exception.Message});throw}
}
function AppCheck([string]$Mode){
    $scratch=New-Item -ItemType Directory -Path (Join-Path $report.FullName $Mode.TrimStart('-'))
    $info=[Diagnostics.ProcessStartInfo]::new($script:app,($Mode+' "'+$scratch.FullName+'"'))
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
    $process=[Diagnostics.Process]::Start($info)
    try{$errors=$process.StandardError.ReadToEndAsync();$output=$process.StandardOutput.ReadToEndAsync();if(!$process.WaitForExit(180000)){$process.Kill();throw "$Mode timed out"};if($process.ExitCode -ne 0){throw $errors.Result};$output.Result}finally{$process.Dispose()}
}
$managedMissing=if(!(Test-Path -LiteralPath $Assembly -PathType Leaf)){'Build the managed assembly and matching PDB first.'}else{''}
$nativeMissing=if($ManagedOnly){'Managed-only run; the native host was not tested.'}elseif(!$Executable -or !(Test-Path -LiteralPath $Executable -PathType Leaf)){'Supply the fully linked portable executable.'}else{''}
$desktopMissing=if($nativeMissing){$nativeMissing}elseif($NonInteractive){'Requires a Windows desktop session.'}else{''}
if(!$nativeMissing){$script:app=(Resolve-Path -LiteralPath $Executable).Path}
$companionMissing=if($nativeMissing){$nativeMissing}elseif(!(Test-Path -LiteralPath (Join-Path (Split-Path $script:app -Parent) 'FleetHacker.Steganography.exe'))){'Requires the matching optional companion beside the executable.'}else{''}
try{
    Suite 'Managed' {& "$PSScriptRoot/Managed.ps1" -Assembly $Assembly} $managedMissing
    Suite 'IdentityIntegrity' {& "$PSScriptRoot/IdentityIntegrity.ps1" -Executable $script:app} $nativeMissing
    Suite 'Reliability' {& "$PSScriptRoot/Reliability.ps1" -Executable $script:app} $nativeMissing
    Suite 'StegoPolicy' {AppCheck '--verify-stego-policy'} $nativeMissing
    Suite 'ReviewUI' {AppCheck '--verify-review-ui'} $desktopMissing
    Suite 'Smoke' {& "$PSScriptRoot/Smoke.ps1" -Executable $script:app -PreviewFixtures $PreviewFixtures -HtmlFixture $HtmlFixture -PlaybackFixtures $PlaybackFixtures -DraftModel $DraftModel} $desktopMissing
    Suite 'Workflow' {& "$PSScriptRoot/Workflow.ps1" -Executable $script:app} $desktopMissing
    Suite 'Gallery' {AppCheck '--verify-gallery'} $desktopMissing
    Suite 'Companion' {& "$PSScriptRoot/Companion.ps1" -Executable $script:app} $companionMissing
    Suite 'StegoScan' {& "$PSScriptRoot/StegoScan.ps1" -Executable $script:app} $desktopMissing
    $payloadMissing=if($companionMissing){$companionMissing}elseif(!$SteganographyFixture){'Supply generated payload fixtures with SteganographyFixture.'}elseif(!(Test-Path -LiteralPath $SteganographyFixture)){'The steganography fixture path does not exist.'}else{''}
    Suite 'Steganography' {& "$PSScriptRoot/Steganography.ps1" -Executable $script:app -Fixture $SteganographyFixture} $payloadMissing
    $videoMissing=if($desktopMissing){$desktopMissing}elseif($managedMissing){$managedMissing}elseif(!$VideoFixture -or !(Test-Path -LiteralPath $VideoFixture)){'Supply a generated 320x180 video of at least five seconds with VideoFixture.'}else{''}
    Suite 'VideoPlayback' {& "$PSScriptRoot/VideoPlayback.ps1" -Assembly ((Resolve-Path -LiteralPath $Assembly).Path) -Fixture ((Resolve-Path -LiteralPath $VideoFixture).Path) -Snapshot (Join-Path $report.FullName 'video-playback.png')} $videoMissing
    if(!$PlaybackFixtures){Suite 'Smoke optional playback formats' {} 'PlaybackFixtures was not supplied.'}
    if(!$PreviewFixtures){Suite 'Smoke optional preview formats' {} 'PreviewFixtures was not supplied.'}
    if(!$HtmlFixture){Suite 'Smoke optional HTML import' {} 'HtmlFixture was not supplied.'}
    if(!$DraftModel){Suite 'Smoke optional native AI model' {} 'DraftModel was not supplied.'}
}finally{
    $summary|Format-Table -AutoSize|Out-String|Write-Output
    [IO.File]::WriteAllText((Join-Path $report.FullName 'results.json'),($summary|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    Write-Output "Regression report: $($report.FullName)"
}
