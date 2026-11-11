using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaReviewer {
    internal static partial class MediaDecode {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ReadBytes(IntPtr state,IntPtr buffer,int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate long SeekBytes(IntPtr state,long offset,int origin);
        [StructLayout(LayoutKind.Sequential)] struct Input { public ReadBytes Read;public SeekBytes Seek;public IntPtr State; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr AllocatePixels(int width,int height,int stride,int capacity);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DecodeFrame(string format,ref Input input,AllocatePixels allocate,int side,int maxPixels,out int stride,
            out int width,out int height,out int sourceWidth,out int sourceHeight,out int rotation,int percent);

        internal static PreviewResult CreateSavedWebp(byte[] bytes) {
            int width,height;ImageHeader.SavedWebpDimensions(bytes,out width,out height);
            using(var source=new MemoryStream(bytes,false))return Linked(source,"webp_pipe",256,32768);
        }

        internal static PreviewResult CreateScoped(string path,long size,long ticks,string identity,int side,int limit,string root) {
            return DecodeScoped(path,size,ticks,identity,side,limit,root,false)[0];
        }

        internal static PreviewResult[] CreateMotion(string path,long size,long ticks,string identity,string root,int side=256,int limit=32768){
            return DecodeScoped(path,size,ticks,identity,side,limit,root,true);
        }

        static PreviewResult[] DecodeScoped(string path,long size,long ticks,string identity,int side,int limit,string root,bool motion){
            if(!((side==256 && limit==32768) || (side>=1 && side<=InspectionPlan.MaxSide && limit==InspectionPlan.EncodedLimit)))
                throw new InvalidDataException("Invalid bounded preview request.");
            string extension=Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            using(var source=new FileStream(Paths.Io(path),FileMode.Open,FileAccess.Read,FileShare.Read,65536)) {
                FileStamp before=FileAccessNative.Stamp(source);
                if(size>=0 && (before.Size!=size || before.ModifiedTicks!=ticks || (!String.IsNullOrEmpty(identity) && before.Identity!=identity)))
                    throw new InvalidDataException("Source changed after fingerprinting. Rescan before retrying the preview.");
                if(!String.IsNullOrEmpty(root))Paths.OpenedSourceWithinRoot(source,root);
                bool recognized=Formats.Supported(extension);
                byte[] header;using(var reader=new BinaryReader(source,System.Text.Encoding.UTF8,true))header=reader.ReadBytes(recognized ? 64 : 512);
                string type=recognized ? ImageHeader.Detect(header,extension) : ImageHeader.Sniff(header);
                if(type==null)throw new InvalidDataException("The renamed source has no supported media signature.");
                source.Position=0;
                string demuxer=type=="mpeg" && header.Length>=4 && header[0]==0 && header[1]==0 && header[2]==1 && header[3]==0xB3 ? "mpegvideo" : Demuxer(type);
                long inputLimit=Formats.IsVideo(type) || type=="heif" || type=="heic" ? 64L<<30 : 128L<<20;
                if(source.Length>inputLimit)throw new InvalidDataException("The input exceeds the preview limit. The fingerprint is retained.");
                if(motion){
                    if(!Formats.IsPlayable(type))throw new InvalidDataException("The recorded motion source has no supported video or GIF signature.");
                    if(type=="gif")demuxer="gif";
                    int[] positions=type=="gif" ? new[]{0,33,66} : new[]{0,20,40,60,80};
                    var frames=new System.Collections.Generic.List<PreviewResult>(positions.Length);
                    foreach(int position in positions){
                        source.Position=0;
                        var frame=Linked(source,demuxer,side,limit,position);
                        if(frame==null)continue;
                        if(frames.Any(previous=>previous.Bytes.SequenceEqual(frame.Bytes)))Array.Clear(frame.Bytes,0,frame.Bytes.Length);
                        else frames.Add(frame);
                    }
                    if(!before.Same(FileAccessNative.Stamp(source)))throw new InvalidDataException("Source changed during frame sampling.");
                    return frames.ToArray();
                }
                PreviewResult result;
                if(demuxer!=null)result=Linked(source,demuxer,side,limit);
                else if(Formats.IsRaw(type)) {
                    using(var embedded=new MemoryStream(EmbeddedJpeg(source),false))result=WindowsImage(embedded,"jpeg",side,limit);
                    result.Decoder="RAW embedded JPEG / Windows Imaging Component";
                } else if(type=="svg")throw new InvalidDataException("SVG rendering is unavailable in this build. The fingerprint is retained.");
                else result=WindowsImage(source,type,side,limit);
                if(!before.Same(FileAccessNative.Stamp(source)))throw new InvalidDataException("Source changed during preview generation.");
                return new[]{result};
            }
        }

        static string Demuxer(string type) {
            switch(type) {
                case "avif":case "heif":case "heic":case "mp4":case "m4v":case "mov":return "mov";
                case "mkv":case "webm":return "matroska";
                case "wmv":return "asf";
                case "avi":return "avi";
                case "flv":return "flv";
                case "mpeg":case "mpg":case "mpe":return "mpeg";
                case "ts":case "m2ts":case "mts":return "mpegts";
                case "webp":return "webp_pipe";
                default:return null;
            }
        }

        static PreviewResult Linked(Stream source,string format,int side,int limit,int percent=0) {
            var decode=Native.Export<DecodeFrame>("mcr_decode");
            var scratch=new byte[65536];Exception ioError=null;
            var input=new Input {
                Read=(state,address,count)=>{try{int read=source.Read(scratch,0,Math.Min(scratch.Length,count));Marshal.Copy(scratch,0,address,read);return read;}catch(Exception e){ioError=e;return -1;}},
                Seek=(state,offset,origin)=>{try{return origin==0x10000 ? source.Length : origin>=0 && origin<=2 ? source.Seek(offset,(SeekOrigin)origin) : -1;}catch(IOException){return -1;}}
            };
            byte[] pixels=null;
            GCHandle pinned=new GCHandle();
            int allocatedWidth=0,allocatedHeight=0,allocatedStride=0;
            Exception allocationError=null;
            AllocatePixels allocate=(width,height,stride,capacity)=>{
                if(pixels!=null || width<1 || height<1 || width>side || height>side ||
                    (long)width*height>InspectionPlan.MaxPixels || stride!=((width*4+63)&~63) ||
                    capacity!=(long)stride*height+64)
                    return IntPtr.Zero;
                try{
                    pixels=new byte[capacity];
                    pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                    allocatedWidth=width;allocatedHeight=height;allocatedStride=stride;
                    return pinned.AddrOfPinnedObject();
                }catch(Exception error){allocationError=error;return IntPtr.Zero;}
            };
            int w,h,sw,sh,angle,pitch;
            BitmapSource frame;
            try{
                int result=decode(format,ref input,allocate,side,(int)InspectionPlan.MaxPixels,out pitch,out w,out h,out sw,out sh,out angle,percent);
                GC.KeepAlive(input);
                if(ioError!=null)throw ioError;
                if(allocationError!=null)throw allocationError;
                if(result==1 && percent!=0)return null;
                if(result!=0 || pixels==null || w!=allocatedWidth || h!=allocatedHeight || pitch!=allocatedStride)
                    throw new InvalidDataException("Linked FFmpeg could not decode a bounded frame ("+result+").");
                frame=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,pitch);
            }finally{if(pinned.IsAllocated)pinned.Free();}
            if(angle!=0)frame=new TransformedBitmap(frame,new RotateTransform(angle));
            if(Math.Abs(angle)%180==90){int swap=sw;sw=sh;sh=swap;}
            return Encode(frame,sw,sh,side,limit,"Linked FFmpeg 9.0.2");
        }

        static PreviewResult WindowsImage(Stream stream,string type,int side,int limit) {
            bool scaledDecoder=type=="jpeg" || type=="png";
            if(scaledDecoder){int w,h;ImageHeader.Dimensions(stream,type,out w,out h);ImageHeader.SourcePixels(type,w,h);}
            stream.Position=0;
            var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
            BitmapFrame frame=type=="ico" ? decoder.Frames.OrderByDescending(f=>(long)f.PixelWidth*f.PixelHeight).First() : decoder.Frames[0];
            int width=frame.PixelWidth,height=frame.PixelHeight;
            if(width<1 || height<1 || (long)width*height>(scaledDecoder ? 200000000 : 32000000))
                throw new InvalidDataException("Image dimensions exceed the preview limit.");
            int orientation=1;
            try {
                var metadata=frame.Metadata as BitmapMetadata;
                object value=metadata==null ? null : metadata.GetQuery(type=="jpeg" ? "/app1/ifd/{ushort=274}" : "/ifd/{ushort=274}");
                if(value!=null)orientation=Convert.ToInt32(value);
            }catch(NotSupportedException){}catch(ArgumentException){}
            BitmapSource image=frame;
            int bound=InspectionPlan.BoundSide(width,height,side);
            // Load bounded TIFF pixels before JPEG encoding; transforming a lazy TIFF
            // frame at Save can retain full-resolution WIC buffers across requests.
            if((scaledDecoder || type=="tiff") && bound<Math.Max(width,height)) {
                stream.Position=0;
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
                if(width>=height)bitmap.DecodePixelWidth=bound;else bitmap.DecodePixelHeight=bound;
                bitmap.StreamSource=stream;bitmap.EndInit();image=bitmap;
            }
            var transform=new TransformGroup();
            if(orientation==2 || orientation==5 || orientation==7)transform.Children.Add(new ScaleTransform(-1,1));
            if(orientation==4)transform.Children.Add(new ScaleTransform(1,-1));
            if(orientation==3)transform.Children.Add(new RotateTransform(180));
            if(orientation==5 || orientation==8)transform.Children.Add(new RotateTransform(-90));
            if(orientation==6 || orientation==7)transform.Children.Add(new RotateTransform(90));
            if(transform.Children.Count!=0)image=new TransformedBitmap(image,transform);
            if(orientation>=5 && orientation<=8){int swap=width;width=height;height=swap;}
            return Encode(image,width,height,side,limit,"Windows Imaging Component");
        }

        static PreviewResult Encode(BitmapSource image,int width,int height,int side,int limit,string decoder) {
            int bound=InspectionPlan.BoundSide(image.PixelWidth,image.PixelHeight,side);
            for(int resize=0;resize<4;resize++) {
                double scale=Math.Min(1,bound/(double)Math.Max(image.PixelWidth,image.PixelHeight));
                BitmapSource current=scale<1 ? new TransformedBitmap(image,new ScaleTransform(scale,scale)) : image;
                for(int quality=85;quality>=25;quality-=20) {
                    var encoder=new JpegBitmapEncoder {QualityLevel=quality};encoder.Frames.Add(BitmapFrame.Create(current));
                    using(var output=new MemoryStream()){encoder.Save(output);if(output.Length<=limit)return new PreviewResult {Bytes=output.ToArray(),Width=current.PixelWidth,Height=current.PixelHeight,SourceWidth=width,SourceHeight=height,Decoder=decoder};}
                }
                bound=Math.Max(1,bound*3/4);
            }
            throw new InvalidDataException("Preview exceeds the bounded encoded size.");
        }
    }
}
