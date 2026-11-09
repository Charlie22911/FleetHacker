from pathlib import Path
import shutil,tomllib,json,hashlib,os
project=Path(__file__).resolve().parents[1];work=project/'.build/stego';cache=Path(os.environ.get('MCR_STEGO_CACHE','/root/.cache/fleethacker-stego'))/'cargo/registry';dest=work/'third-party-source/rust-crates';notices=work/'runtime/binwalk/notices';dest.mkdir(parents=True,exist_ok=True)
records=[]
for package in tomllib.loads((project/'steganography/binwalk.Cargo.lock').read_text())['package']:
 if not package.get('source','').startswith('registry+'):continue
 name=package['name']+'-'+package['version'];archives=list((cache/'cache').glob('*/'+name+'.crate'))
 if not archives:continue
 archive=archives[0]
 digest=hashlib.sha256(archive.read_bytes()).hexdigest()
 if digest!=package['checksum']:raise ValueError('Crate checksum mismatch '+name)
 shutil.copy2(archive,dest/archive.name)
 sources=list((cache/'src').glob('*/'+name))
 if len(sources)!=1:raise ValueError('Missing source directory '+name)
 for source in sources[0].rglob('*'):
  if source.is_file() and source.name.upper().startswith(('LICENSE','LICENCE','COPYING','NOTICE','COPYRIGHT')):
   target=notices/name/source.relative_to(sources[0]);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
 records.append(dict(name=name,sha256=digest))
(work/'rust-crate-manifest.json').write_text(json.dumps(records,indent=2));print(len(records),'verified source crates')
