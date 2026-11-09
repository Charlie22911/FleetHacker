"""Run original Aletheia model weights with its DCI primary-score transforms."""
import contextlib,json,os,sys
from pathlib import Path

def primary(nn,image):
    import numpy as np
    shape=nn.shape;patch=np.zeros(shape);d0=min(image.shape[0],shape[0]);d1=min(image.shape[1],shape[1]);d2=min(image.shape[2],shape[2]);patch[:d0,:d1,:d2]=image[:d0,:d1,:d2]
    values=[]
    for flip in (0,1):
        for rotation in (0,1,2,3):
            for roll in (0,128,256,384):
                transformed=patch
                if flip:transformed=np.flip(transformed,axis=1)
                if rotation:transformed=np.rot90(transformed,rotation)
                if roll:transformed=np.roll(transformed,roll,axis=1)
                batch=np.array([transformed]).astype('float32')/255
                values.append(nn.model.predict(batch,verbose=0)[0,1])
    return float(np.mean(values))
def main(request):
    root=Path(request['root']);sys.path.insert(0,str(root/'aletheia'));sys.path.insert(0,str(Path(__file__).parent))
    # Trusted model files are local. Never use an upstream downloader at runtime.
    import socket
    def offline(*args,**kwargs):raise RuntimeError('Network access is disabled in the analysis adapter')
    socket.create_connection=offline;socket.getaddrinfo=offline;socket.socket.connect=offline
    with contextlib.redirect_stdout(sys.stderr):
        import numpy as np
        from imageio.v2 import imread
        from aletheialib.models import NN,load_model
        image=imread(request['path'])
        if image.ndim!=3 or image.shape[2]<3:return dict(state='not_applicable',reason='The pinned color classifiers require RGB channels',findings=[])
        methods=('outguess','steghide','nsf5','juniw') if request['format']=='jpeg' else ('lsbr','lsbm','hill','uniw')
        network=NN('effnetb0',inference=True);findings=[];scores=[]
        for method in methods:
            filename=root/'aletheia/aletheia-models'/('effnetb0-A-alaska2-'+method+'.h5')
            if not filename.is_file():raise FileNotFoundError('A required pinned model is missing')
            load_model(network,'effnetb0-A-alaska2-'+method);value=primary(network,image)
            if not np.isfinite(value) or not 0<=value<=1:raise ValueError('Invalid model output')
            scores.append(dict(method=method,value=value,semantics='Original Aletheia A-model mean across 32 DCI transforms; not a calibrated real-world probability'))
            if value>=.5:findings.append(dict(kind='statistical',strength='moderate',method=method,value=value,correlation='aletheia-statistical',explanation='Pinned Aletheia '+method+' classifier exceeded its 0.5 reference threshold.'))
        return dict(state='partial',version='348dc41be66ad8dfecac54b27ba7f2963a6a3a57',findings=findings,scores=scores,reason='Primary color-classifier scores retained. Octave WS and full DCI reliability checks are unavailable; this is incomplete coverage.',configuration='Original weights, float32, original top-left/padding and 32 DCI transforms; no resizing')
if __name__=='__main__':
    try:
        line=sys.stdin.buffer.readline(16385)
        if len(line)>16384:raise ValueError('Model request too large')
        print(json.dumps(main(json.loads(line)),allow_nan=False))
    except Exception as error:print(str(error),file=sys.stderr);sys.exit(1)
