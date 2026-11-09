using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MediaReviewer {
    internal sealed class GalleryDecodedFrame {internal PreviewResult Preview;internal int DelayMilliseconds;}
    internal static partial class MediaDecode {
        internal static PreviewResult CreateGalleryVideoFrame(string path,long size,long ticks,string identity,string root,int index){
            int percent=GalleryAnimationPolicy.VideoPercent(index);
            using(var source=new FileStream(Paths.Io(path),FileMode.Open,FileAccess.Read,FileShare.Read,65536)){
                var before=FileAccessNative.Stamp(source);
                if(before.Size!=size || before.ModifiedTicks!=ticks || !String.IsNullOrEmpty(identity) && before.Identity!=identity)throw new InvalidDataException("Source changed after fingerprinting.");
                if(!String.IsNullOrEmpty(root))Paths.OpenedSourceWithinRoot(source,root);
                if(source.Length>64L<<30)throw new InvalidDataException("Video exceeds the preview limit.");
                byte[] header=new byte[512];int count=source.Read(header,0,header.Length);Array.Resize(ref header,count);
                string type=ImageHeader.Detect(header,Path.GetExtension(path).TrimStart('.').ToLowerInvariant());
                if(!Formats.IsVideo(type))throw new InvalidDataException("Source has no supported video signature.");
                string demuxer=type=="mpeg" && header.Length>=4 && header[0]==0 && header[1]==0 && header[2]==1 && header[3]==0xB3 ? "mpegvideo" : Demuxer(type);
                source.Position=0;
                var result=Linked(source,demuxer,256,32768,percent);
                if(!before.Same(FileAccessNative.Stamp(source)))throw new InvalidDataException("Source changed during frame sampling.");
                if(result==null)throw new InvalidDataException("Video frame is unavailable at this sample.");
                return result;
            }
        }
        internal sealed class GalleryGif:IDisposable {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Open(ref Input input,int repeats);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Next(IntPtr gif,AllocatePixels allocate,out int width,out int height,out int delay);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Close(IntPtr gif);
            readonly FileStream source;readonly FileStamp stamp;readonly Input input;readonly Next next;readonly Close close;
            readonly byte[] scratch=new byte[65536];IntPtr handle;Exception ioError;
            internal GalleryGif(string path,long size,long ticks,string identity,string root){
                source=new FileStream(Paths.Io(path),FileMode.Open,FileAccess.Read,FileShare.Read,65536);
                try{
                    stamp=FileAccessNative.Stamp(source);
                    if(stamp.Size!=size || stamp.ModifiedTicks!=ticks || !String.IsNullOrEmpty(identity) && stamp.Identity!=identity)throw new InvalidDataException("Source changed after fingerprinting.");
                    if(!String.IsNullOrEmpty(root))Paths.OpenedSourceWithinRoot(source,root);
                    if(source.Length>128L<<20)throw new InvalidDataException("GIF exceeds the preview limit.");
                    byte[] header=new byte[16];int count=source.Read(header,0,header.Length);Array.Resize(ref header,count);
                    if(ImageHeader.Detect(header,"")!="gif")throw new InvalidDataException("Source has no GIF signature.");
                    source.Position=0;int repeats=GifRepeats(source);source.Position=0;
                    input=new Input {
                        Read=(state,address,length)=>{try{int read=source.Read(scratch,0,Math.Min(length,scratch.Length));Marshal.Copy(scratch,0,address,read);return read;}catch(Exception error){ioError=error;return -1;}},
                        Seek=(state,offset,origin)=>{try{return origin==0x10000 ? source.Length : origin>=0 && origin<=2 ? source.Seek(offset,(SeekOrigin)origin) : -1;}catch(Exception error){ioError=error;return -1;}}
                    };
                    next=Native.Export<Next>("mcr_gif_next");close=Native.Export<Close>("mcr_gif_close");
                    var descriptor=input;handle=Native.Export<Open>("mcr_gif_open")(ref descriptor,repeats);
                    if(ioError!=null)throw ioError;
                    if(handle==IntPtr.Zero)throw new InvalidDataException("GIF animation could not be opened.");
                }catch{Dispose();throw;}
            }
            static int GifRepeats(Stream stream){
                using(var reader=new BinaryReader(stream,System.Text.Encoding.ASCII,true)){
                    byte[] header=reader.ReadBytes(13);if(header.Length!=13)throw new InvalidDataException("Incomplete GIF header.");
                    if((header[10]&128)!=0)Skip(stream,3L*(1<<((header[10]&7)+1)));
                    while(stream.Position<stream.Length){
                        int marker=reader.ReadByte();if(marker==0x3b)return -1;
                        if(marker==0x2c){byte[] image=reader.ReadBytes(9);if(image.Length!=9)throw new InvalidDataException("Incomplete GIF image.");if((image[8]&128)!=0)Skip(stream,3L*(1<<((image[8]&7)+1)));reader.ReadByte();}
                        else if(marker==0x21){int label=reader.ReadByte();if(label==255){int length=reader.ReadByte();byte[] name=reader.ReadBytes(length);if(name.Length!=length)throw new EndOfStreamException();string app=System.Text.Encoding.ASCII.GetString(name);if(app=="NETSCAPE2.0" || app=="ANIMEXTS1.0"){int block=reader.ReadByte();byte[] loop=reader.ReadBytes(block);if(loop.Length!=block)throw new EndOfStreamException();if(block==3 && loop[0]==1)return loop[1]|loop[2]<<8;}}}
                        else throw new InvalidDataException("Invalid GIF block.");
                        int count;while((count=reader.ReadByte())!=0)Skip(stream,count);
                    }
                    throw new InvalidDataException("Incomplete GIF stream.");
                }
            }
            static void Skip(Stream stream,long count){if(count<0 || count>stream.Length-stream.Position)throw new EndOfStreamException();stream.Position+=count;}
            internal GalleryDecodedFrame Read(){
                if(handle==IntPtr.Zero)throw new ObjectDisposedException("GIF animation");
                byte[] pixels=null;GCHandle pinned=new GCHandle();int stride=0,allocatedWidth=0,allocatedHeight=0;
                AllocatePixels allocate=(width,height,pitch,capacity)=>{
                    if(pixels!=null || width<1 || height<1 || width>256 || height>256 || pitch!=((width*4+63)&~63) || capacity!=(long)pitch*height+64)return IntPtr.Zero;
                    pixels=new byte[capacity];stride=pitch;allocatedWidth=width;allocatedHeight=height;pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);return pinned.AddrOfPinnedObject();
                };
                try{
                    int width,height,delay;int status=next(handle,allocate,out width,out height,out delay);
                    GC.KeepAlive(input);
                    if(ioError!=null)throw ioError;
                    if(!stamp.Same(FileAccessNative.Stamp(source)))throw new InvalidDataException("GIF changed during playback.");
                    if(status==1)return null;
                    if(status!=0 || pixels==null || width!=allocatedWidth || height!=allocatedHeight)throw new InvalidDataException("GIF animation could not decode its next frame.");
                    var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,stride);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using(var encoded=new MemoryStream()){
                        encoder.Save(encoded);if(encoded.Length>512*1024)throw new InvalidDataException("GIF display frame exceeds its encoded limit.");
                        return new GalleryDecodedFrame {Preview=new PreviewResult {Bytes=encoded.ToArray(),Width=width,Height=height,SourceWidth=width,SourceHeight=height,Mime="image/png",Decoder="Linked FFmpeg GIF animation"},DelayMilliseconds=delay};
                    }
                }finally{if(pinned.IsAllocated)pinned.Free();}
            }
            public void Dispose(){if(handle!=IntPtr.Zero){close(handle);handle=IntPtr.Zero;}if(source!=null)source.Dispose();}
        }
    }
}
