"""Adapters for pinned upstream engines. No payload is executed."""
import ctypes,hashlib,io,json,os,re,subprocess,sys,tempfile,threading,time,zipfile,zlib
from pathlib import Path

class EngineJob:
    def __init__(self,memory_mb):
        self.handle=None
        if os.name!='nt':return
        from ctypes import wintypes as w
        class Basic(ctypes.Structure):_fields_=[('process',ctypes.c_longlong),('job',ctypes.c_longlong),('flags',w.DWORD),('minimum',ctypes.c_size_t),('maximum',ctypes.c_size_t),('active',w.DWORD),('affinity',ctypes.c_size_t),('priority',w.DWORD),('scheduling',w.DWORD)]
        class Io(ctypes.Structure):_fields_=[(name,ctypes.c_ulonglong) for name in ('read_ops','write_ops','other_ops','read_bytes','write_bytes','other_bytes')]
        class Limits(ctypes.Structure):_fields_=[('basic',Basic),('io',Io),('process_memory',ctypes.c_size_t),('job_memory',ctypes.c_size_t),('peak_process',ctypes.c_size_t),('peak_job',ctypes.c_size_t)]
        self.api=ctypes.WinDLL('kernel32',use_last_error=True)
        self.api.CreateJobObjectW.argtypes=[ctypes.c_void_p,w.LPCWSTR];self.api.CreateJobObjectW.restype=w.HANDLE
        self.api.SetInformationJobObject.argtypes=[w.HANDLE,ctypes.c_int,ctypes.c_void_p,w.DWORD]
        self.api.AssignProcessToJobObject.argtypes=[w.HANDLE,w.HANDLE]
        self.api.TerminateJobObject.argtypes=[w.HANDLE,w.UINT];self.api.CloseHandle.argtypes=[w.HANDLE]
        self.handle=self.api.CreateJobObjectW(None,None)
        limits=Limits();limits.basic.flags=0x2000|0x200|8;limits.basic.active=4;limits.job_memory=memory_mb*1024*1024
        if not self.handle or not self.api.SetInformationJobObject(self.handle,9,ctypes.byref(limits),ctypes.sizeof(limits)):raise OSError(ctypes.get_last_error(),'Cannot constrain engine process')
    def assign(self,process):
        if self.handle and not self.api.AssignProcessToJobObject(self.handle,int(process._handle)):raise OSError(ctypes.get_last_error(),'Cannot own engine process')
    def stop(self):
        if self.handle:self.api.TerminateJobObject(self.handle,0xc000013a)
    def close(self):
        if self.handle:self.api.CloseHandle(self.handle);self.handle=None

def run(command,*,timeout=60,memory_mb=1024,env=None,input_bytes=b'',limit=1024*1024,cwd=None):
    job=EngineJob(memory_mb);process=None;out=bytearray();err=bytearray();overflow=threading.Event();threads=[]
    def drain(pipe,store,maximum):
        while True:
            block=pipe.read(4096)
            if not block:break
            room=maximum-len(store)
            if len(block)>room:overflow.set()
            store.extend(block[:max(0,room)])
    try:
        process=subprocess.Popen([str(x) for x in command],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env,cwd=cwd,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        job.assign(process)
        for pipe,store,maximum in ((process.stdout,out,limit),(process.stderr,err,65536)):
            thread=threading.Thread(target=drain,args=(pipe,store,maximum),daemon=True);thread.start();threads.append(thread)
        process.stdin.write(input_bytes);process.stdin.close();started=time.monotonic()
        while process.poll() is None:
            if overflow.is_set():job.stop();process.kill();break
            if time.monotonic()-started>timeout:job.stop();process.kill();raise TimeoutError('Engine exceeded its processing-time limit')
            time.sleep(.02)
        process.wait()
        for thread in threads:thread.join(5)
        if any(t.is_alive() for t in threads):raise RuntimeError('Engine output stream did not close')
        if overflow.is_set():raise ValueError('Engine output exceeded its byte limit')
        if process.returncode!=0:raise RuntimeError('Engine exited with code '+str(process.returncode)+': '+err.decode('utf-8','replace')[-2048:])
        return bytes(out),bytes(err)
    finally:
        job.stop()
        if process is not None:
            if process.poll() is None:process.kill()
            process.wait()
            for thread in threads:thread.join(5)
            for pipe in (process.stdout,process.stderr):
                if pipe is not None:pipe.close()
        job.close()

def environment(root):
    root=Path(root);system=os.environ.get('SystemRoot',r'C:\Windows')
    env={key:os.environ[key] for key in ('SystemRoot','WINDIR','TEMP','TMP','USERPROFILE','LOCALAPPDATA') if key in os.environ}
    ruby=next((root/'ruby').glob('*/bin'))
    env.update(PATH=str(ruby)+os.pathsep+str(root/'file/usr/bin')+os.pathsep+str(Path(system)/'System32'),GEM_HOME=str(root/'gems'),GEM_PATH=str(root/'gems'),PYTHONNOUSERSITE='1',PYTHONUTF8='1',CUDA_VISIBLE_DEVICES='-1',TF_CPP_MIN_LOG_LEVEL='2',TF_NUM_INTRAOP_THREADS='4',TF_NUM_INTEROP_THREADS='1',OMP_NUM_THREADS='4')
    return env

def validate_zip(data):
    if not data.startswith(b'PK\x03\x04'):return None
    marker=data.find(b'PK\x05\x06')
    if marker<0 or marker+22>len(data):return None
    end=marker+22+int.from_bytes(data[marker+20:marker+22],'little')
    if end>len(data):return None
    payload=data[:end]
    try:
        with zipfile.ZipFile(io.BytesIO(payload)) as archive:
            entries=archive.infolist()
            if not entries or len(entries)>100 or sum(e.file_size for e in entries)>32*1024*1024:return None
            for entry in entries:
                with archive.open(entry) as stream:
                    while stream.read(65536):pass # ZIP reader validates CRC without writing or executing entries.
        return hashlib.sha256(payload).hexdigest(),len(payload)
    except (OSError,ValueError,RuntimeError,zipfile.BadZipFile):return None

MAX_PAYLOAD=32*1024*1024
MAX_PAYLOAD_PIXELS=8_000_000
class ValidationLimit(ValueError):pass
def _png_end(data):
    if not data.startswith(b'\x89PNG\r\n\x1a\n'):return None
    position=8;seen_header=False;seen_pixels=False
    for _ in range(10000):
        if position+12>len(data):return None
        length=int.from_bytes(data[position:position+4],'big');end=position+12+length
        if end>len(data):return None
        kind=data[position+4:position+8]
        if not all(65<=c<=90 or 97<=c<=122 for c in kind):return None
        if zlib.crc32(memoryview(data)[position+4:end-4])!=int.from_bytes(data[end-4:end],'big'):return None
        if kind==b'IHDR':
            if seen_header or position!=8 or length!=13:return None
            seen_header=True
        if not seen_header:return None
        if kind==b'IDAT':seen_pixels=True
        if kind==b'IEND':return end if length==0 and seen_pixels else None
        position=end
    raise ValidationLimit('PNG chunk-validation limit reached')

def _jpeg_end(data):
    # Parse segments and entropy markers; a thumbnail or FF D9 in APP data is not the outer end.
    if not data.startswith(b'\xff\xd8'):return None
    position=2;scan=False
    for _ in range(1000000):
        if scan:
            position=data.find(b'\xff',position)
            if position<0:return None
        if position>=len(data) or data[position]!=255:return None
        marker=position+1
        while marker<len(data) and data[marker]==255:marker+=1
        if marker>=len(data):return None
        code=data[marker];position=marker+1
        if scan and (code==0 or 0xd0<=code<=0xd7):continue
        scan=False
        if code==0xd9:return position
        if code in (0,0xd8) or 0xd0<=code<=0xd7:return None
        if code==1:continue
        if position+2>len(data):return None
        size=int.from_bytes(data[position:position+2],'big')
        if size<2 or position+size>len(data):return None
        position+=size;scan=code==0xda
    raise ValidationLimit('JPEG marker-validation limit reached')

def validate_image(data):
    """Validate a bounded, complete hidden PNG/JPEG, excluding recovered carrier padding."""
    if len(data)>MAX_PAYLOAD:raise ValidationLimit('Recovered image exceeds the byte limit')
    kind='png' if data.startswith(b'\x89PNG\r\n\x1a\n') else 'jpeg' if data.startswith(b'\xff\xd8\xff') else None
    end=_png_end(data) if kind=='png' else _jpeg_end(data) if kind=='jpeg' else None
    if end is None:return None
    payload=bytes(data[:end])
    try:
        from PIL import Image
        with Image.open(io.BytesIO(payload)) as image:
            if image.width<1 or image.height<1:return None
            if image.width*image.height>MAX_PAYLOAD_PIXELS:raise ValidationLimit('Recovered image exceeds the pixel limit')
            if image.format!=('PNG' if kind=='png' else 'JPEG'):return None
            image.verify()
        with Image.open(io.BytesIO(payload)) as image:image.load()
        return hashlib.sha256(payload).hexdigest(),len(payload),kind
    except ValidationLimit:raise
    except Image.DecompressionBombError as error:raise ValidationLimit('Recovered image exceeds the decoder pixel limit') from error
    except (OSError,ValueError,RuntimeError):return None

def validate_payload(data):
    bounded=data[:MAX_PAYLOAD];archive=validate_zip(bounded)
    if archive:return (*archive,'zip')
    image=validate_image(bounded)
    if image:return image
    if len(data)>MAX_PAYLOAD and data.startswith((b'PK\x03\x04',b'\x89PNG\r\n\x1a\n',b'\xff\xd8\xff')):raise ValidationLimit('Payload extends beyond the validation byte limit')
    return None

def _carrier_layout(path,kind):
    if path.stat().st_size>256*1024*1024:raise ValidationLimit('Carrier exceeds the byte limit')
    data=path.read_bytes()
    if kind=='jpeg':
        end=_jpeg_end(data)
        if end is None:return None,[]
        from PIL.JpegImagePlugin import JpegImageFile
        try:
            # The base decoder preserves mpoffset; the MPO factory consumes it.
            with JpegImageFile(io.BytesIO(data)) as image:
                if 'mp' not in image.info:return end,[]
                index=image._getmp();base=image.info.get('mpoffset');entries=index.get(0xb002,[])
                if not isinstance(base,int) or not 1<=len(entries)<=256 or index.get(0xb001)!=len(entries):raise ValueError('Invalid MPF index')
                members=[]
                for entry in entries:
                    offset=entry['DataOffset'];size=entry['Size'];start=0 if offset==0 else base+offset
                    if not isinstance(offset,int) or not isinstance(size,int) or start<0 or size<4 or start+size>len(data):raise ValueError('Invalid MPF member range')
                    members.append((start,start+size))
                ordered=sorted(members)
                if ordered[0]!=(0,end) or any(left[1]>right[0] for left,right in zip(ordered,ordered[1:])):raise ValueError('Overlapping MPF members')
                if any(_jpeg_end(data[start:stop])!=stop-start for start,stop in members):raise ValueError('Incomplete MPF member')
                return end,members
        except (OSError,ValueError,TypeError,KeyError,AttributeError,SyntaxError) as error:raise ValidationLimit('MPF secondary-image metadata could not be fully validated') from error
    if kind=='png':return _png_end(data),[]
    if kind=='bmp' and data.startswith(b'BM') and len(data)>=54:
        size=int.from_bytes(data[2:6],'little')
        return (size if 54<=size<=len(data) else None),[]
    return None,[]

def _validated(finding,validated):
    digest,size,kind=validated
    explanation='Recovered ZIP structure and entry CRCs validated' if kind=='zip' else 'Recovered PNG chunk CRCs and image decoding validated' if kind=='png' else 'Recovered complete JPEG structure and image decoding validated'
    finding.update(kind='validated_payload',strength='strong',correlation='payload:'+digest,payloadSha256=digest,payloadSize=size,payloadType=kind,explanation=explanation+'; no recovered files were written or executed.')
    return finding

def zsteg(path,kind,root):
    env=environment(root);ruby=next((root/'ruby').glob('*/bin/ruby.exe'));cli=root/'gems/gems/zsteg-0.2.14/bin/zsteg'
    output,errors=run([ruby,cli,'--all','--no-color',path],env=env,timeout=120,cwd=path.parent)
    raw=output.decode('utf-8','replace');findings=[];recipes=[]
    for line in raw.splitlines():
        match=re.match(r'^(\S+)\s+\.\.\s+(.*)$',line)
        if not match or not match.group(2).strip():continue
        recipe,detail=match.groups()
        findings.append(dict(kind='candidate',strength='weak',method=recipe,correlation='zsteg:'+recipe,explanation=detail[:512]))
        recoverable='zlib:' in detail or 'file:' in detail and any(signature in detail.lower() for signature in ('zip archive','png image','jpeg image'))
        if recoverable and recipe.startswith('b') and recipe not in recipes:recipes.append(recipe)
        if len(findings)>=500:break
    recovery_limited=len(recipes)>8
    for recipe in recipes[:8]:
        # Windows Ruby otherwise translates binary LF bytes to CRLF on stdout.
        try:
            data,_=run([ruby,'-e','$stdout.binmode; $0=ARGV.shift; load $0',cli,'--no-color','--extract',recipe,path],env=env,timeout=30,limit=MAX_PAYLOAD,cwd=path.parent)
            validated=validate_payload(data)
            if validated:findings.append(_validated(dict(method=recipe),validated))
        except (OSError,ValueError,RuntimeError,TimeoutError):recovery_limited=True
    clipped=len(raw)>65536 or len(findings)>=500 or recovery_limited
    return dict(state='partial' if clipped else 'completed',version='0.2.14',configuration='--all --no-color; upstream byte-check limit 256; bounded ZIP/PNG/JPEG validation',findings=findings,raw=raw[:65536],stderr=errors.decode('utf-8','replace')[:8192],reason='Output or payload-recovery limit reached; coverage is incomplete' if clipped else '')

def binwalk(path,kind,root):
    with tempfile.TemporaryDirectory(prefix='FHBinwalk-') as directory:
        log=Path(directory)/'results.json'
        output,errors=run([root/'binwalk/binwalk.exe','--search-all','--threads','1','--log',log,path],env=environment(root),timeout=60,cwd=directory)
        if not log.exists() or log.stat().st_size>1024*1024:raise ValueError('Missing or oversized Binwalk results')
        raw=log.read_text(encoding='utf-8');events=json.loads(raw);findings=[];clipped=len(raw)>65536;carrier_end=None;members=[];boundary_checked=False
        for event in events:
            analysis=event.get('Analysis') or {}
            for item in analysis.get('file_map',[]):
                offset=item.get('offset');size=item.get('size') or 0;name=str(item.get('name',''))
                if not isinstance(offset,int) or offset<0 or offset>=path.stat().st_size:continue
                finding=dict(kind='recognized_structure' if offset==0 or name in ('png','jpeg','bmp','zlib') else 'candidate',strength='informational' if offset==0 else 'weak',method=name,offset=offset,length=size,correlation='bytes:'+str(offset)+':'+name,explanation=str(item.get('description',name))[:512])
                if name in ('png','jpeg') and offset>0 and not boundary_checked:
                    try:carrier_end,members=_carrier_layout(path,kind)
                    except ValidationLimit:clipped=True
                    boundary_checked=True
                if name=='zip' or name in ('png','jpeg') and offset>0 and carrier_end is not None and offset>=carrier_end and not any(start<=offset<stop for start,stop in members):
                    with path.open('rb') as source:source.seek(offset);data=source.read(MAX_PAYLOAD+1)
                    try:
                        valid=validate_payload(data)
                        if valid:_validated(finding,valid)
                    except ValidationLimit:clipped=True
                findings.append(finding)
                if len(findings)>=500:clipped=True;break
        return dict(state='partial' if clipped else 'completed',reason='Finding, output or payload-validation limit reached; coverage is incomplete' if clipped else '',version='3.1.1',configuration='signature scan; no extraction or recursive processing',findings=findings,raw=raw[:65536],stderr=errors.decode('utf-8','replace')[:8192])

def aletheia(path,kind,root):
    request=dict(path=str(path),format=kind,root=str(root))
    output,errors=run([root/'python/python/python.exe','-I',root/'scripts/fh_model.py'],input_bytes=(json.dumps(request)+'\n').encode(),env=environment(root),timeout=300,memory_mb=4096,cwd=path.parent,limit=250000)
    result=json.loads(output);result['stderr']=errors.decode('utf-8','replace')[-8192:];return result
def detectors(root):
    root=Path(root)
    return {'aletheia':lambda path,kind:aletheia(path,kind,root),'zsteg':lambda path,kind:zsteg(path,kind,root),'binwalk':lambda path,kind:binwalk(path,kind,root)}
