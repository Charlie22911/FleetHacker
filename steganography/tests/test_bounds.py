import json,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import fh_engines
class Bounds(unittest.TestCase):
    def test_binwalk_truncation_is_partial(self):
        with tempfile.TemporaryDirectory() as directory:
            image=Path(directory)/'image';image.write_bytes(b'x'*4096)
            def run(command,**kwargs):
                log=Path(command[command.index('--log')+1]);log.write_text(json.dumps([{'Analysis':{'file_map':[dict(offset=i+1,name='test',description='candidate') for i in range(501)]}}]));return b'',b''
            with patch.object(fh_engines,'run',run),patch.object(fh_engines,'environment',lambda _:{}):result=fh_engines.binwalk(image,'png',Path(directory))
            self.assertEqual(len(result['findings']),500);self.assertEqual(result['state'],'partial');self.assertIn('limit',result['reason'])
    def test_zip_crc_failure_is_not_validated(self):
        import io,zipfile
        data=io.BytesIO()
        with zipfile.ZipFile(data,'w') as archive:archive.writestr('data',b'original')
        damaged=data.getvalue().replace(b'original',b'changed!')
        self.assertIsNone(fh_engines.validate_zip(damaged))
if __name__=='__main__':unittest.main()
