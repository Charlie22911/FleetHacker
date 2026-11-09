using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MediaReviewer {
    internal static class GalleryAnimationSmoke {
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        internal static void Policy(){
            var policy=typeof(CardItem).Assembly.GetType("MediaReviewer.GalleryAnimationPolicy");
            Check(policy!=null,"Gallery animation is unavailable: video thumbnails cannot cycle across the full recording.");
            var sample=policy.GetMethod("VideoPercent",BindingFlags.Static|BindingFlags.NonPublic);
            int[] expected={0,5,10,15,20,25,30,35,40,45,50,55,60,65,70,75,80,85,90,95};
            for(int i=0;i<expected.Length;i++)Check((int)sample.Invoke(null,new object[]{i})==expected[i],"Video gallery samples must include the beginning, middle and final section in order.");
            try{sample.Invoke(null,new object[]{20});throw new Exception("Out of range video frame accepted");}catch(TargetInvocationException error){Check(error.InnerException is ArgumentOutOfRangeException,"Unexpected sample range failure");}
            long elapsed=0;var poster=Bitmap(20,20,20);var animation=Bitmap(200,0,0);
            var cards=Enumerable.Range(1,16).Select(i=>{var card=new CardItem(new MediaRow {GroupId=i,ContentId=i,RootPath="C:\\gallery",RelativePath=i+".gif",Extension="gif",LocationCount=1});card.SetImage(poster,null);return card;}).ToArray();
            using(var gallery=new GalleryAnimations(()=>elapsed)){
                gallery.SetVisible(cards);Check(gallery.ActiveCount==8,"Visible animation cap failed.");foreach(var card in cards.Take(8))card.SetAnimationImage(animation);
                elapsed=20001;gallery.SetVisible(cards);Check(cards.Take(8).All(c=>ReferenceEquals(c.Image,poster)),"Visible cards beyond the first group were starved of animation slots.");
                foreach(var card in cards.Skip(8))card.SetAnimationImage(animation);elapsed=40002;gallery.SetVisible(cards);Check(cards.Skip(8).All(c=>ReferenceEquals(c.Image,poster)),"Gallery animation groups did not rotate back.");
                gallery.Clear();Check(gallery.ActiveCount==0,"Clearing the gallery retained active slots.");
            }
            Cadence();
        }
        static void Cadence(){
            long elapsed=0;var card=new CardItem(new MediaRow {GroupId=1,ContentId=1,RootPath="C:\\gallery",RelativePath="timed.gif",Extension="gif",LocationCount=1});card.SetImage(Bitmap(10,10,10),null);
            using(var gallery=new GalleryAnimations(()=>elapsed)){
                gallery.SetVisible(new[]{card});var states=(List<GalleryAnimationState>)typeof(GalleryAnimations).GetField("states",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gallery);var state=states[0];state.Queued=true;state.Delay=50;
                var presented=new List<long>();var previous=card.Image;
                for(elapsed=0;elapsed<=400;elapsed+=20){if(state.Next==null)state.Next=Bitmap(200,0,0);gallery.Tick();if(!ReferenceEquals(previous,card.Image)){presented.Add(elapsed);previous=card.Image;}}
                Check(presented.SequenceEqual(new long[]{0,60,100,160,200,260,300,360,400}),"GIF presentation accumulated timer rounding instead of retaining its frame cadence.");
                elapsed=2000;state.Next=Bitmap(0,200,0);gallery.Tick();var resumed=card.Image;state.Next=Bitmap(0,0,200);elapsed=2020;gallery.Tick();Check(ReferenceEquals(card.Image,resumed),"GIF replayed stale deadlines after a long stall.");elapsed=2060;gallery.Tick();Check(!ReferenceEquals(card.Image,resumed),"GIF did not resume its frame cadence after a long stall.");
            }
        }
        static byte[] Solid(byte red,byte green,byte blue){var data=new byte[16*16*4];for(int i=0;i<data.Length;i+=4){data[i]=blue;data[i+1]=green;data[i+2]=red;data[i+3]=255;}return data;}
        static BitmapSource Bitmap(byte red,byte green,byte blue){var result=BitmapSource.Create(16,16,96,96,PixelFormats.Bgra32,null,Solid(red,green,blue),64);result.Freeze();return result;}
        static byte[] Pixel(BitmapSource image,int x,int y){var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];converted.CopyPixels(pixels,converted.PixelWidth*4,0);int at=(y*converted.PixelWidth+x)*4;return new[]{pixels[at+2],pixels[at+1],pixels[at]};}
        static BitmapSource Decode(GalleryDecodedFrame frame){try{return MediaLoader.Decode(frame.Preview.Bytes,256);}finally{Array.Clear(frame.Preview.Bytes,0,frame.Preview.Bytes.Length);}}
        static void Color(BitmapSource image,int x,int y,int red,int green,int blue,string message,int tolerance=40){var p=Pixel(image,x,y);Check(Math.Abs(p[0]-red)<tolerance && Math.Abs(p[1]-green)<tolerance && Math.Abs(p[2]-blue)<tolerance,message+" actual="+String.Join(",",p));}
        static void Block(BinaryWriter writer,byte[] data){for(int offset=0;offset<data.Length;){int count=Math.Min(255,data.Length-offset);writer.Write((byte)count);writer.Write(data,offset,count);offset+=count;}writer.Write((byte)0);}
        static void GifFrame(BinaryWriter writer,int left,int width,byte color,int disposal,int delay,bool transparent=false){
            writer.Write(new byte[]{0x21,0xf9,4,(byte)((disposal<<2)|(transparent ? 1 : 0)),(byte)delay,0,0,0,0x2c});writer.Write((ushort)left);writer.Write((ushort)0);writer.Write((ushort)width);writer.Write((ushort)10);writer.Write((byte)0);writer.Write((byte)2);
            var bytes=new List<byte>();int bits=0,count=0;Action<int> code=value=>{bits|=value<<count;count+=3;while(count>=8){bytes.Add((byte)bits);bits>>=8;count-=8;}};
            for(int i=0;i<width*10;i++){code(4);code(color);}code(5);if(count>0)bytes.Add((byte)bits);Block(writer,bytes.ToArray());
        }
        static void Gif(string path,int disposal,int repeats){using(var writer=new BinaryWriter(File.Create(path))){writer.Write(Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)30);writer.Write((ushort)10);writer.Write(new byte[]{0x81,0,0,0,0,0,255,0,0,0,255,0,0,0,255});if(repeats>=0){writer.Write(new byte[]{0x21,0xff,11});writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));writer.Write(new byte[]{3,1,(byte)repeats,(byte)(repeats>>8),0});}GifFrame(writer,0,30,1,1,5);GifFrame(writer,0,10,2,disposal,7);GifFrame(writer,20,10,3,1,9);writer.Write((byte)0x3b);}}
        static MediaRow Row(string path,string type,long group=1){using(var file=File.OpenRead(path)){var stamp=FileAccessNative.Stamp(file);return new MediaRow {GroupId=group,ContentId=group,RootPath=Path.GetDirectoryName(path),RelativePath=Path.GetFileName(path),Name=Path.GetFileName(path),Extension=type,Size=stamp.Size,ModifiedTicks=stamp.ModifiedTicks,FileIdentity=stamp.Identity,LocationCount=1,Status="TO_REVIEW"};}}
        static void Transparency(string path){
            using(var writer=new BinaryWriter(File.Create(path))){writer.Write(Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)30);writer.Write((ushort)10);writer.Write(new byte[]{0x81,0,0,0,0,0,255,0,0,0,255,0,0,0,255});GifFrame(writer,0,10,1,1,5,true);writer.Write((byte)0x3b);}
            var row=Row(path,"gif");using(var gif=new MediaDecode.GalleryGif(path,row.Size,row.ModifiedTicks,row.FileIdentity,row.RootPath)){
                var frame=gif.Read();Check(frame.Preview.Mime=="image/png" && frame.Preview.Bytes.Length<=512*1024,"GIF alpha frame did not use bounded PNG encoding.");var image=Decode(frame);var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixel=new byte[4];rgba.CopyPixels(new Int32Rect(20,5,1,1),pixel,4,0);Check(pixel[3]==0,"GIF transparency was matted into an opaque thumbnail.");Color(image,5,5,255,0,0,"Opaque GIF pixels changed while retaining alpha.",4);
            }
        }
        static void GifFrames(string path,int disposal,int repeats,int expected){
            Gif(path,disposal,repeats);var row=Row(path,"gif");
            using(var gif=new MediaDecode.GalleryGif(path,row.Size,row.ModifiedTicks,row.FileIdentity,row.RootPath)){
                int[] delays={50,70,90};for(int i=0;i<expected;i++){var frame=gif.Read();Check(frame!=null,"GIF ended before its requested loops finished.");Check(frame.DelayMilliseconds==delays[i%3],"GIF frame delay was not retained.");var image=Decode(frame);if(i%3==0)Color(image,15,5,255,0,0,"GIF first frame");if(i%3==1)Color(image,5,5,0,255,0,"GIF changed rectangle");if(i%3==2){Color(image,15,5,255,0,0,"GIF preserved unchanged canvas");Color(image,25,5,0,0,255,"GIF final rectangle");if(disposal==3)Color(image,5,5,255,0,0,"GIF restore previous disposal");else {var pixel=Pixel(image,5,5);Check(pixel[1]<80,"GIF restore background disposal retained the prior green rectangle.");}}}
                if(repeats!=0)Check(gif.Read()==null,"GIF ignored its finite/no-extension loop boundary.");
            }
            using(var exclusive=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.None)){}
        }
        static void Four(BinaryWriter writer,string text){writer.Write(Encoding.ASCII.GetBytes(text));}
        static byte[] Data(Action<BinaryWriter> body){using(var data=new MemoryStream()){using(var writer=new BinaryWriter(data,Encoding.ASCII,true))body(writer);return data.ToArray();}}
        static void Chunk(BinaryWriter writer,string name,byte[] payload){Four(writer,name);writer.Write(payload.Length);writer.Write(payload);if((payload.Length&1)!=0)writer.Write((byte)0);}
        static void Avi(string path,int frameCount=20){const int width=32,height=16,size=width*height*3;
            byte[] header=Data(w=>{Four(w,"hdrl");Chunk(w,"avih",Data(a=>{a.Write(1000000);a.Write(size);a.Write(0);a.Write(0x10);a.Write(frameCount);a.Write(0);a.Write(1);a.Write(size);a.Write(width);a.Write(height);for(int i=0;i<4;i++)a.Write(0);}));Chunk(w,"LIST",Data(s=>{Four(s,"strl");Chunk(s,"strh",Data(a=>{Four(a,"vids");Four(a,"DIB ");a.Write(0);a.Write((short)0);a.Write((short)0);a.Write(0);a.Write(1);a.Write(1);a.Write(0);a.Write(frameCount);a.Write(size);a.Write(-1);a.Write(0);a.Write((short)0);a.Write((short)0);a.Write((short)width);a.Write((short)height);}));Chunk(s,"strf",Data(a=>{a.Write(40);a.Write(width);a.Write(height);a.Write((short)1);a.Write((short)24);a.Write(0);a.Write(size);for(int i=0;i<4;i++)a.Write(0);}));}));});
            using(var writer=new BinaryWriter(File.Create(path))){Four(writer,"RIFF");writer.Write(0);Four(writer,"AVI ");Chunk(writer,"LIST",header);long movi=writer.BaseStream.Position;Four(writer,"LIST");writer.Write(4+frameCount*(8+size));Four(writer,"movi");for(int frame=0;frame<frameCount;frame++){var pixels=new byte[size];for(int i=0;i<pixels.Length;i+=3)pixels[i+2]=(byte)(frame*(frameCount==2 ? 120 : 12));Chunk(writer,"00db",pixels);}Chunk(writer,"idx1",Data(a=>{for(int i=0;i<frameCount;i++){Four(a,"00db");a.Write(0x10);a.Write(4+i*(8+size));a.Write(size);}}));long end=writer.BaseStream.Position;writer.BaseStream.Position=4;writer.Write((int)end-8);}
        }
        static void Wait(GalleryAnimations gallery,Func<bool> done,int milliseconds=15000){var timer=Stopwatch.StartNew();while(!done()){gallery.Tick();if(timer.ElapsedMilliseconds>milliseconds)throw new Exception("Gallery animation timed out.");Thread.Sleep(10);}}
        static void Lifecycle(string gifPath,string videoPath){
            var poster=Bitmap(40,40,40);var card=new CardItem(Row(gifPath,"gif"));card.SetImage(poster,null);
            using(var gallery=new GalleryAnimations()){
                gallery.SetVisible(new[]{card});Wait(gallery,()=>!ReferenceEquals(card.Image,poster));var first=card.Image;Wait(gallery,()=>!ReferenceEquals(card.Image,first));gallery.Clear();Check(ReferenceEquals(card.Image,poster),"Leaving the viewport must restore the saved thumbnail.");Check(gallery.ActiveCount==0 && gallery.CachedPixels==0,"Leaving the viewport retained animated frame caches.");
                var cards=Enumerable.Range(1,12).Select(i=>new CardItem(Row(gifPath,"gif",i))).ToArray();gallery.SetVisible(cards);Check(gallery.ActiveCount==8,"Gallery active-card limit failed.");gallery.Clear();
                var missing=new CardItem(new MediaRow {ContentId=1,GroupId=1,RootPath=Path.GetDirectoryName(gifPath),RelativePath="missing.gif",Extension="gif",LocationCount=1,Status="TO_REVIEW"});missing.SetImage(poster,null);gallery.SetVisible(new[]{missing});for(int i=0;i<30;i++){gallery.Tick();Thread.Sleep(10);}Check(ReferenceEquals(missing.Image,poster),"Disconnected source lost its saved-thumbnail fallback.");gallery.Clear();
                var video=new CardItem(Row(videoPath,"avi"));video.SetImage(poster,null);gallery.SetVisible(new[]{video});Wait(gallery,()=>gallery.CachedPixels==20L*32*16);Check(gallery.CachedPixels<=8L*20*256*256,"Gallery video cache exceeded its pixel bound.");var firstVideo=video.Image;Wait(gallery,()=>!ReferenceEquals(firstVideo,video.Image));gallery.Clear();Check(ReferenceEquals(video.Image,poster),"Video animation did not restore its saved poster.");
                var protectedRow=Row(gifPath,"gif");protectedRow.Status="EVIDENCE";var disconnected=Row(gifPath,"gif");disconnected.LocationCount=0;var historical=Row(gifPath,"gif");historical.SourceIsHistorical=true;gallery.SetVisible(new[]{new CardItem(protectedRow),new CardItem(disconnected),new CardItem(historical)});Check(gallery.ActiveCount==0,"Protected/disconnected/historical rows must use their saved posters.");
            }
        }
        static void ArchiveLifecycle(string directory,string gifPath,string videoPath){
            string archive=Path.Combine(directory,"animations.zip");
            using(var zip=new ZipArchive(File.Create(archive),ZipArchiveMode.Create)){foreach(var path in new[]{gifPath,videoPath})using(var output=zip.CreateEntry(Path.GetFileName(path),CompressionLevel.NoCompression).Open())using(var input=File.OpenRead(path))input.CopyTo(output);}
            var original=File.ReadAllBytes(archive);var members=Archives.Inspect(archive);var poster=Bitmap(30,30,30);
            var cards=members.Select((member,i)=>{
                var row=new MediaRow {GroupId=i+1,ContentId=i+1,RootPath=directory,RelativePath=Path.GetFileName(archive)+Archives.Separator+member.Name,Name=member.Name,Extension=Path.GetExtension(member.Name).TrimStart('.'),Size=member.Size,ModifiedTicks=member.ModifiedTicks,FileIdentity=member.Identity,LocationCount=1,Status="TO_REVIEW"};var card=new CardItem(row);card.SetImage(poster,null);return card;
            }).ToArray();
            MediaScratch.EnsureDirectory();var prior=new HashSet<string>(Directory.GetDirectories(Path.GetTempPath(),"MediaComplianceReviewer-*"),StringComparer.OrdinalIgnoreCase);
            using(var gallery=new GalleryAnimations()){
                gallery.SetVisible(cards);Wait(gallery,()=>cards.All(c=>!ReferenceEquals(c.Image,poster)));var first=cards[0].Image;Wait(gallery,()=>!ReferenceEquals(cards[0].Image,first));
                var workers=Directory.GetDirectories(Path.GetTempPath(),"MediaComplianceReviewer-*").Where(path=>!prior.Contains(path)).ToArray();Check(workers.Length==2,"GIF and video archive preparation must use independent isolated workers.");
                Check(workers.Sum(worker=>Directory.GetFiles(worker,"archive-preview-*").Length)==2,"Gallery retained more than one extracted original per visible archive member.");
                gallery.Clear();Wait(gallery,()=>workers.All(worker=>!Directory.Exists(worker)));
                Check(cards.All(c=>ReferenceEquals(c.Image,poster)),"Archived animation did not restore its saved poster.");
            }
            Check(File.ReadAllBytes(archive).SequenceEqual(original),"Archive animation changed its source archive.");
            bool limited=false;var selected=cards[0].Record;try{using(var source=new GalleryArchiveSource(selected.SourcePath,selected.Size,selected.ModifiedTicks,selected.FileIdentity,selected.RootPath,true,CancellationToken.None,selected.Size-1)){} }catch(InvalidDataException){limited=true;}Check(limited,"Archive animation ignored its remaining disk budget.");
            using(var exclusive=File.Open(archive,FileMode.Open,FileAccess.Read,FileShare.None)){}
        }
        internal static int Run(string directory){
            try{Directory.CreateDirectory(directory);Policy();string once=Path.Combine(directory,"once.gif"),restore=Path.Combine(directory,"restore.gif"),finite=Path.Combine(directory,"finite.gif"),loop=Path.Combine(directory,"loop.gif"),video=Path.Combine(directory,"samples.avi");
                GifFrames(once,2,-1,3);GifFrames(restore,3,-1,3);GifFrames(finite,3,1,6);GifFrames(loop,3,0,9);Transparency(Path.Combine(directory,"transparent.gif"));Avi(video);
                var row=Row(video,"avi");using(var host=new DecoderHost(Native.RuntimeDirectory)){for(int index=0;index<20;index++){var frame=host.CreateGalleryFrame(video,row.Size,row.ModifiedTicks,row.FileIdentity,CancellationToken.None,row.RootPath,index);var image=Decode(frame);Color(image,16,8,index*12,0,0,"Video sample "+index+" did not come from its recorded position.",4);}}
                string shortVideo=Path.Combine(directory,"short.avi");Avi(shortVideo,2);var shortRow=Row(shortVideo,"avi");using(var host=new DecoderHost(Native.RuntimeDirectory)){var last=host.CreateGalleryFrame(shortVideo,shortRow.Size,shortRow.ModifiedTicks,shortRow.FileIdentity,CancellationToken.None,shortRow.RootPath,19);Color(Decode(last),16,8,120,0,0,"Short video must repeat its final available frame for the last sample.",4);}
                var gif=Row(loop,"gif");using(var host=new DecoderHost(Native.RuntimeDirectory)){for(int i=0;i<105;i++){var frame=host.CreateGalleryFrame(loop,gif.Size,gif.ModifiedTicks,gif.FileIdentity,CancellationToken.None,gif.RootPath,-1);Check(frame!=null && frame.DelayMilliseconds==new[]{50,70,90}[i%3],"Private GIF stream restarted or lost timing across 100 requests.");Array.Clear(frame.Preview.Bytes,0,frame.Preview.Bytes.Length);}}
                Lifecycle(loop,video);ArchiveLifecycle(directory,loop,video);
                File.WriteAllText(Path.Combine(directory,"gallery-animation.txt"),"PASS: 20 full-span video samples; GIF alpha, timing/cadence, background/previous disposal, finite/infinite/no-extension loops; persistent isolated GIF stream; independent GIF/video workers; visible-card/cache bounds; poster fallback; archive/viewport cancellation and source release.");Console.WriteLine("PASS gallery animation");return 0;
            }catch(Exception error){File.WriteAllText(Path.Combine(directory,"gallery-animation-error.txt"),error.ToString());Console.Error.WriteLine(error);return 1;}
        }
    }
}
