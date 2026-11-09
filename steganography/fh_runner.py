import json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from fh_analysis import analyze
from fh_engines import detectors
def main():
    while True:
        line=sys.stdin.buffer.readline(65537)
        if not line:return
        if len(line)>65536:raise ValueError('Analysis request exceeds its limit')
        request=json.loads(line)
        result=analyze(request['path'],detectors=detectors(Path(__file__).resolve().parent.parent))
        print(json.dumps(result,allow_nan=False),flush=True)
if __name__=='__main__':main()
