"""Prepare notices and corresponding source archives for the optional package."""
from pathlib import Path
import hashlib,json,shutil,subprocess,urllib.request
project=Path(__file__).resolve().parents[1];work=project/'.build/stego';helper=work/'runtime/file';sources=work/'third-party-source';sources.mkdir(exist_ok=True)
git=Path(r'C:\Program Files\Git')
shutil.copy2(git/'LICENSE.txt',helper/'Git-for-Windows-LICENSE.txt')
shutil.copy2(git/'usr/share/licenses/file/COPYING',helper/'file-COPYING.txt')
shutil.copy2(work/'binwalk-source/LICENSE',work/'runtime/binwalk/LICENSE.txt')
for pin in json.loads((project/'steganography/helper-sources.lock.json').read_text()):
    target=sources/pin['file']
    if not target.exists():urllib.request.urlretrieve(pin['url'],target)
    with target.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
    if digest!=pin['sha256']:raise ValueError('Source archive checksum mismatch: '+pin['file'])
runtime_source=work/'msys-runtime-source';revision='b4195d69133078c498a1bf811c4fb0c61fc3c8af'
if not runtime_source.exists():subprocess.run(['git','init',runtime_source],check=True)
subprocess.run(['git','-C',runtime_source,'fetch','--depth=1','https://github.com/git-for-windows/msys2-runtime.git',revision],check=True)
subprocess.run(['git','-C',runtime_source,'archive','--format=tar.gz','--output='+str(sources/'msys2-runtime-b4195d6.tar.gz'),revision],check=True)
license=subprocess.check_output(['git','-C',runtime_source,'show',revision+':winsup/CYGWIN_LICENSE'])
(helper/'CYGWIN_LICENSE.txt').write_bytes(license)
for engine in ('aletheia','binwalk'):
    source=work/(engine+'-source');args=['git','-C',source,'archive','--format=zip','--output='+str(sources/(engine+'-source.zip')),'HEAD']
    if engine=='aletheia':args+=['aletheialib','LICENSE.txt','aletheia-models/README.md']
    subprocess.run(args,check=True)
records=[]
for path in sorted(sources.rglob('*')):
    if path.is_file() and path.name!='source-manifest.json':
        with path.open('rb') as stream:records.append(dict(path=path.relative_to(sources).as_posix(),sha256=hashlib.file_digest(stream,'sha256').hexdigest()))
(sources/'source-manifest.json').write_text(json.dumps(records,indent=2))
print(len(records),'source archives prepared')
