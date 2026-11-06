using System;
using System.IO;
using System.Text;
namespace MediaReviewer {
    internal static class ImageHeader {
        const long HeaderLimit=16L*1024*1024;
        static bool At(byte[] b,int p,string value){return p+value.Length<=b.Length && Encoding.ASCII.GetString(b,p,value.Length)==value;}
        internal static string Sniff(byte[] header) {
            if(header.Length>=4 && header[0]==0 && header[1]==0 && header[2]==1 && (header[3]==0xBA || header[3]==0xB3))return "mpeg";
            if(header.Length>376 && header[0]==0x47 && header[188]==0x47 && header[376]==0x47)return "ts";
            if(header.Length>388 && header[4]==0x47 && header[196]==0x47 && header[388]==0x47)return "m2ts";
            try {
                string type=Detect(header,"");
                if(type=="svg" && !Encoding.UTF8.GetString(header).TrimStart('\ufeff',' ','\r','\n','\t').StartsWith("<svg",StringComparison.Ordinal))return null;
                return type;
            }catch(InvalidDataException){return null;}
        }
        internal static string Detect(byte[] b,string extension){
            if(b.Length==0)throw new InvalidDataException("Media file is empty; selected extension ."+extension+". Fingerprint retained.");
            if(b.Length>=2 && b[0]==255 && b[1]==216)return "jpeg";
            if(b.Length>=8 && b[0]==137 && At(b,1,"PNG\r\n\x1a\n"))return "png";
            if(Formats.IsRaw(extension))return extension;
            if(At(b,0,"GIF87a") || At(b,0,"GIF89a"))return "gif";
            if(b.Length>=9 && At(b,0,"FLV") && b[3]==1 && (b[4]&0xfa)==0 &&
                b[5]==0 && b[6]==0 && b[7]==0 && b[8]>=9)return "flv";
            if(At(b,0,"RIFF") && At(b,8,"WEBP"))return "webp";
            if(At(b,0,"BM"))return "bmp";
            if(b.Length>=4 && b[0]==0 && b[1]==0 && b[2]==1 && b[3]==0)return "ico";
            if(b.Length>=4 && ((b[0]==73 && b[1]==73 && (b[2]==42 || b[2]==43) && b[3]==0) || (b[0]==77 && b[1]==77 && b[2]==0 && (b[3]==42 || b[3]==43))))return "tiff";
            if(At(b,4,"ftyp")){
                uint size=((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];int start=8,end=size==0 ? b.Length : (int)Math.Min(size,(uint)b.Length);
                if(size==1){if(b.Length<24)throw new InvalidDataException("Truncated extended media brand header");start=16;ulong extended=0;for(int i=8;i<16;i++)extended=(extended<<8)|b[i];end=(int)Math.Min(extended,(ulong)b.Length);}
                if(end<start+8)throw new InvalidDataException("Invalid media brand header length");
                for(int at=start;at+4<=end;at+=4)if(at!=start+4 && (At(b,at,"avif") || At(b,at,"avis")))return "avif";
                for(int at=start;at+4<=end;at+=4)if(at!=start+4 && (At(b,at,"heic") || At(b,at,"heix") || At(b,at,"hevc") || At(b,at,"hevx") || At(b,at,"mif1") || At(b,at,"msf1")))return "heif";
                return "mp4";
            }
            if(At(b,0,"RIFF") && At(b,8,"AVI "))return "avi";
            if(b.Length>=4 && b[0]==0x1a && b[1]==0x45 && b[2]==0xdf && b[3]==0xa3)return "mkv";
            if(b.Length>=4 && b[0]==0x30 && b[1]==0x26 && b[2]==0xb2 && b[3]==0x75)return "wmv";
            string xml=Encoding.UTF8.GetString(b).TrimStart('\ufeff',' ','\r','\n','\t');
            if(extension=="svg" || xml.StartsWith("<svg",StringComparison.Ordinal) || xml.StartsWith("<?xml",StringComparison.Ordinal))return "svg";
            if(Formats.IsVideo(extension))return extension;
            string reason=At(b,0,"%PDF-") ? "Header identifies a PDF document" :
                xml.StartsWith("<!doctype html",StringComparison.OrdinalIgnoreCase) || xml.StartsWith("<html",StringComparison.OrdinalIgnoreCase) ? "Header identifies an HTML document" :
                b.Length<8 ? "Media header is too short" : "Media header is not recognized";
            throw new InvalidDataException(reason+"; selected extension ."+extension+"; examined "+b.Length+" header bytes. The file may be incomplete, damaged or have an incorrect extension. Fingerprint retained.");
        }
        static int Byte(Stream source){int value=source.ReadByte();if(value<0)throw new InvalidDataException("Truncated JPEG header");return value;}
        static uint Little32(byte[] b,int p){return (uint)b[p]|((uint)b[p+1]<<8)|((uint)b[p+2]<<16)|((uint)b[p+3]<<24);}
        internal static void SavedWebpDimensions(byte[] b,out int width,out int height){
            width=height=0;
            if(b==null || b.Length<20 || b.Length>32768 || !At(b,0,"RIFF") || !At(b,8,"WEBP"))throw new InvalidDataException("Invalid or oversized saved WebP thumbnail header (32 KiB limit)");
            uint declared=Little32(b,4);if(declared!=(uint)b.Length-8)throw new InvalidDataException("Truncated or inconsistent saved WebP container length");
            bool extended=false,frame=false;int canvasWidth=0,canvasHeight=0;
            for(int at=12;at+8<=b.Length;){
                uint size=Little32(b,at+4);int p=at+8,w=0,h=0;if((long)p+size>b.Length)throw new InvalidDataException("Truncated saved WebP chunk");
                if(At(b,at,"VP8X")){
                    if(extended || frame)throw new InvalidDataException("Misplaced saved WebP extended header");extended=true;
                    if(size<10)throw new InvalidDataException("Truncated saved WebP extended header");
                    if((b[p]&2)!=0)throw new InvalidDataException("Animated WebP is not a saved still thumbnail");
                    w=canvasWidth=1+b[p+4]+(b[p+5]<<8)+(b[p+6]<<16);h=canvasHeight=1+b[p+7]+(b[p+8]<<8)+(b[p+9]<<16);
                }else if(At(b,at,"VP8L")){
                    if(size<5 || b[p]!=0x2f)throw new InvalidDataException("Invalid saved lossless WebP header");uint bits=Little32(b,p+1);if((bits>>29)!=0)throw new InvalidDataException("Unsupported saved WebP version");w=1+(int)(bits&0x3fff);h=1+(int)((bits>>14)&0x3fff);
                }else if(At(b,at,"VP8 ")){
                    if(size<10 || (b[p]&1)!=0 || b[p+3]!=0x9d || b[p+4]!=1 || b[p+5]!=0x2a)throw new InvalidDataException("Invalid saved lossy WebP frame header");w=(b[p+6]|(b[p+7]<<8))&0x3fff;h=(b[p+8]|(b[p+9]<<8))&0x3fff;
                }
                if(w!=0 || h!=0){if(w<1 || h<1 || w>256 || h>256)throw new InvalidDataException("Saved WebP thumbnail dimensions exceed the 256 pixel limit");if(!At(b,at,"VP8X")){if(frame || (extended && (w!=canvasWidth || h!=canvasHeight)))throw new InvalidDataException("Inconsistent saved WebP frame dimensions");frame=true;width=w;height=h;}}
                long next=(long)p+size+(size&1);if(next>b.Length)throw new InvalidDataException("Truncated saved WebP chunk padding");at=(int)next;
            }
            if(!frame)throw new InvalidDataException("Saved WebP image frame is missing");
        }
        internal static void Dimensions(Stream source,string format,out int width,out int height){
            source.Position=0;width=height=0;
            if(format=="png"){
                byte[] b=new byte[24];int read=0,n;while(read<b.Length && (n=source.Read(b,read,b.Length-read))>0)read+=n;
                if(read!=b.Length || Detect(b,"png")!="png" || !At(b,12,"IHDR"))throw new InvalidDataException("Invalid or truncated PNG dimensions header");
                width=(b[16]<<24)|(b[17]<<16)|(b[18]<<8)|b[19];height=(b[20]<<24)|(b[21]<<16)|(b[22]<<8)|b[23];
            }else if(format=="jpeg"){
                if(Byte(source)!=255 || Byte(source)!=216)throw new InvalidDataException("Invalid JPEG signature");
                while(source.Position<HeaderLimit){
                    if(Byte(source)!=255)throw new InvalidDataException("Invalid JPEG marker before dimensions");int marker;do{marker=Byte(source);if(source.Position>=HeaderLimit)throw new InvalidDataException("JPEG metadata exceeds the 16 MiB header search limit");}while(marker==255);
                    if(marker==217 || marker==218)throw new InvalidDataException("JPEG dimensions are missing before image data");
                    if(marker==1 || (marker>=208 && marker<=215))continue;
                    int length=(Byte(source)<<8)|Byte(source);if(length<2)throw new InvalidDataException("Invalid JPEG segment length");
                    if(source.Position+length-2>source.Length)throw new InvalidDataException("Truncated JPEG metadata segment");
                    if(marker>=192 && marker<=207 && marker!=196 && marker!=200 && marker!=204){
                        if(length<7)throw new InvalidDataException("Invalid JPEG dimensions segment");Byte(source);height=(Byte(source)<<8)|Byte(source);width=(Byte(source)<<8)|Byte(source);break;
                    }
                    if(source.Position+length-2>HeaderLimit)throw new InvalidDataException("JPEG metadata exceeds the 16 MiB header search limit");source.Position+=length-2;
                }
                if(width==0 && height==0)throw new InvalidDataException("JPEG dimensions unavailable within the 16 MiB header search limit");
            }else throw new InvalidDataException("Expected a validated JPEG or PNG signature");
            if(width<1 || height<1)throw new InvalidDataException("Invalid "+format+" dimensions: "+width+"x"+height);
        }
        internal static void SourcePixels(string format,int width,int height){
            if(width<1 || height<1 || width>65535 || height>65535 || (long)width*height>200000000)throw new InvalidDataException(format+" source dimensions "+width+"x"+height+" exceed the 200 megapixel / 65535 per-axis preview guardrail");
        }
    }
}
