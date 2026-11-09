"""Create the single-file companion resource from its verified staged files."""
import hashlib,json,zipfile
from pathlib import Path
build=Path(__file__).resolve().parents[1]/'.build/stego'
manifest=json.loads((build/'engine-manifest.json').read_text(encoding='utf-8'))
target=build/'engine-payload.zip'
records={r['path']:r for r in manifest['Files']};existing=set();reusable=target.exists()
if reusable:
    with zipfile.ZipFile(target,'r') as previous:
        for item in previous.infolist():
            record=records.get(item.filename)
            if record is None or item.filename in existing or item.file_size!=record['size']:reusable=False;break
            with previous.open(item) as stream:
                if hashlib.file_digest(stream,'sha256').hexdigest()!=record['sha256']:reusable=False;break
            existing.add(item.filename)
output=target if reusable else target.with_suffix('.partial')
with zipfile.ZipFile(output,'a' if reusable else 'w',zipfile.ZIP_DEFLATED,compresslevel=6,allowZip64=True,strict_timestamps=False) as archive:
    for record in manifest['Files']:
        if reusable and record['path'] in existing:continue
        source=build/'payload'/record['path']
        with source.open('rb') as stream:
            if hashlib.file_digest(stream,'sha256').hexdigest()!=record['sha256']:raise ValueError('Staged payload changed: '+record['path'])
        archive.write(source,record['path'])
if not reusable:target.with_suffix('.partial').replace(target)
with target.open('rb') as stream:print(json.dumps(dict(bytes=target.stat().st_size,sha256=hashlib.file_digest(stream,'sha256').hexdigest())))
