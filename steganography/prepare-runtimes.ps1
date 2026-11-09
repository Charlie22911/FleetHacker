[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$lock=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'engine-lock.json') -Raw | ConvertFrom-Json
$cache=New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot '.build/stego/downloads')
$runtime=New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot '.build/stego/runtime')
foreach($name in @('python','ruby')){
    $pin=$lock.$name;$suffix=if($name -eq 'ruby'){'.7z'}else{'.tar.gz'}
    $archive=Join-Path $cache.FullName ($name+$suffix)
    if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest -Uri $pin.url -OutFile ($archive+'.partial');Move-Item -LiteralPath ($archive+'.partial') -Destination $archive}
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256){throw "Invalid $name runtime digest"}
    $destination=New-Item -ItemType Directory -Force -Path (Join-Path $runtime.FullName $name)
    if($name -eq 'ruby'){
        & 'C:\Program Files\7-Zip\7z.exe' x $archive ('-o'+$destination.FullName) -y | Out-Null
    }else{tar.exe -xzf $archive -C $destination.FullName}
    if($LASTEXITCODE -ne 0){throw "Cannot extract the $name runtime"}
}
Get-ChildItem -Path (Join-Path $runtime.FullName 'python/python/python.exe'),(Join-Path $runtime.FullName 'ruby/*/bin/ruby.exe') | Select-Object FullName
