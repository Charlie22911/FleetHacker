"""Vendor the existing Windows file helper and its non-system PE dependencies."""
from pathlib import Path
import hashlib,json,shutil,struct
base=Path(__file__).resolve().parents[1]/'.build'/'stego'
source=Path(r'C:\Program Files\Git')
target=base/'runtime'/'file'
def imports(path):
    data=path.read_bytes(); pe=struct.unpack_from('<I',data,0x3c)[0]
    sections=struct.unpack_from('<H',data,pe+6)[0];opt=struct.unpack_from('<H',data,pe+20)[0];start=pe+24
    directory=start+(112 if struct.unpack_from('<H',data,start)[0]==0x20b else 96)
    rva=struct.unpack_from('<I',data,directory+8)[0]; rows=[]
    for i in range(sections):
        off=start+opt+40*i;size,virtual,rawsize,raw=struct.unpack_from('<IIII',data,off+8);rows.append((virtual,max(size,rawsize),raw))
    def offset(r):
        for v,size,raw in rows:
            if v<=r<v+size:return raw+r-v
        raise ValueError('Invalid PE address')
    if not rva:return []
    cursor=offset(rva);result=[]
    while any(data[cursor:cursor+20]):
        name=offset(struct.unpack_from('<I',data,cursor+12)[0]);end=data.index(0,name)
        result.append(data[name:end].decode());cursor+=20
    return result
pending=['file.exe'];copied=[]
while pending:
    name=pending.pop()
    if name.lower() in {p.lower() for p in copied}:continue
    original=source/'usr'/'bin'/name
    if not original.exists():
        if name.lower().startswith(('msys-','lib')):raise RuntimeError('Missing helper dependency '+name)
        continue
    destination=target/'usr'/'bin'/name;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(original,destination)
    copied.append(name);pending+=imports(original)
magic=target/'usr'/'share'/'misc';magic.mkdir(parents=True,exist_ok=True)
shutil.copy2(source/'usr'/'share'/'misc'/'magic.mgc',magic/'magic.mgc')
records=[dict(path=str(p.relative_to(target)).replace('\\','/'),sha256=hashlib.file_digest(p.open('rb'),'sha256').hexdigest()) for p in target.rglob('*') if p.is_file()]
(base/'file-helper-manifest.json').write_text(json.dumps(records,indent=2))
expected=json.loads((Path(__file__).parent/'file-helper-lock.json').read_text())
actual={r['path']:r['sha256'] for r in records};pins={r['path']:r['sha256'] for r in expected}
if any(actual.get(path)!=digest for path,digest in pins.items()):raise RuntimeError('The installed file helper does not match the pinned package')
print(json.dumps(dict(files=copied,bytes=sum(p.stat().st_size for p in target.rglob('*') if p.is_file()))))
