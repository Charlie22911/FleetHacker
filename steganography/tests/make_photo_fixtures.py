"""Create labeled, separate copies of supplied photos; never modify source files."""
import argparse,hashlib,io,json,zipfile
from pathlib import Path
import numpy as np
from PIL import Image,ImageOps

def digest(data):return hashlib.sha256(data).hexdigest()
def encode(image,kind):
    stream=io.BytesIO();image.save(stream,format=kind,**({'quality':92} if kind=='JPEG' else {}));return stream.getvalue()
def lsb(cover,payload,channels='rgb'):
    pixels=np.array(cover,dtype=np.uint8);order=['rgb'.index(c) for c in channels]
    ordered=pixels[:,:,order].copy().reshape(-1);bits=np.unpackbits(np.frombuffer(payload,dtype=np.uint8))
    if len(bits)>len(ordered):raise ValueError('Payload exceeds one-bit RGB capacity')
    ordered[:len(bits)]=(ordered[:len(bits)]&254)|bits
    pixels[:,:,order]=ordered.reshape(pixels.shape)
    return Image.fromarray(pixels)
def generate(output,sources):
    output=Path(output).resolve();sources=[Path(p).resolve(strict=True) for p in sources]
    before=[dict(path=str(p),sha256=digest(p.read_bytes())) for p in sources]
    output.mkdir(parents=True,exist_ok=False)
    for name in ('cases','source-copies','payloads'):(output/name).mkdir()
    def write(path,data):
        with path.open('xb') as stream:stream.write(data)
    covers=[]
    for index,path in enumerate(sources):
        write(output/'source-copies'/(str(index+1)+path.suffix),path.read_bytes())
        with Image.open(path) as image:
            image=ImageOps.exif_transpose(image).convert('RGB');image.thumbnail((512,512));covers.append(image.copy())
    hidden=covers[1].copy();hidden.thumbnail((48,48))
    payloads={'png':encode(hidden,'PNG'),'jpeg':encode(hidden,'JPEG'),
              'text':b'Portable media review validation: quarterly equipment inventory, room 402, asset 73129.\n'*8}
    stream=io.BytesIO()
    with zipfile.ZipFile(stream,'w',compression=zipfile.ZIP_DEFLATED) as archive:
        for name,data in [('inventory.txt',payloads['text']),('snapshot.png',payloads['png'])]:
            info=zipfile.ZipInfo(name,date_time=(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;archive.writestr(info,data)
    payloads['zip']=stream.getvalue()
    for name,data in payloads.items():write(output/'payloads'/('hidden.'+('txt' if name=='text' else 'jpg' if name=='jpeg' else name)),data)
    cases=[]
    def add(name,data,source,method,payload=None,verified=False):
        write(output/'cases'/name,data)
        cases.append(dict(file='cases/'+name,source=source,method=method,containsHiddenData=payload is not None,
            payloadType=payload,expectedVerified=verified,payloadSha256=digest(payloads[payload]) if payload else None,sha256=digest(data)))
    a,b,c=covers
    for prefix,image,kind in [('a',a,'PNG'),('b',b,'BMP'),('c',c,'PNG')]:
        ext=kind.lower();clean=encode(image,kind);jpeg=encode(image,'JPEG')
        add(prefix+'-clean.'+ext,clean,prefix,'clean control')
        add(prefix+'-clean.jpg',jpeg,prefix,'clean control')
        for payload in (('zip','png','jpeg','text') if prefix=='a' else ('zip','png') if prefix=='b' else ('png',)):
            channels='bgr' if prefix=='c' else 'rgb'
            add(prefix+'-lsb-'+payload+'.'+ext,encode(lsb(image,payloads[payload],channels),kind),prefix,'one RGB LSB; '+channels+' channel order',payload,payload!='text')
        for payload in (('zip','png','jpeg') if prefix=='a' else ('jpeg',) if prefix=='b' else ('png',)):
            add(prefix+'-appended-'+payload+'.jpg',jpeg+payloads[payload],prefix,'appended after JPEG end marker',payload,True)
    damaged=bytearray(payloads['png']);damaged[-1]^=1
    add('a-invalid-png-crc.jpg',encode(a,'JPEG')+damaged,'a','damaged hidden PNG negative control')
    manifest=dict(schemaVersion=1,sources=before,payloads={k:dict(sha256=digest(v),bytes=len(v)) for k,v in payloads.items()},cases=cases)
    write(output/'manifest.json',json.dumps(manifest,indent=2).encode())
    for row,path in zip(before,sources):
        if digest(path.read_bytes())!=row['sha256']:raise RuntimeError('A source photo changed during fixture creation')
    print(json.dumps(dict(output=str(output),cases=len(cases),positives=sum(c['containsHiddenData'] for c in cases),sourceHashesUnchanged=True)),flush=True)
    return manifest
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('output');parser.add_argument('sources',nargs=3)
    args=parser.parse_args();generate(args.output,args.sources)
