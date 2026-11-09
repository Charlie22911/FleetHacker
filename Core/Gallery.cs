using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace MediaReviewer {
    public sealed class CardItem:INotifyPropertyChanged {
        public MediaRow Record { get; private set; }
        bool selected,pending; BitmapSource image,animationImage; string imageError;
        public bool ImageQueued;
        public CardItem(MediaRow record) { Record=record; }
        public string Name { get { return Record.Name; } }
        public string PathLabel { get { return Record.RelativePath; } }
        public string FolderLabel {get{string path=Record.RelativePath??"";int split=path.IndexOf('\\');return split<0 ? "" : path.Substring(0,split);}}
        public string TypeLabel {get{
            string detected=(Record.Extension??"").ToUpperInvariant();
            string name=Record.Name??"";int dot=name.LastIndexOf('.');
            string actual=dot<0 ? "" : name.Substring(dot+1).ToUpperInvariant();
            return detected.Length!=0 && actual!=detected ? detected+" (detected)" : detected;
        }}
        public bool IsRenamed {get{
            string detected=(Record.Extension??"").ToUpperInvariant();
            string name=Record.Name??"";int dot=name.LastIndexOf('.');
            string actual=dot<0 ? "" : name.Substring(dot+1).ToUpperInvariant();
            return detected.Length!=0 && actual!=detected;
        }}
        public Visibility RenamedBadgeVisibility {get{return IsRenamed ? Visibility.Visible : Visibility.Collapsed;}}
        public Visibility MultiLocationBadgeVisibility {get{return Record.LocationCount>1 ? Visibility.Visible : Visibility.Collapsed;}}
        public string LocationBadgeText {get{return Record.LocationCount.ToString("N0")+" copies";}}
        public Visibility NotesBadgeVisibility {get{return !String.IsNullOrWhiteSpace(Record.Notes) ? Visibility.Visible : Visibility.Collapsed;}}
        public string NotesPreview {get{return String.IsNullOrWhiteSpace(Record.Notes) ? "" : "Note: "+Record.Notes.Replace((char)13,' ').Replace((char)10,' ');}}
        public string HashBadge {get{return Record.Algorithm=="sha256" ? "SHA-256" : "Candidate";}}
        public string DimensionsLabel {get{return Record.PreviewWidth>0 && Record.PreviewHeight>0 ? Record.PreviewWidth+"×"+Record.PreviewHeight : "";}}
        public string SizeLabel {get{return Record.Status=="EVIDENCE" ? "" : (Record.Size/1024.0).ToString("N1")+" KiB";}}
        public string ModifiedLabel {get{return Record.ModifiedTicks>0 && Record.ModifiedTicks<=DateTime.MaxValue.Ticks ? new DateTime(Record.ModifiedTicks,DateTimeKind.Utc).ToLocalTime().ToString("g") : "";}}
        public string ReviewerLabel {get{return Record.Reviewer??"";}}
        public string FullPath { get { return Record.SourcePath; } }
        public string Info { get { return Record.Status=="EVIDENCE" ? "Password-protected original" : Record.OriginalRetained && Record.LocationCount==0 ? "Stored original - no source location" : (Record.OriginalRetained ? "Original retained | " : "")+Record.LocationCount+" location"+(Record.LocationCount==1 ? "" : "s")+" · "+Record.Extension.ToUpperInvariant()+" · "+(Record.Size/1024.0).ToString("0.#",CultureInfo.CurrentCulture)+" KiB"+(Record.SourceIsHistorical ? " · Historical location" : ""); } }
        public string FingerprintLabel { get { return (Record.Algorithm=="sha256" ? "Full SHA-256" : "Quick candidate match")+(Record.AiScore.HasValue ? (Record.AiGemma ? " · Gemma priority " : " · Marqo score ")+Record.AiScore.Value.ToString("P0") : ""); } }
        public string PreviewRetryLabel {get{return String.IsNullOrEmpty(Record.PreviewRetryState) ? "" : "Preview retry: "+Record.PreviewRetryState;}}
        public string Placeholder { get { return imageError ?? (Record.PreviewState=="unavailable" || Record.PreviewState=="protected" ? (String.IsNullOrEmpty(Record.PreviewError) ? "Preview unavailable" : Record.PreviewError) : Record.PreviewBytes==null && Record.PreviewState!="available" ? "Preview pending" : "Loading preview"); } }
        public BitmapSource Image { get { return animationImage??image; } }
        public Visibility PlaceholderVisibility { get { return Image==null ? Visibility.Visible : Visibility.Collapsed; } }
        public bool IsSelected { get { return selected; } set { if(selected!=value){selected=value;Changed("IsSelected");} } }
        public bool IsPending { get { return pending; } set { if(pending!=value){pending=value;Changed("IsPending");Changed("CanSelect");} } }
        public bool CanSelect { get { return !pending; } }
        public bool CanLoadImage { get { return image==null && imageError==null && !ImageQueued && Record.PreviewBytes!=null; } }
        public void SetImage(BitmapSource source,string error) { image=source; imageError=error; ImageQueued=false; Changed("Image");Changed("Placeholder");Changed("PlaceholderVisibility"); }
        internal void SetAnimationImage(BitmapSource source){if(ReferenceEquals(animationImage,source))return;animationImage=source;Changed("Image");Changed("PlaceholderVisibility");}
        public void Update(MediaRow record) {
            bool changed=Record.ContentId!=record.ContentId || Record.PreviewRevision!=record.PreviewRevision;
            if(!changed && record.PreviewBytes==null)record.PreviewBytes=Record.PreviewBytes;
            Record=record;
            if(changed){image=null;animationImage=null;imageError=null;ImageQueued=false;Changed("Image");Changed("PlaceholderVisibility");}
            if(image!=null) Record.PreviewBytes=null; Changed("Name");Changed("PathLabel");Changed("FullPath");Changed("Info");Changed("FingerprintLabel");Changed("Placeholder");Changed("PreviewRetryLabel");Changed("FolderLabel");Changed("TypeLabel");Changed("SizeLabel");Changed("ModifiedLabel");Changed("ReviewerLabel");Changed("IsRenamed");Changed("RenamedBadgeVisibility");Changed("MultiLocationBadgeVisibility");Changed("LocationBadgeText");Changed("NotesBadgeVisibility");Changed("NotesPreview");Changed("HashBadge");Changed("DimensionsLabel"); }
        public event PropertyChangedEventHandler PropertyChanged;
        void Changed(string name) { var handler=PropertyChanged; if(handler!=null) handler(this,new PropertyChangedEventArgs(name)); }
    }
    public sealed class MediaLoadResult {
        public string Kind,Error; public long GroupId,ContentId,Generation,PreviewRevision; public BitmapSource Image;public int SourceWidth,SourceHeight,RequestedSide;
    }
    internal sealed class ThumbnailWork { internal long Id,Content,Generation,PreviewRevision; internal byte[] Bytes; }
    internal sealed class InspectWork { internal MediaRow Row; internal long Generation;internal int Side; }
    public sealed class MediaLoader:IDisposable {
        readonly BoundedQueue<ThumbnailWork> thumbnails=new BoundedQueue<ThumbnailWork>(20,1024*1024);
        readonly BoundedQueue<MediaLoadResult> output=new BoundedQueue<MediaLoadResult>(20,16*1024*1024);
        readonly object inspectGate=new object();InspectWork pendingInspect;MediaLoadResult inspectResult;CancellationTokenSource activeInspect;
        readonly CancellationTokenSource stop=new CancellationTokenSource(); readonly Thread thumbnailThread,inspectThread;
        long latestInspect; volatile bool closing,closed;
        readonly bool privateInspection;
        public bool Closed { get { return closed; } }
        public MediaLoader():this(false){}
        internal MediaLoader(bool independentInspection) { privateInspection=independentInspection;thumbnailThread=new Thread(Thumbnails) { IsBackground=true,Name="MediaReviewer saved thumbnail loader" }; inspectThread=new Thread(Inspect) { IsBackground=true,Name="MediaReviewer source inspector" }; thumbnailThread.Start(); inspectThread.Start(); }
        public bool QueueThumbnail(long id,long generation,byte[] bytes){return QueueThumbnail(id,0,generation,bytes);}
        public bool QueueThumbnail(long id,long content,long generation,byte[] bytes,long previewRevision=0) { if(closing) return false; if(bytes==null || bytes.Length>32768) throw new InvalidDataException("Saved thumbnail exceeds its bounded encoded size"); return thumbnails.TryAdd(new ThumbnailWork { Id=id,Content=content,Generation=generation,Bytes=bytes,PreviewRevision=previewRevision },bytes.Length+128); }
        public bool QueueInspect(MediaRow row,long generation,int side,bool useSavedPoster=false) {
            if(row==null || side<1 || side>InspectionPlan.MaxSide)throw new ArgumentException("Invalid bounded inspection request");
            byte[] saved=useSavedPoster ? row.PreviewBytes : null;
            if(saved!=null && saved.Length>32768 || saved==null &&
                (row.RootPath==null || row.RelativePath==null || row.RootPath.Length+row.RelativePath.Length>65536))
                throw new ArgumentException("Invalid bounded inspection request");
            var snapshot=new MediaRow {GroupId=row.GroupId,ContentId=row.ContentId,RootPath=row.RootPath,RelativePath=row.RelativePath,Size=row.Size,ModifiedTicks=row.ModifiedTicks,FileIdentity=row.FileIdentity,PreviewBytes=saved};
            lock(inspectGate){if(closing)return false;latestInspect=generation;pendingInspect=new InspectWork {Row=snapshot,Generation=generation,Side=side};inspectResult=null;if(activeInspect!=null)activeInspect.Cancel();Monitor.PulseAll(inspectGate);return true;}
        }
        public void CancelInspect(long generation){lock(inspectGate){latestInspect=generation;pendingInspect=null;inspectResult=null;if(activeInspect!=null)activeInspect.Cancel();Monitor.PulseAll(inspectGate);}}
        public bool TryTakeResult(out MediaLoadResult result) {lock(inspectGate){result=inspectResult;inspectResult=null;if(result!=null && result.Generation==latestInspect)return true;}result=output.Take(CancellationToken.None,0);return result!=null; }
        internal static BitmapSource Decode(byte[] bytes,int side) {
            return Decode(bytes,side,CancellationToken.None);
        }
        internal static BitmapSource Decode(byte[] bytes,int side,CancellationToken token) {
            token.ThrowIfCancellationRequested();byte[] converted=null;
            try{
            if(ImageHeader.Detect(bytes,"")=="webp")bytes=converted=Preview.CreateSaved(bytes,token).Bytes;
            int width,height; Preview.Dimensions(bytes,bytes.Length,out width,out height);
            if(width>side || height>side || (long)width*height>InspectionPlan.MaxPixels) throw new InvalidDataException("Saved preview dimensions exceed the bounded display limit");
            using(var stream=new MemoryStream(bytes,false)) {
                var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth=Math.Min(side,width); bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }
            }finally{if(converted!=null)Array.Clear(converted,0,converted.Length);}
        }
        void Post(MediaLoadResult result) { long size=512+(result.Image==null ? 0 : (long)result.Image.PixelWidth*result.Image.PixelHeight*4); output.Add(result,size,stop.Token); }
        void Thumbnails() {
            try { while(!stop.IsCancellationRequested) { ThumbnailWork work=thumbnails.Take(stop.Token,100); if(work==null) continue; var result=new MediaLoadResult { Kind="Thumbnail",GroupId=work.Id,ContentId=work.Content,Generation=work.Generation,PreviewRevision=work.PreviewRevision }; try { result.Image=Decode(work.Bytes,256,stop.Token); } catch(Exception error) { result.Error=error.Message; } Post(result); } } catch(OperationCanceledException) {}
        }
        void Inspect() {
            DecoderHost own=null;
            try{
            while(!stop.IsCancellationRequested){InspectWork work;CancellationTokenSource request;
                lock(inspectGate){if(pendingInspect==null && !closing)Monitor.Wait(inspectGate,100);if(closing)break;work=pendingInspect;pendingInspect=null;if(work==null)continue;request=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);activeInspect=request;}
                var result=new MediaLoadResult {Kind="Inspect",GroupId=work.Row.GroupId,ContentId=work.Row.ContentId,Generation=work.Generation,RequestedSide=work.Side};
                try{
                    if(work.Row.PreviewBytes!=null){
                        result.Image=Decode(work.Row.PreviewBytes,256,request.Token);
                        result.SourceWidth=result.Image.PixelWidth;result.SourceHeight=result.Image.PixelHeight;
                    }else for(int attempt=0;;attempt++){try{
                        string path=Catalog.PreviewSourcePath(work.Row.RootPath,work.Row.RelativePath);
                        PreviewResult preview;
                        if(privateInspection){
                            if(own==null)own=new DecoderHost(Native.RuntimeDirectory);
                            if(Archives.IsPath(path))preview=Archives.WithPreviewFile(path,work.Row.Size,work.Row.ModifiedTicks,work.Row.FileIdentity,request.Token,work.Row.RootPath,
                                (file,stamp)=>own.CreateScoped(file,stamp.Size,stamp.ModifiedTicks,stamp.Identity,work.Side,InspectionPlan.EncodedLimit,request.Token,Native.RuntimeDirectory));
                            else preview=own.CreateScoped(path,work.Row.Size,work.Row.ModifiedTicks,work.Row.FileIdentity,work.Side,InspectionPlan.EncodedLimit,request.Token,work.Row.RootPath);
                        }else preview=Preview.CreateExpectedScoped(path,work.Row.Size,work.Row.ModifiedTicks,work.Row.FileIdentity,work.Side,InspectionPlan.EncodedLimit,request.Token,work.Row.RootPath);
                        request.Token.ThrowIfCancellationRequested();result.Image=Decode(preview.Bytes,work.Side);result.SourceWidth=preview.SourceWidth;result.SourceHeight=preview.SourceHeight;break;
                    }catch(Exception error){if(!FileAccessNative.Transient(error) || attempt>=3)throw;if(request.Token.WaitHandle.WaitOne(attempt==0 ? 250 : attempt==1 ? 1000 : 3000))request.Token.ThrowIfCancellationRequested();}}}
                catch(OperationCanceledException){}catch(Exception error){result.Error=error.Message;}
                finally{lock(inspectGate){if(!closing && !request.IsCancellationRequested && work.Generation==latestInspect)inspectResult=result;activeInspect=null;request.Dispose();}}
            }
            }finally{if(own!=null)own.Dispose();}
        }
        public void Dispose() { if(closed) return; closing=true; stop.Cancel();lock(inspectGate){pendingInspect=null;inspectResult=null;if(activeInspect!=null)activeInspect.Cancel();Monitor.PulseAll(inspectGate);}thumbnails.Close();output.Close();thumbnailThread.Join();inspectThread.Join();closed=true; }
        public void CloseAsync() { if(closing) return; closing=true; new Thread(Dispose) { IsBackground=true,Name="MediaReviewer preview shutdown" }.Start(); }
    }
}
