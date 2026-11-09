"""Bounded original-byte analysis and explainable, non-probabilistic results."""
import hashlib,json,math,os,shutil,tempfile,time
from pathlib import Path

ENGINE_IDS=('aletheia','zsteg','binwalk')
MAX_FINDINGS=1000
MAX_ENGINE_JSON=250000
def fingerprint(path):
    with open(path,'rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def actual_format(header):
    if header.startswith(b'\x89PNG\r\n\x1a\n'):return 'png'
    if header.startswith(b'\xff\xd8\xff'):return 'jpeg'
    if header.startswith(b'BM'):return 'bmp'
    return None
def score(findings):
    groups={}
    for finding in findings:
        kind=finding.get('kind','candidate')
        points=0 if kind=='recognized_structure' else 80 if kind=='validated_payload' else 40 if kind=='statistical' and finding.get('strength')=='moderate' else 20 if kind=='structure_candidate' else 5
        key=finding.get('correlation') or ':'.join(str(finding.get(k,'')) for k in ('engine','kind','method','offset'))
        current=groups.get(key)
        if current is None or points>current['points']:groups[key]=dict(correlation=key,points=points,explanation=finding.get('explanation','Detector indicator'))
    # Independent confirmation bonuses remain disabled until an explicit independence rule is validated.
    return dict(index=max((g['points'] for g in groups.values()),default=0),version='heuristic-1',contributions=list(groups.values()))
def _bounded(record):
    if not isinstance(record,dict) or record.get('state') not in ('completed','partial','unavailable','not_applicable','failed','timed_out','cancelled'):raise ValueError('Invalid engine result')
    findings=record.setdefault('findings',[])
    if not isinstance(findings,list) or len(findings)>MAX_FINDINGS:raise ValueError('Engine finding limit exceeded')
    if len(json.dumps(record,allow_nan=False))>MAX_ENGINE_JSON:raise ValueError('Engine output limit exceeded')
    for item in findings:
        if not isinstance(item,dict):raise ValueError('Invalid evidence record')
        value=item.get('value')
        if value is not None and (not isinstance(value,(int,float)) or not math.isfinite(value)):raise ValueError('Invalid numeric detector output')
    return record
def analyze(path,detectors=None,max_bytes=256*1024*1024,max_pixels=32_000_000):
    detectors=detectors or {};path=Path(path)
    result=dict(schemaVersion=1,source=str(path),sha256=None,size=None,format=None,status='failed',suspicionIndex=None,scoreVersion='heuristic-1',engines=[],findings=[],contributions=[],warnings=[])
    try:
        with path.open('rb') as source:
            before=os.fstat(source.fileno());result['size']=before.st_size
            if before.st_size>max_bytes:result['status']='limited';result['warnings']=['Original exceeds the configured byte limit'];return result
            result['format']=actual_format(source.read(16));source.seek(0)
            if result['format'] is None:result['sha256']=fingerprint(path);result['status']='unsupported';result['warnings']=['Actual file format is outside PNG/JPEG/BMP coverage'];return result
            with tempfile.TemporaryDirectory(prefix='FHStegoInput-') as directory:
                snapshot=Path(directory)/('input.'+result['format']);digest=hashlib.sha256();copied=0
                with snapshot.open('xb') as destination:
                    while True:
                        block=source.read(1024*1024)
                        if not block:break
                        copied+=len(block)
                        if copied>max_bytes:raise ValueError('Original grew beyond its byte limit')
                        digest.update(block);destination.write(block)
                after=os.fstat(source.fileno())
                if copied!=before.st_size or before.st_size!=after.st_size or before.st_mtime_ns!=after.st_mtime_ns:raise ValueError('Original changed during snapshot creation')
                result['sha256']=digest.hexdigest()
                from PIL import Image
                with Image.open(snapshot) as image:
                    width,height=image.size;result['width']=width;result['height']=height;result['mode']=image.mode
                    if width<1 or height<1 or width*height>max_pixels:result['status']='limited';result['warnings']=['Original exceeds the configured pixel limit'];return result
                    image.verify()
                for engine_id in ENGINE_IDS:
                    started=time.monotonic()
                    if engine_id=='zsteg' and result['format']=='jpeg':record=dict(state='not_applicable',reason='zsteg supports PNG and BMP',findings=[])
                    elif engine_id not in detectors:record=dict(state='unavailable',reason='Engine components unavailable',findings=[])
                    else:
                        try:
                            if fingerprint(snapshot)!=result['sha256']:raise ValueError('Private input snapshot was modified')
                            record=_bounded(detectors[engine_id](snapshot,result['format']))
                            if fingerprint(snapshot)!=result['sha256']:raise ValueError('Detector modified its input snapshot')
                        except TimeoutError as error:record=dict(state='timed_out',reason=str(error)[:1024],findings=[])
                        except Exception as error:record=dict(state='failed',reason=str(error)[:1024],findings=[])
                    record['id']=engine_id;record['elapsedMs']=round(1000*(time.monotonic()-started));result['engines'].append(record)
                    for finding in record['findings']:
                        finding['engine']=engine_id;result['findings'].append(finding)
                complete=all(e['state'] in ('completed','not_applicable') for e in result['engines'])
                result['status']='completed' if complete else 'partial'
                if result['findings'] or complete:
                    scored=score(result['findings']);result['suspicionIndex']=scored['index'];result['contributions']=scored['contributions']
                result['warnings'].append('A suspicion index is not a probability. No indicators is not proof that steganography is absent.')
                if not complete:result['warnings'].append('Analysis coverage is incomplete; inspect each engine status.')
    except Exception as error:result['status']='failed';result['warnings'].append(str(error)[:1024])
    return result
