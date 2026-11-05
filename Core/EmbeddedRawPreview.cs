using System;
using System.IO;
namespace MediaReviewer {
    internal static partial class MediaDecode {
        static byte[] EmbeddedJpeg(Stream source){
            byte[] block=new byte[65536],best=null;long scanned=0;int previous=-1,phase=0,skip=0,lengthHigh=0;bool entropy=false,afterScan=false;MemoryStream candidate=null;
            try{int n;while(scanned<128L*1024*1024 && (n=source.Read(block,0,(int)Math.Min(block.Length,128L*1024*1024-scanned)))>0){scanned+=n;
                for(int at=0;at<n;at++){int value=block[at];if(candidate==null){if(previous==255 && value==216){candidate=new MemoryStream();candidate.WriteByte(255);candidate.WriteByte(216);phase=0;entropy=false;skip=0;}previous=value;continue;}
                    candidate.WriteByte((byte)value);if(candidate.Length>8*1024*1024){candidate.Dispose();candidate=null;previous=value;continue;}
                    bool end=false;
                    if(skip>0){if(--skip==0){entropy=afterScan;phase=0;}}
                    else if(phase==2){lengthHigh=value;phase=3;}
                    else if(phase==3){skip=(lengthHigh<<8)+value-2;if(skip<0){candidate.Dispose();candidate=null;}else if(skip==0){entropy=afterScan;phase=0;}}
                    else if(phase==1){if(value==217)end=true;else if(value==255){}else if(entropy && (value==0 || (value>=208 && value<=215))){phase=0;}else if(value==1 || (value>=208 && value<=215)){phase=0;}else{afterScan=value==218;entropy=false;phase=2;}}
                    else if(value==255)phase=1;
                    else if(!entropy){candidate.Dispose();candidate=null;}
                    if(end){byte[] jpeg=candidate.ToArray();candidate.Dispose();candidate=null;try{int w,h;Preview.Dimensions(jpeg,jpeg.Length,out w,out h);if(best==null || jpeg.Length>best.Length)best=jpeg;if(Math.Max(w,h)>=256)return jpeg;}catch(InvalidDataException){};}
                    previous=value;
                }
            }}finally{if(candidate!=null)candidate.Dispose();}
            if(best==null)throw new InvalidDataException("RAW embedded JPEG unavailable within the first 128 MiB (8 MiB candidate limit); full RAW decoding is not available");return best;
        }
    }
}
