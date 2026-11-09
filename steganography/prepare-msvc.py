"""Extract app-local Microsoft runtime DLLs; never execute the installer."""
from pathlib import Path
import hashlib,json,shutil,struct,subprocess,urllib.request
project=Path(__file__).resolve().parents[1];work=project/'.build/stego';lock=json.loads((project/'steganography/msvc-runtime.lock.json').read_text());archive=work/'downloads/vc_redist.x64.exe'
if not archive.exists():urllib.request.urlretrieve(lock['url'],archive)
with archive.open('rb') as stream:
    if hashlib.file_digest(stream,'sha256').hexdigest()!=lock['sha256']:raise ValueError('Microsoft runtime archive checksum mismatch')
data=archive.read_bytes();directory=work/'vcredist';directory.mkdir(exist_ok=True);seven=r'C:\Program Files\7-Zip\7z.exe';position=0;containers=[]
while True:
    position=data.find(b'MSCF',position)
    if position<0:break
    size=struct.unpack_from('<I',data,position+8)[0]
    if size>=36 and position+size<=len(data):
        target=directory/('container'+str(len(containers))+'.cab');target.write_bytes(data[position:position+size]);containers.append(target)
    position+=4
if len(containers)!=2:raise ValueError('Unexpected Microsoft runtime container layout')
subprocess.run([seven,'x',archive,'-o'+str(directory),'-y'],check=True,stdout=subprocess.DEVNULL)
subprocess.run([seven,'x',containers[1],'-o'+str(directory/'attached'),'-y'],check=True,stdout=subprocess.DEVNULL)
subprocess.run([seven,'x',directory/'attached/a12','-o'+str(directory/'crt'),'-y'],check=True,stdout=subprocess.DEVNULL)
destination=work/'runtime/python/python'
for source in (directory/'crt').glob('*.dll_amd64'):
    target=destination/source.name.removesuffix('_amd64')
    if target.exists() and hashlib.sha256(source.read_bytes()).digest()==hashlib.sha256(target.read_bytes()).digest():continue
    shutil.copy2(source,target)
notices=work/'runtime/python/notices/msvc';notices.mkdir(parents=True,exist_ok=True)
for source in directory.glob('u*'):
    if source.is_file() and source.read_bytes().startswith(b'{\\rtf'):shutil.copy2(source,notices/(source.name+'.rtf'))
(notices/'provenance.json').write_text(json.dumps(lock,indent=2))
print('Prepared app-local Microsoft runtime DLLs without installation')
