using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MediaReviewer {
    internal static class PreviewMemorySmoke {
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        static void Fixture(string path,int width,int height,PixelFormat format,bool oriented=false){
            int stride=checked((width*format.BitsPerPixel+7)/8);
            var pixels=new byte[checked(stride*height)];
            if(oriented){for(int y=0;y<height;y++)for(int x=0;x<width;x++){int at=y*stride+x*3;pixels[at]=(byte)(x<width/2 ? 255 : 0);pixels[at+2]=(byte)(x>=width/2 ? 255 : 0);}}
            else for(int i=0;i<pixels.Length;i+=2){pixels[i]=0x80;if(i+1<pixels.Length)pixels[i+1]=0x60;}
            var bitmap=BitmapSource.Create(width,height,96,96,format,null,pixels,stride);
            BitmapMetadata metadata=null;
            if(oriented){metadata=new BitmapMetadata("tiff");metadata.SetQuery("/ifd/{ushort=274}",(ushort)6);}
            var encoder=new TiffBitmapEncoder {Compression=TiffCompressOption.Lzw};
            encoder.Frames.Add(BitmapFrame.Create(bitmap,null,metadata,null));
            using(var output=File.Create(path))encoder.Save(output);
        }
        static void CheckPreview(PreviewResult preview,int width,int height,int side,int limit){
            Check(preview.SourceWidth==width && preview.SourceHeight==height,"TIFF source dimensions changed.");
            Check(preview.Width>0 && preview.Height>0 && preview.Width<=side && preview.Height<=side && (long)preview.Width*preview.Height<=InspectionPlan.MaxPixels,"TIFF output exceeded its decoded pixel bound.");
            Check(preview.Bytes!=null && preview.Bytes.Length>0 && preview.Bytes.Length<=limit,"TIFF output exceeded its encoded byte bound.");
            int encodedWidth,encodedHeight;Preview.Dimensions(preview.Bytes,preview.Bytes.Length,out encodedWidth,out encodedHeight);
            Check(encodedWidth==preview.Width && encodedHeight==preview.Height,"TIFF output dimensions disagree with its encoded JPEG.");
        }
        static long PrivateBytes(DecoderHost host){var process=(Process)typeof(DecoderHost).GetField("process",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);process.Refresh();return process.PrivateMemorySize64;}
        static void Orientation(string path){
            Fixture(path,600,300,PixelFormats.Rgb24,true);
            using(var host=new DecoderHost(Native.RuntimeDirectory)){
                var preview=host.Create(path,-1,0,null,256,32768,CancellationToken.None);
                CheckPreview(preview,300,600,256,32768);
                Check(preview.Width==128 && preview.Height==256,"TIFF rotation/scaling produced incorrect display geometry.");
                var image=MediaLoader.Decode(preview.Bytes,256);var rgb=new FormatConvertedBitmap(image,PixelFormats.Rgb24,null,0);var top=new byte[3];var bottom=new byte[3];
                rgb.CopyPixels(new Int32Rect(64,40,1,1),top,3,0);rgb.CopyPixels(new Int32Rect(64,210,1,1),bottom,3,0);
                Check(top[0]>220 && top[2]<30 && bottom[0]<30 && bottom[2]>220,"TIFF orientation changed or doubled the recorded rotation.");
            }
        }
        internal static int Run(string directory,string existing=null){
            try{
                Directory.CreateDirectory(directory);
                string image=Path.Combine(directory,"rgb48-24mp.tif"),oversized=Path.Combine(directory,"over-32mp.tif");
                Fixture(image,6000,4000,PixelFormats.Rgb48);Fixture(oversized,8000,4500,PixelFormats.BlackWhite);
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                long sampledPrivate=0;
                using(var host=new DecoderHost(Native.RuntimeDirectory)){
                    for(int index=0;index<24;index++){
                        CheckPreview(host.Create(image,-1,0,null,256,32768,CancellationToken.None),6000,4000,256,32768);
                        sampledPrivate=Math.Max(sampledPrivate,PrivateBytes(host));
                    }
                    Console.WriteLine("24 consecutive RGB48 TIFF thumbnails passed in the 384MiB decoder job.");
                    for(int index=0;index<8;index++){
                        CheckPreview(host.Create(image,-1,0,null,InspectionPlan.MaxSide,InspectionPlan.EncodedLimit,CancellationToken.None),6000,4000,InspectionPlan.MaxSide,InspectionPlan.EncodedLimit);
                        sampledPrivate=Math.Max(sampledPrivate,PrivateBytes(host));
                    }
                    Console.WriteLine("8 consecutive bounded TIFF inspection previews passed.");
                    bool rejected=false;
                    try{host.Create(oversized,-1,0,null,256,32768,CancellationToken.None);}catch(InvalidDataException error){rejected=error.Message.Contains("Image dimensions exceed the preview limit");}
                    Check(rejected,"TIFF decoding no longer enforces its 32-megapixel source guardrail.");
                }
                Orientation(Path.Combine(directory,"oriented.tif"));
                if(existing!=null){using(var host=new DecoderHost(Native.RuntimeDirectory)){
                    var preview=host.Create(existing,-1,0,null,256,32768,CancellationToken.None);
                    Check(preview.Width>0 && preview.Height>0 && preview.Width<=256 && preview.Height<=256 && preview.Bytes.Length<=32768,"Existing TIFF did not produce a bounded thumbnail.");
                    Console.WriteLine("Existing TIFF: "+preview.Width+"x"+preview.Height+"; source "+preview.SourceWidth+"x"+preview.SourceHeight);
                }}
                string report="PASS: 24 RGB48 TIFF thumbnails and 8 inspection previews in the unchanged 384MiB worker; 32MP TIFF source guardrail retained; decoded-pixel and encoded-byte limits retained; recorded TIFF orientation preserved. Sampled worker private bytes="+sampledPrivate+". Existing source tested="+(existing!=null)+".";
                File.WriteAllText(Path.Combine(directory,"preview-memory.txt"),report,Encoding.UTF8);Console.WriteLine(report);return 0;
            }catch(Exception error){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"preview-memory-error.txt"),error.ToString(),Encoding.UTF8);Console.Error.WriteLine(error);return 1;}
        }
    }
}
