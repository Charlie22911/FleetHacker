import hashlib,importlib.util,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
class AnalysisTests(unittest.TestCase):
    def test_analysis_pipeline_exists(self):
        self.assertIsNotNone(importlib.util.find_spec('fh_analysis'),'The companion must have a real bounded image-analysis pipeline')
    def module(self):
        import fh_analysis
        return fh_analysis
    def image(self,folder,name='input.png'):
        from PIL import Image
        path=Path(folder)/name;Image.new('RGB',(32,32),(30,80,120)).save(path,format='PNG');return path
    def test_uses_actual_format_and_preserves_original(self):
        with tempfile.TemporaryDirectory() as folder:
            path=self.image(folder,'misnamed.jpg');before=hashlib.sha256(path.read_bytes()).hexdigest();seen=[]
            def detector(snapshot,kind):
                seen.append((str(snapshot)!=str(path),kind,hashlib.sha256(snapshot.read_bytes()).hexdigest()))
                return dict(state='completed',findings=[])
            result=self.module().analyze(path,detectors={'aletheia':detector,'zsteg':detector,'binwalk':detector})
            self.assertEqual(result['format'],'png');self.assertEqual(before,result['sha256']);self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(),before)
            self.assertTrue(all(private and kind=='png' and digest==before for private,kind,digest in seen));self.assertEqual(result['status'],'completed')
    def test_one_failure_does_not_stop_other_engines(self):
        with tempfile.TemporaryDirectory() as folder:
            def fail(*args):raise TimeoutError('engine timeout')
            def complete(*args):return dict(state='completed',findings=[])
            result=self.module().analyze(self.image(folder),detectors={'aletheia':fail,'zsteg':complete,'binwalk':complete})
            self.assertEqual([r['state'] for r in result['engines']],['timed_out','completed','completed']);self.assertEqual(result['status'],'partial');self.assertIsNone(result['suspicionIndex'])
    def test_duplicate_payload_is_not_independent_evidence(self):
        findings=[dict(kind='validated_payload',strength='strong',correlation='sha256:abc',explanation='Validated generated archive')]*2
        scored=self.module().score(findings)
        self.assertEqual(scored['index'],80);self.assertEqual(len(scored['contributions']),1)
    def test_unsupported_format_is_explicit(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'not.png';path.write_bytes(b'GIF89a'+b'\0'*100)
            result=self.module().analyze(path,detectors={})
            self.assertEqual(result['status'],'unsupported');self.assertIsNone(result['suspicionIndex'])
    def test_input_limit_and_detector_output_are_bounded(self):
        with tempfile.TemporaryDirectory() as folder:
            path=self.image(folder)
            limited=self.module().analyze(path,detectors={},max_bytes=10)
            self.assertEqual(limited['status'],'limited')
            def oversized(*args):return dict(state='completed',findings=[dict(kind='candidate',strength='weak',correlation=str(i),explanation='x'*100) for i in range(2000)])
            # No unbounded engine result can be treated as successful coverage.
            result=self.module().analyze(path,detectors={'zsteg':oversized})
            self.assertEqual(result['engines'][1]['state'],'failed');self.assertLess(len(json.dumps(result)),1024*1024)
if __name__=='__main__':unittest.main()
