using System;
using System.IO;
using System.Threading;

namespace MediaReviewer {
    public sealed partial class ReviewerEngine {
        sealed class PlaybackRequest {
            internal readonly string Id=Guid.NewGuid().ToString("N");
            internal readonly CancellationTokenSource Cancel=new CancellationTokenSource();
            internal MediaRow Row;
            internal string Stage="Queued for playback…";
        }
        readonly object playbackGate=new object();
        PlaybackRequest pendingPlayback,activePlayback;
        Thread playbackThread;

        internal string RequestVideoPlayback(MediaRow row) {
            var selected=SourceSelection(row);
            lock(playbackGate){
                if(!Ready || closing || faulted)throw new InvalidOperationException("The database is unavailable.");
                if(pendingPlayback!=null)throw new InvalidOperationException("A video is already queued for playback.");
                if(playbackThread==null){
                    var thread=new Thread(PlaybackMain) {IsBackground=true,Name="MediaReviewer playback preparation"};
                    thread.Start();
                    playbackThread=thread;
                }
                pendingPlayback=new PlaybackRequest {Row=selected};
                Monitor.PulseAll(playbackGate);
                return pendingPlayback.Id;
            }
        }
        internal string VideoStage(string request) {
            lock(playbackGate){
                if(pendingPlayback!=null && pendingPlayback.Id==request)return pendingPlayback.Stage;
                return activePlayback!=null && activePlayback.Id==request ? activePlayback.Stage : null;
            }
        }
        internal void CancelVideoPlayback(string request) {
            lock(playbackGate){
                if(pendingPlayback!=null && pendingPlayback.Id==request){
                    pendingPlayback.Cancel.Dispose();
                    pendingPlayback=null;
                }
                if(activePlayback!=null && activePlayback.Id==request)activePlayback.Cancel.Cancel();
            }
        }
        void PlaybackMain() {
            while(true){
                PlaybackRequest work;
                lock(playbackGate){
                    while(!closing && !faulted && pendingPlayback==null)Monitor.Wait(playbackGate,250);
                    if(closing || faulted)return;
                    work=activePlayback=pendingPlayback;
                    pendingPlayback=null;
                    work.Stage="Checking video source…";
                }
                string playback=null,error=null;
                bool temporary=false;
                try{
                    var token=work.Cancel.Token;
                    token.ThrowIfCancellationRequested();
                    RecordedSource source;
                    using(var db=new SqliteConnection(path,true)){
                        db.Script("PRAGMA query_only=ON; PRAGMA cache_size=-2048; PRAGMA mmap_size=0;");
                        source=CurrentSource(db,work.Row);
                    }
                    token.ThrowIfCancellationRequested();
                    using(var file=OpenRecordedSource(source)){
                        string original=VerifyLaunchable(source,file,true);
                        token.ThrowIfCancellationRequested();
                        string extension=Path.GetExtension(original).TrimStart('.').ToLowerInvariant();
                        temporary=Formats.UsesPlaybackProxy(extension);
                        if(temporary){
                            lock(playbackGate)work.Stage="Preparing offline playback copy…";
                            playback=PlaybackProxy.Create(original,extension,file.Length,token);
                        }else playback=original;
                    }
                }catch(Exception failure){error=failure.GetBaseException().Message;}
                bool deliver;
                lock(playbackGate){
                    deliver=!work.Cancel.IsCancellationRequested && !closing && !faulted;
                    activePlayback=null;
                    work.Cancel.Dispose();
                }
                if(!deliver || error!=null){
                    if(temporary)PlaybackProxy.Release(playback);
                    if(!deliver)continue;
                }
                Respond(new UiResult {RequestId=work.Id,Kind="VideoSource",Success=error==null,Error=error,Value=playback});
            }
        }
        void StopPlayback() {
            Thread thread;
            lock(playbackGate){
                if(pendingPlayback!=null){pendingPlayback.Cancel.Dispose();pendingPlayback=null;}
                if(activePlayback!=null)activePlayback.Cancel.Cancel();
                thread=playbackThread;
                Monitor.PulseAll(playbackGate);
            }
            if(thread!=null)thread.Join();
        }
    }
}
