[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $project
& "$PSScriptRoot/prepare-runtimes.ps1"
$python=Join-Path $project '.build/stego/runtime/python/python/python.exe'
& $python -m pip install --require-hashes --only-binary=:all: -r "$PSScriptRoot/python-requirements.txt"
if($LASTEXITCODE -ne 0){throw 'Private Python dependencies could not be prepared'}
& $python "$PSScriptRoot/prepare-msvc.py"
if($LASTEXITCODE -ne 0){throw 'Private Microsoft runtime preparation failed'}
$runtime=Join-Path $project '.build/stego/runtime'
$ruby=(Get-ChildItem "$runtime/ruby/*/bin/ruby.exe").FullName
$gem=Join-Path (Split-Path $ruby) 'gem'
$env:GEM_HOME=Join-Path $runtime 'gems';$env:GEM_PATH=$env:GEM_HOME
foreach($pin in (Get-Content "$PSScriptRoot/ruby-gems.lock.json" -Raw|ConvertFrom-Json)){
    $archive=Join-Path $project ('.build/stego/downloads/'+$pin.file)
    if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest $pin.url -OutFile $archive}
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256){throw 'Ruby gem checksum mismatch'}
    & $ruby $gem install --local $archive --install-dir $env:GEM_HOME --ignore-dependencies --no-document
    if($LASTEXITCODE -ne 0){throw 'Private Ruby dependency preparation failed'}
}
$lock=Get-Content "$PSScriptRoot/engine-lock.json" -Raw|ConvertFrom-Json
foreach($engine in @('aletheia','binwalk')){
    $folder=Join-Path $project ('.build/stego/'+$engine+'-source')
    $url=if($engine -eq 'aletheia'){'https://github.com/daniellerch/aletheia.git'}else{'https://github.com/ReFirmLabs/binwalk.git'}
    if(!(Test-Path -LiteralPath $folder)){git clone --filter=blob:none --no-checkout $url $folder;if($LASTEXITCODE -ne 0){throw 'Engine clone failed'}}
    git -C $folder fetch --depth=1 origin $lock.$engine.revision
    if($LASTEXITCODE -ne 0){throw 'Pinned engine fetch failed'}
    if($engine -eq 'aletheia'){
        $paths=@('/aletheialib/','/LICENSE.txt','/aletheia-models/README.md','/aletheia-models/effnetb0-init.h5','/aletheia-models/effnetb0-B-alaska2-lsbm.h5')
        $paths+=@('outguess','steghide','nsf5','juniw','lsbr','lsbm','hill','uniw'|ForEach-Object {'/aletheia-models/effnetb0-A-alaska2-'+$_+'.h5'})
        git -C $folder sparse-checkout set --no-cone @paths
        if($LASTEXITCODE -ne 0){throw 'Model checkout selection failed'}
    }
    git -C $folder checkout --detach $lock.$engine.revision
    if($LASTEXITCODE -ne 0){throw 'Pinned engine checkout failed'}
}
& $python "$PSScriptRoot/prepare-file-helper.py"
if($LASTEXITCODE -ne 0){throw 'Pinned file helper preparation failed'}
& $python "$PSScriptRoot/prepare-rust.py"
if($LASTEXITCODE -ne 0){throw 'Private Rust preparation failed'}
$linuxRoot=& wsl.exe -d Ubuntu-24.04 -- wslpath -a -u $project
& wsl.exe -d Ubuntu-24.04 -- bash "$linuxRoot/steganography/build-binwalk.sh"
if($LASTEXITCODE -ne 0){throw 'Native Binwalk preparation failed'}
& $python "$PSScriptRoot/prepare-notices.py"
if($LASTEXITCODE -ne 0){throw 'Analysis notices preparation failed'}
& $python "$PSScriptRoot/prepare-payload.py"
if($LASTEXITCODE -ne 0){throw 'Analysis payload preparation failed'}
& $python "$PSScriptRoot/package-payload.py"
if($LASTEXITCODE -ne 0){throw 'Analysis payload packaging failed'}
& "$PSScriptRoot/build.ps1"
