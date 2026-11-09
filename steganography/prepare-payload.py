"""Stage and hash the immutable engine package; inputs remain in the build cache."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess
project=Path(__file__).resolve().parents[1];build=project/'.build/stego';runtime=build/'runtime';payload=build/'payload'
payload.mkdir(exist_ok=True)
installed=set()
def install(source,destination):
    installed.add(destination.relative_to(payload).as_posix())
    destination.parent.mkdir(parents=True,exist_ok=True)
    if destination.exists():
        if source.stat().st_size==destination.stat().st_size and hashlib.file_digest(source.open('rb'),'sha256').digest()==hashlib.file_digest(destination.open('rb'),'sha256').digest():return
        destination.unlink()
    try:os.link(source,destination)
    except OSError:shutil.copy2(source,destination)
for name in ('python','ruby','gems','file','binwalk'):
    if not (runtime/name).is_dir():raise RuntimeError('Missing prepared runtime: '+name)
    for source in (runtime/name).rglob('*'):
        if not source.is_file():continue
        relative=source.relative_to(runtime)
        if '__pycache__' in relative.parts or source.suffix in ('.pyc','.pyo') or 'include' in relative.parts:continue
        install(source,payload/relative)
upstream=build/'aletheia-source'
for source in (upstream/'aletheialib').rglob('*.py'):install(source,payload/'aletheia'/source.relative_to(upstream))
models=['effnetb0-init.h5']+['effnetb0-A-alaska2-'+method+'.h5' for method in ('outguess','steghide','nsf5','juniw','lsbr','lsbm','hill','uniw')]+['effnetb0-B-alaska2-lsbm.h5']
for model in models:install(upstream/'aletheia-models'/model,payload/'aletheia/aletheia-models'/model)
install(upstream/'LICENSE.txt',payload/'notices/aletheia-LICENSE.txt')
for source in (project/'steganography').glob('fh_*.py'):install(source,payload/'scripts'/source.name)
files=[]
for relative in sorted(installed):
    source=payload/relative
    with source.open('rb') as stream:files.append(dict(path=relative,size=source.stat().st_size,sha256=hashlib.file_digest(stream,'sha256').hexdigest()))
engines=[dict(Id='aletheia',State='available',Reason='Original color classifiers; full Octave/DCI reliability coverage is incomplete.',Formats=['png','jpeg','bmp'],Version='348dc41be66ad8dfecac54b27ba7f2963a6a3a57'),dict(Id='zsteg',State='available',Reason='Pinned upstream --all scan with file-identification helper.',Formats=['png','bmp'],Version='0.2.14'),dict(Id='binwalk',State='available',Reason='Native Windows signature scan; extraction disabled.',Formats=['png','jpeg','bmp'],Version='3.1.1')]
manifest=dict(ProtocolVersion=1,FullCoverage=False,Engines=engines,Files=files)
(build/'engine-manifest.json').write_text(json.dumps(manifest,separators=(',',':')),encoding='utf-8')
(build/'engine-capabilities.json').write_text(json.dumps({k:manifest[k] for k in ('ProtocolVersion','FullCoverage','Engines')},separators=(',',':')),encoding='utf-8')
print(json.dumps(dict(files=len(files),uncompressedBytes=sum(f['size'] for f in files))))
