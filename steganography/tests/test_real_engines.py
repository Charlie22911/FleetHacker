import hashlib,json,os,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from fh_engines import zsteg,binwalk
class Engines(unittest.TestCase):
    root=Path(os.environ['FH_STEGO_RUNTIME']).resolve();fixtures=Path(os.environ['FH_STEGO_FIXTURES']).resolve()
    def test_original_zsteg_recovers_generated_payload(self):
        result=zsteg(self.fixtures/'lsb.png','png',self.root)
        expected=hashlib.sha256((self.fixtures/'payload.zip').read_bytes()).hexdigest()
        self.assertTrue(any(f.get('payloadSha256')==expected for f in result['findings']),json.dumps(result)[:3000])
    def test_original_binwalk_validates_appended_payload(self):
        result=binwalk(self.fixtures/'appended.jpg','jpeg',self.root)
        expected=hashlib.sha256((self.fixtures/'payload.zip').read_bytes()).hexdigest()
        self.assertTrue(any(f.get('payloadSha256')==expected for f in result['findings']),json.dumps(result)[:3000])
    def test_clean_format_headers_are_informational(self):
        result=binwalk(self.fixtures/'clean.png','png',self.root)
        self.assertFalse(any(f['kind']=='validated_payload' for f in result['findings']))
if __name__=='__main__':unittest.main()
