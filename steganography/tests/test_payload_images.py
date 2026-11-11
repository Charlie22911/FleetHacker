import hashlib,io,json,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from PIL import Image
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import fh_engines

def image_bytes(kind):
    stream=io.BytesIO();Image.new('RGB',(32,24),(30,80,120)).save(stream,format=kind);return stream.getvalue()

class ImagePayloads(unittest.TestCase):
    def validate(self,data):return getattr(fh_engines,'validate_image',lambda _:None)(data)
    def test_complete_png_recovery_excludes_carrier_padding(self):
        payload=image_bytes('PNG')
        self.assertEqual(self.validate(payload+b'carrier bit padding'),(hashlib.sha256(payload).hexdigest(),len(payload),'png'))
    def test_complete_jpeg_recovery_excludes_carrier_padding(self):
        payload=image_bytes('JPEG')
        self.assertEqual(self.validate(payload+b'carrier bit padding'),(hashlib.sha256(payload).hexdigest(),len(payload),'jpeg'))
    def test_bad_png_crc_is_not_verified(self):
        data=bytearray(image_bytes('PNG'));data[-1]^=1
        self.assertIsNone(self.validate(data))
    def test_truncated_jpeg_is_not_verified(self):self.assertIsNone(self.validate(image_bytes('JPEG')[:-4]))
    def test_jpeg_marker_inside_app_data_does_not_truncate_recovery(self):
        data=image_bytes('JPEG');segment=b'metadata\xff\xd9still metadata'
        payload=data[:2]+b'\xff\xe1'+(len(segment)+2).to_bytes(2,'big')+segment+data[2:]
        self.assertEqual(self.validate(payload),(hashlib.sha256(payload).hexdigest(),len(payload),'jpeg'))
    def test_signature_without_a_decodable_image_is_not_verified(self):
        self.assertIsNone(self.validate(b'\xff\xd8\xff\xd9'))
        self.assertIsNone(self.validate(b'\x89PNG\r\n\x1a\n'+b'\0'*50))
    def test_zlib_hint_still_recovers_the_complete_hidden_png(self):
        payload=image_bytes('PNG')
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'ruby/test/bin').mkdir(parents=True);(root/'ruby/test/bin/ruby.exe').touch()
            path=root/'input.png';path.write_bytes(image_bytes('PNG'))
            def run(command,**kwargs):
                return (payload+b'padding',b'') if '--extract' in command else (b'b1,rgb,lsb,xy .. zlib: compressed data, offset=41\n',b'')
            with patch.object(fh_engines,'run',run),patch.object(fh_engines,'environment',lambda _:{}):result=fh_engines.zsteg(path,'png',root)
            self.assertTrue(any(f.get('payloadSha256')==hashlib.sha256(payload).hexdigest() for f in result['findings']))
    def binwalk(self,data,offset,name='png'):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'carrier.jpg';path.write_bytes(data)
            def run(command,**kwargs):
                log=Path(command[command.index('--log')+1])
                log.write_text(json.dumps([{'Analysis':{'file_map':[dict(offset=offset,name=name,size=80,description='Image')]}}]))
                return b'',b''
            with patch.object(fh_engines,'run',run),patch.object(fh_engines,'environment',lambda _:{}):return fh_engines.binwalk(path,'jpeg',Path(directory))
    def test_appended_image_is_verified_by_structure_and_decode(self):
        carrier=image_bytes('JPEG');payload=image_bytes('PNG');result=self.binwalk(carrier+payload,len(carrier))
        self.assertTrue(any(f.get('payloadSha256')==hashlib.sha256(payload).hexdigest() and f['kind']=='validated_payload' for f in result['findings']))
    def test_image_inside_jpeg_metadata_is_not_a_hidden_payload(self):
        carrier=image_bytes('JPEG');payload=image_bytes('PNG');segment=b'metadata'+payload
        image=carrier[:2]+b'\xff\xe1'+(len(segment)+2).to_bytes(2,'big')+segment+carrier[2:]
        result=self.binwalk(image,2+4+len(b'metadata'))
        self.assertFalse(any(f['kind']=='validated_payload' for f in result['findings']))
    def mpo(self):
        stream=io.BytesIO();Image.new('RGB',(32,24),(30,80,120)).save(stream,format='MPO',save_all=True,append_images=[Image.new('RGB',(24,24),(70,40,110))])
        return stream.getvalue()
    def test_referenced_mpo_secondary_is_not_hidden(self):
        data=self.mpo();offset=data.find(b'\xff\xd9')+2
        result=self.binwalk(data,offset,'jpeg')
        self.assertFalse(any(f['kind']=='validated_payload' for f in result['findings']))
    def test_unreferenced_payload_after_mpo_still_qualifies(self):
        data=self.mpo();payload=image_bytes('PNG');result=self.binwalk(data+payload,len(data))
        self.assertTrue(any(f.get('payloadSha256')==hashlib.sha256(payload).hexdigest() for f in result['findings']))
    def test_image_pixel_limit_reports_partial_coverage(self):
        stream=io.BytesIO();Image.new('RGB',(3000,2700),(1,2,3)).save(stream,format='JPEG');carrier=image_bytes('JPEG')
        result=self.binwalk(carrier+stream.getvalue(),len(carrier),'jpeg')
        self.assertEqual(result['state'],'partial')
        self.assertFalse(any(f['kind']=='validated_payload' for f in result['findings']))
    def test_image_byte_limit_reports_partial_coverage(self):
        carrier=image_bytes('JPEG');payload=image_bytes('PNG')
        with patch.object(fh_engines,'MAX_PAYLOAD',64):result=self.binwalk(carrier+payload,len(carrier))
        self.assertEqual(result['state'],'partial')
        self.assertFalse(any(f['kind']=='validated_payload' for f in result['findings']))
    def test_hdr_style_mpf_preserves_gainmap_and_finds_unreferenced_image(self):
        data=bytearray(self.mpo());base=data.index(b'MPF\0')+4
        byteorder='little' if data[base:base+2]==b'II' else 'big'
        ifd=base+int.from_bytes(data[base+4:base+8],byteorder)
        count=int.from_bytes(data[ifd:ifd+2],byteorder);table=None
        for i in range(count):
            entry=ifd+2+i*12
            if int.from_bytes(data[entry:entry+2],byteorder)==0xb002:table=base+int.from_bytes(data[entry+8:entry+12],byteorder)
        self.assertIsNotNone(table)
        metadata=b'http://ns.adobe.com/xap/1.0/\0hdrgm:Version="1.0"'
        segment=b'\xff\xe1'+(len(metadata)+2).to_bytes(2,'big')+metadata
        size=int.from_bytes(data[table+4:table+8],byteorder)
        data[table+4:table+8]=(size+len(segment)).to_bytes(4,byteorder)
        hdr=bytes(data[:2]+segment+data[2:]);offset=hdr.find(b'\xff\xd9')+2
        self.assertFalse(any(f['kind']=='validated_payload' for f in self.binwalk(hdr,offset,'jpeg')['findings']))
        payload=image_bytes('PNG')
        self.assertTrue(any(f.get('payloadSha256')==hashlib.sha256(payload).hexdigest() for f in self.binwalk(hdr+payload,len(hdr))['findings']))
if __name__=='__main__':unittest.main()
