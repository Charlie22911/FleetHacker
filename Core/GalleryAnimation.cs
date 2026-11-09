using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
namespace MediaReviewer {
    internal static class GalleryAnimationPolicy {
        internal const int MaxActive=8,VideoFrames=20;
        internal static int VideoPercent(int index){if(index<0 || index>=VideoFrames)throw new ArgumentOutOfRangeException("index");return index*5;}
        internal static long NextDeadline(long previous,long now,int delay){return previous==0 || now-previous>Math.Max(250L,delay*2L) ? now+delay : previous+delay;}
        internal static bool CanAnimate(MediaRow row){return row!=null && row.Status!="EVIDENCE" && !row.SourceIsHistorical && row.LocationCount>0 && !String.IsNullOrEmpty(row.RootPath) && !String.IsNullOrEmpty(row.RelativePath) && row.RootPath.Length+row.RelativePath.Length<=65536 && (row.FileIdentity==null || row.FileIdentity.Length<=1024) && Formats.IsPlayable(row.Extension);}
    }
    internal sealed class GalleryAnimationState {
        internal readonly CardItem Card;internal readonly MediaRow Row;internal readonly bool Gif;
        internal readonly List<BitmapSource> Frames=new List<BitmapSource>();
        internal bool Queued,Finished;internal int Position=-1;internal long Due;
        internal BitmapSource Next;internal int Delay;
        internal GalleryAnimationState(CardItem card){Card=card;var r=card.Record;Row=new MediaRow {GroupId=r.GroupId,ContentId=r.ContentId,PreviewRevision=r.PreviewRevision,RootPath=r.RootPath,RelativePath=r.RelativePath,Size=r.Size,ModifiedTicks=r.ModifiedTicks,FileIdentity=r.FileIdentity,Extension=r.Extension};Gif=String.Equals(r.Extension,"gif",StringComparison.OrdinalIgnoreCase);}
        internal bool Matches(CardItem card){var r=card.Record;return ReferenceEquals(Card,card) && Row.ContentId==r.ContentId && Row.PreviewRevision==r.PreviewRevision && Row.Size==r.Size && Row.ModifiedTicks==r.ModifiedTicks && Row.FileIdentity==r.FileIdentity && Row.RootPath==r.RootPath && Row.RelativePath==r.RelativePath;}
    }
    internal sealed class GalleryAnimationWork {
        internal GalleryAnimationState State;internal int Index;internal long Epoch;internal CancellationTokenSource Cancel;
    }
    internal sealed class GalleryAnimationResult {
        internal GalleryAnimationState State;internal BitmapSource Image;internal int Delay;internal bool Failed,Finished;internal long Epoch;
    }
    internal sealed class GalleryArchiveSource:IDisposable {
        internal readonly string Path;internal readonly FileStamp Stamp;
        internal GalleryArchiveSource(string path,long size,long ticks,string fileIdentity,string root,bool gif,CancellationToken token,long remaining){
            if(size<0 || size>remaining || gif && size>128L*1024*1024)throw new InvalidDataException("Archive animation exceeds the temporary preview limit.");
            Path=MediaScratch.NewArchivePath(System.IO.Path.GetExtension(Archives.Split(path)[1]));
            string identity=null;
            try{
                if(new DriveInfo(System.IO.Path.GetPathRoot(Path)).AvailableFreeSpace<size+64L*1024*1024)throw new IOException("Insufficient temporary space for archive animation.");
                using(var source=SourceRead.Open(path,size,ticks,fileIdentity,token,root))
                using(var output=new FileStream(Paths.Io(Path),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536)){
                    identity=FileAccessNative.Stamp(output).Identity;var buffer=new byte[65536];int count;
                    while((count=source.Stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,count);}
                    source.Validate();output.Flush();Stamp=FileAccessNative.Stamp(output);
                }
            }catch{if(identity!=null)MediaScratch.Release(Path,identity);throw;}
        }
        public void Dispose(){MediaScratch.Release(Path,Stamp.Identity);}
    }
    // Separate isolated GIF/video lanes and one pending frame per visible card. Video caches are
    // limited to 8 x 20 x 256 x 256 x 4 bytes; GIFs retain only their current/next image.
    internal sealed class GalleryAnimationLoader:IDisposable {
        readonly object gate=new object();readonly Queue<GalleryAnimationWork> pending=new Queue<GalleryAnimationWork>();
        readonly Queue<GalleryAnimationResult> results=new Queue<GalleryAnimationResult>();readonly Thread thread;
        GalleryAnimationWork active;bool reset,closing;long epoch;volatile bool closed;
        internal bool Closed {get{return closed;}}
        internal GalleryAnimationLoader(string lane){thread=new Thread(Run){IsBackground=true,Name="MediaReviewer gallery "+lane};thread.Start();}
        internal void Reset(){lock(gate){epoch++;reset=true;if(active!=null)active.Cancel.Cancel();while(pending.Count>0)pending.Dequeue().Cancel.Dispose();results.Clear();Monitor.PulseAll(gate);}}
        internal bool Queue(GalleryAnimationState state,int index){lock(gate){if(closing || pending.Count+results.Count>=GalleryAnimationPolicy.MaxActive)return false;pending.Enqueue(new GalleryAnimationWork {State=state,Index=index,Epoch=epoch,Cancel=new CancellationTokenSource()});Monitor.PulseAll(gate);return true;}}
        internal bool TryTake(out GalleryAnimationResult result){lock(gate){while(results.Count>0){result=results.Dequeue();if(result.Epoch==epoch)return true;}result=null;return false;}}
        void Run(){DecoderHost decoder=null;
            try{while(true){GalleryAnimationWork work;
                bool restart;
                lock(gate){while(!closing && !reset && pending.Count==0)Monitor.Wait(gate);if(closing)break;restart=reset;reset=false;work=pending.Count==0 ? null : active=pending.Dequeue();}
                if(restart){if(decoder!=null){decoder.Dispose();decoder=null;}}
                if(work==null)continue;
                var result=new GalleryAnimationResult {State=work.State,Epoch=work.Epoch};
                try{
                    if(decoder==null)decoder=new DecoderHost(Native.RuntimeDirectory,30000,384);
                    var row=work.State.Row;string path=Catalog.PreviewSourcePath(row.RootPath,row.RelativePath);GalleryDecodedFrame frame;
                    frame=decoder.CreateGalleryFrame(path,row.Size,row.ModifiedTicks,row.FileIdentity,work.Cancel.Token,row.RootPath,work.Index);
                    work.Cancel.Token.ThrowIfCancellationRequested();
                    if(frame==null)result.Finished=true;
                    else try{result.Image=MediaLoader.Decode(frame.Preview.Bytes,256,work.Cancel.Token);result.Delay=frame.DelayMilliseconds;}finally{Array.Clear(frame.Preview.Bytes,0,frame.Preview.Bytes.Length);}
                }catch(OperationCanceledException){}catch(Exception){result.Failed=true;}
                finally{lock(gate){if(!closing && !work.Cancel.IsCancellationRequested && work.Epoch==epoch)results.Enqueue(result);active=null;work.Cancel.Dispose();}}
            }}finally{try{if(decoder!=null)decoder.Dispose();}finally{closed=true;}}
        }
        internal void CloseAsync(){lock(gate){if(closing)return;closing=true;if(active!=null)active.Cancel.Cancel();while(pending.Count>0)pending.Dequeue().Cancel.Dispose();results.Clear();Monitor.PulseAll(gate);}}
        public void Dispose(){CloseAsync();thread.Join();}
    }
    internal sealed class GalleryAnimations:IDisposable {
        readonly GalleryAnimationLoader gifLoader=new GalleryAnimationLoader("GIFs"),videoLoader=new GalleryAnimationLoader("videos");readonly List<GalleryAnimationState> states=new List<GalleryAnimationState>();
        readonly Func<long> milliseconds;CardItem[] visibleCards=new CardItem[0];int rotation;long rotateAt;
        internal GalleryAnimations(Func<long> elapsedMilliseconds=null){var clock=Stopwatch.StartNew();milliseconds=elapsedMilliseconds??(()=>clock.ElapsedMilliseconds);}
        internal bool Closed {get{return gifLoader.Closed && videoLoader.Closed;}}
        internal int ActiveCount {get{return states.Count;}}
        internal long CachedPixels {get{return states.Sum(s=>s.Frames.Sum(f=>(long)f.PixelWidth*f.PixelHeight)+(s.Next==null ? 0 : (long)s.Next.PixelWidth*s.Next.PixelHeight)+(s.Gif && s.Card.Image!=null ? (long)s.Card.Image.PixelWidth*s.Card.Image.PixelHeight : 0));}}
        internal void SetVisible(IEnumerable<CardItem> visible){
            var candidates=visible.Where(c=>GalleryAnimationPolicy.CanAnimate(c.Record)).ToArray();long now=milliseconds();
            if(!visibleCards.SequenceEqual(candidates)){visibleCards=candidates;rotation=0;rotateAt=now+20000;}
            else if(candidates.Length>GalleryAnimationPolicy.MaxActive && now>=rotateAt){rotation=(rotation+GalleryAnimationPolicy.MaxActive)%candidates.Length;rotateAt=now+20000;}
            var chosen=candidates.Skip(rotation).Concat(candidates.Take(rotation)).Take(GalleryAnimationPolicy.MaxActive).ToArray();
            if(chosen.Length==states.Count && chosen.Select((c,i)=>states[i].Matches(c)).All(v=>v))return;
            gifLoader.Reset();videoLoader.Reset();
            var keep=new List<GalleryAnimationState>();
            foreach(var card in chosen){var prior=states.FirstOrDefault(s=>s.Matches(card));keep.Add(prior??new GalleryAnimationState(card));}
            foreach(var state in states)if(!keep.Contains(state))state.Card.SetAnimationImage(null);
            states.Clear();states.AddRange(keep);
            foreach(var state in states){state.Queued=false;if(state.Gif){state.Next=null;state.Finished=false;state.Due=0;}}
        }
        void Drain(GalleryAnimationLoader loader){
            GalleryAnimationResult result;
            while(loader.TryTake(out result)){
                var state=result.State;if(!states.Contains(state))continue;state.Queued=false;
                if(result.Failed){state.Finished=true;state.Next=null;state.Frames.Clear();state.Card.SetAnimationImage(null);continue;}
                if(result.Finished){state.Finished=true;continue;}
                if(state.Gif){state.Next=result.Image;state.Delay=result.Delay;}
                else state.Frames.Add(result.Image);
            }
        }
        internal void Tick(){
            Drain(gifLoader);Drain(videoLoader);
            long now=milliseconds();
            foreach(var state in states){
                if(state.Gif){
                    if(state.Next!=null && now>=state.Due){state.Card.SetAnimationImage(state.Next);state.Next=null;state.Due=GalleryAnimationPolicy.NextDeadline(state.Due,now,state.Delay);}
                    if(!state.Finished && state.Next==null && !state.Queued)state.Queued=gifLoader.Queue(state,-1);
                }else{
                    if(now>=state.Due && state.Frames.Count>0){int next=(state.Position+1)%GalleryAnimationPolicy.VideoFrames;if(next<state.Frames.Count){state.Position=next;state.Card.SetAnimationImage(state.Frames[next]);state.Due=GalleryAnimationPolicy.NextDeadline(state.Due,now,500);}}
                    if(!state.Finished && state.Frames.Count<GalleryAnimationPolicy.VideoFrames && !state.Queued)state.Queued=videoLoader.Queue(state,state.Frames.Count);
                }
            }
        }
        internal void Clear(){SetVisible(new CardItem[0]);}
        internal void CloseAsync(){Clear();gifLoader.CloseAsync();videoLoader.CloseAsync();}
        public void Dispose(){CloseAsync();gifLoader.Dispose();videoLoader.Dispose();}
    }
}
