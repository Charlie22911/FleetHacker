"""Fetch official, checksum-verified build components without a system install."""
from pathlib import Path
import hashlib, json, urllib.request
root=Path(__file__).resolve().parents[1]/'.build'/'stego'
root.mkdir(parents=True,exist_ok=True)
manifest=json.loads((Path(__file__).parent/'rust-components.lock.json').read_text())
pins=[]
for item in manifest['components']:
    package=item['package'];target=item['target'];destination=root/'downloads'/item['file']
    destination.parent.mkdir(exist_ok=True)
    if not destination.exists():
        urllib.request.urlretrieve(item['url'],str(destination)+'.partial')
        Path(str(destination)+'.partial').rename(destination)
    digest=hashlib.file_digest(destination.open('rb'),'sha256').hexdigest()
    if digest!=item['sha256']:raise RuntimeError('Rust component checksum mismatch')
    pins.append(item)
(root/'rust-pins.json').write_text(json.dumps(dict(version=manifest['version'],components=pins),indent=2))
print(manifest['version'],flush=True)
