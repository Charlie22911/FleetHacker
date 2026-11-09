from pathlib import Path
import io,json,sys,zipfile
import numpy as np
from PIL import Image
directory=Path(sys.argv[1]);directory.mkdir(parents=True,exist_ok=True)
random=np.random.default_rng(22911);pixels=random.integers(0,256,(128,128,3),dtype=np.uint8)
Image.fromarray(pixels).save(directory/'clean.png')
memory=io.BytesIO()
with zipfile.ZipFile(memory,'w',compression=zipfile.ZIP_DEFLATED) as archive:
    info=zipfile.ZipInfo('validation.txt',date_time=(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED
    archive.writestr(info,b'FLEETHACKER GENERATED VALIDATION PAYLOAD\n'*50)
payload=memory.getvalue();(directory/'payload.zip').write_bytes(payload)
bits=np.unpackbits(np.frombuffer(payload,dtype=np.uint8));stego=pixels.copy().reshape(-1);stego[:len(bits)]=(stego[:len(bits)]&254)|bits
Image.fromarray(stego.reshape(pixels.shape)).save(directory/'lsb.png')
Image.fromarray(pixels).save(directory/'clean.jpg',quality=95)
(directory/'appended.jpg').write_bytes((directory/'clean.jpg').read_bytes()+payload)
(directory/'double.png').write_bytes((directory/'lsb.png').read_bytes()+payload)
Image.fromarray(pixels).save(directory/'clean.bmp')
print(json.dumps(dict(images=6,payloadBytes=len(payload),directory=str(directory))))
