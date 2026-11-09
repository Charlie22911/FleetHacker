using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
namespace MediaReviewer {
    public static partial class Catalog {
        internal static string SaveStego(SqliteConnection db,long group,long content,string sha,string package,string result,string source){
            var json=new JavaScriptSerializer {MaxJsonLength=1048576,RecursionLimit=32};
            if(sha==null || sha.Length!=64 || !sha.All(Uri.IsHexDigit) || package==null || package.Length!=64 || !package.All(Uri.IsHexDigit) || result==null || result.Length>1000000)throw new InvalidDataException("Invalid analysis result identity or size");
            var value=json.Deserialize<Dictionary<string,object>>(result);
            if(!value.ContainsKey("sha256") || !String.Equals(value["sha256"] as string,sha,StringComparison.Ordinal))throw new InvalidDataException("Analysis fingerprint mismatch");
            string state=value.ContainsKey("status") ? value["status"] as string : null;
            if(!new[]{"completed","partial","failed","unsupported","limited"}.Contains(state))throw new InvalidDataException("Invalid analysis outcome");
            int? index=null;object score;if(value.TryGetValue("suspicionIndex",out score) && score!=null){int n=Convert.ToInt32(score);if(n<0||n>100)throw new InvalidDataException("Invalid suspicion index");index=n;}
            string id=Guid.NewGuid().ToString("N"),at=Now();value["source"]=source;value["groupId"]=group;value["contentId"]=content;value["packageSha256"]=package;value["createdAt"]=at;result=json.Serialize(value);
            PreviewTransaction(db,()=>{
                if(db.Scalar("SELECT 1 FROM review_groups WHERE id=? AND content_id=? AND retired=0 AND status<>'EVIDENCE'",group,content)==null)throw new InvalidOperationException("This item's identity or protection changed; analysis was discarded");
                db.Execute("INSERT INTO stego_runs VALUES(?,?,?,?,?,?,?,?,?)",id,group,content,sha,package,at,state,index.HasValue ? (object)index.Value : null,result);
                StegoFindings.Attach(db,id,group,content,sha,package,value,at);
                db.Execute("UPDATE stego_runs SET result_json=? WHERE id=?",json.Serialize(value),id);
                Bump(db);
            });return id;
        }
    }
    public sealed partial class ReviewerEngine {
        readonly object stegoGate=new object();Thread stegoThread;CancellationTokenSource stegoCancel;OperationProgress stegoProgress;
        volatile bool stegoActive,stegoPolicyActive;
        public bool StegoAnalysisActive {get{return stegoActive;}}
        OperationProgress GetStegoProgress(string id){lock(stegoGate)return stegoProgress!=null && stegoProgress.RequestId==id ? stegoProgress.Copy() : null;}
        bool CancelStego(string id){lock(stegoGate){if(stegoProgress==null || stegoProgress.RequestId!=id)return false;if(stegoCancel!=null){stegoCancel.Cancel();stegoProgress.State="cancelling";stegoProgress.Stage="Stopping scan…";}return true;}}
        void StegoStage(string text,long done,long total){lock(stegoGate){stegoProgress.Stage=text;stegoProgress.Completed=done;stegoProgress.Total=total;stegoProgress.LastProgressUtc=DateTime.UtcNow;}}
        public string RequestStegoScan(){return StartStego(new StegoScanOptions(),null);}
        public string RequestStegoScan(StegoScanOptions options){if(options==null)throw new ArgumentNullException("options");return StartStego(options,null);}
        internal string RequestStegoScan(Func<string,string,CancellationToken,IStegoSession> factory){return StartStego(new StegoScanOptions(),factory);}
        internal string RequestStegoScan(StegoScanOptions options,Func<string,string,CancellationToken,IStegoSession> factory){return StartStego(options,factory);}
        string StartStego(StegoScanOptions options,Func<string,string,CancellationToken,IStegoSession> factory){
            options=options.Snapshot();
            string id=Guid.NewGuid().ToString("N");
            lock(scanGate){
                if(!ready || closing || faulted || reportActive || sourceConnectionBusy || DraftReviewActive || stegoActive || stegoPolicyActive || scan!=null&&!scan.Finished)throw new InvalidOperationException("Wait for the scan or analysis to finish before starting hidden data analysis");
                if(stegoThread!=null && stegoThread.IsAlive)throw new InvalidOperationException("The previous analysis is finishing; try again shortly");
                lock(stegoGate){stegoCancel=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);stegoProgress=new OperationProgress {RequestId=id,Kind="Hidden data scan",State="running",Stage="Getting ready…",Total=null,Unit="items"};stegoActive=true;
                    stegoThread=new Thread(()=>StegoMain(id,options,factory)){IsBackground=true,Name="FleetHacker hidden data scan"};stegoThread.Start();}
            }return id;
        }
        void StegoMain(string request,StegoScanOptions options,Func<string,string,CancellationToken,IStegoSession> factory){
            string error="";long saved=0,skipped=0,attempted=0,suspects=0;CancellationToken token=stegoCancel.Token;
            try{
                string directory;using(var own=Process.GetCurrentProcess())directory=Path.GetDirectoryName(own.MainModule.FileName);
                string package=StegoCompanion.EmbeddedPin();
                using(var reader=new SqliteConnection(path,true)){
                    reader.Script("PRAGMA query_only=ON; PRAGMA cache_size=-8192; PRAGMA mmap_size=0;");
                    long ceiling=Convert.ToInt64(reader.Scalar("SELECT COALESCE(MAX(id),0) FROM review_groups"));
                    WaitWrite(db=>{StegoFindings.SetThreshold(db,options.MinimumConfidencePercent,token);return null;},4096);
                    long total=options.Scope==StegoScanScope.AllUnchecked ? StegoScan.Pending(reader,package,ceiling) : StegoScan.Candidates(reader,package,ceiling,token,options).LongCount();
                    StegoStage(total==0 ? "All photos are already checked." : "Getting ready to check "+total+" photos…",0,total);
                    IStegoSession session=null;
                    try{foreach(long group in StegoScan.Candidates(reader,package,ceiling,token,options)){
                        token.ThrowIfCancellationRequested();
                        if(session==null)session=factory==null ? new StegoSession(directory,package,token) : factory(directory,package,token);
                        StegoStage("Checking photo "+(attempted+1)+" of "+total,attempted,total);attempted++;
                        var identity=reader.Query("SELECT g.content_id,c.algorithm,c.algorithm_version,c.sampling,c.digest FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.id=? AND g.retired=0 AND g.status<>'EVIDENCE'",new object[]{group},1);
                        if(identity.Count!=1){skipped++;continue;}
                        var row=identity[0];long content=(long)row[0];string snapshot=Path.Combine(session.Work,"original-"+attempted+".input"),digest=null,recorded=null;Exception sourceError=null;
                        if((string)row[1]=="sampled-sha256-v1"){try{Verification.RequireRecordedOriginal(reader,group);}catch(InvalidOperationException failure){skipped++;WaitWrite(db=>{Catalog.Log(db,"warning","Hidden data scan","",failure.Message);return null;},70000);continue;}}
                        foreach(var source in Catalog.RecordedSources(reader,group,content,false,token)){
                            try{
                                recorded=Catalog.PreviewSourcePath((string)source[0],(string)source[1]);
                                if((long)source[2]>256L*1024*1024)throw new InvalidDataException("Original exceeds the 256 MiB analysis limit");
                                digest=StegoSource.Snapshot(recorded,(long)source[2],(long)source[3],source[4] as string,(string)source[0],session.Work,snapshot,session.Nonce,token);
                                if((string)row[1]=="sha256" && (long)row[2]==1 && (string)row[3]=="" && !String.Equals(digest,(string)row[4],StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Original fingerprint changed; scan its current contents");
                                sourceError=null;break;
                            }catch(OperationCanceledException){throw;}catch(Exception failure){sourceError=failure;digest=null;if(File.Exists(snapshot))File.Delete(snapshot);}
                        }
                        if(digest==null){skipped++;WaitWrite(db=>{Catalog.Log(db,"warning","Hidden data scan",recorded??"",sourceError==null ? "Original file is not connected." : sourceError.Message);return null;},70000);continue;}
                        try{
                            string result=session.Analyze(snapshot,digest,token);token.ThrowIfCancellationRequested();var serializer=new JavaScriptSerializer {MaxJsonLength=1048576,RecursionLimit=32};var metadata=serializer.Deserialize<Dictionary<string,object>>(result);metadata["scanScope"]=options.Scope.ToString();result=serializer.Serialize(metadata);
                            bool accepted=(bool)WaitWrite(db=>{if(db.Scalar("SELECT 1 FROM review_groups WHERE id=? AND content_id=? AND retired=0 AND status<>'EVIDENCE'",group,content)==null)return false;Catalog.SaveStego(db,group,content,digest,package,result,recorded);return true;},2100000);if(accepted)saved++;else skipped++;
                        }finally{File.Delete(snapshot);}
                        StegoStage("Checked "+saved+" photos",attempted,total);
                    }}finally{if(session!=null)session.Dispose();}
                    suspects=StegoFindings.Count(reader);
                }
            }catch(Exception failure){error=token.IsCancellationRequested ? "Scan stopped. Finished checks are saved." : failure.Message;}
            finally{
                string summary=attempted==0 ? (options.Scope==StegoScanScope.Selected ? "No selected photos are available to check." : "All photos are already checked.") : "Checked "+saved+" photo"+(saved==1 ? "" : "s")+"."+(skipped==0 ? "" : " "+skipped+" could not be checked.");
                if(error.Length==0)summary+=" "+suspects+" suspect photo"+(suspects==1 ? " is" : "s are")+" in Steganography. Automatic confidence threshold: "+options.MinimumConfidencePercent+"%.";
                lock(stegoGate){stegoProgress.State=error.Length==0 ? "complete" : token.IsCancellationRequested ? "cancelled" : "failed";stegoProgress.Stage=error.Length==0 ? summary : error;stegoProgress.Error=error;stegoCancel.Dispose();stegoCancel=null;stegoActive=false;}
                Respond(new UiResult {RequestId=request,Kind="Hidden data scan",Success=error.Length==0,Error=error,Value=summary});
            }
        }
        public string RequestStegoHistory(long group){
            return EnqueueOperation("Photo result",(db,token,progress)=>{
                var rows=db.Query("SELECT s.status,s.suspicion_index,s.result_json FROM stego_runs s JOIN review_groups g ON g.id=s.group_id WHERE s.group_id=? AND g.retired=0 AND g.status<>'EVIDENCE' AND s.content_id=g.content_id AND s.package_sha256=? ORDER BY s.created_at DESC,s.id DESC LIMIT 1",new object[]{group,StegoCompanion.EmbeddedPin()},1);
                if(rows.Count==0)return "No saved result is available for this photo.";
                var row=rows[0];string status=(string)row[0];var value=new JavaScriptSerializer {MaxJsonLength=1048576,RecursionLimit=32}.Deserialize<Dictionary<string,object>>((string)row[2]);
                bool failed=false;object records;if(value.TryGetValue("engines",out records))foreach(var item in (System.Collections.IEnumerable)records){var engine=(Dictionary<string,object>)item;string state=engine["state"] as string;if(new[]{"failed","timed_out","unavailable","cancelled"}.Contains(state))failed=true;}
                var text=new System.Text.StringBuilder();text.AppendLine(StegoScan.Outcome(status,row[1],failed)).AppendLine();
                if(row[1]!=null && Convert.ToInt64(row[1])>=80)text.AppendLine("An embedded file was found in this photo.");
                else if(row[1]!=null && Convert.ToInt64(row[1])>0)text.AppendLine("The scan found possible signs of hidden data. Review this photo before making a decision.");
                else if(failed)text.AppendLine("The scan could not finish checking this photo.");
                else if(status=="unsupported")text.AppendLine("This photo type is not supported. JPEG, PNG and BMP photos can be checked.");
                else if(status=="limited")text.AppendLine("This photo is larger than the scan can process.");
                else text.AppendLine("The checks that ran found no signs of hidden data.");
                if(failed)text.AppendLine("Some checks could not finish. Run the scan again to retry this photo.");
                if(status=="partial" || failed)text.AppendLine("This was a limited check. Some hidden data may not be detected.");
                text.AppendLine().AppendLine("A scan result does not change the photo's review decision.");object source;if(value.TryGetValue("source",out source))text.AppendLine().AppendLine("File: "+source);return text.ToString();
            });
        }
        public string RequestStegoJson(long group){return EnqueueOperation("Export result",(db,token,progress)=>(string)db.Scalar("SELECT s.result_json FROM stego_runs s JOIN review_groups g ON g.id=s.group_id WHERE s.group_id=? AND g.retired=0 AND g.status<>'EVIDENCE' AND s.content_id=g.content_id AND s.package_sha256=? ORDER BY s.created_at DESC,s.id DESC LIMIT 1",group,StegoCompanion.EmbeddedPin())??"{}");}
        void StopStegoAnalysis(){Thread thread;lock(stegoGate){if(stegoCancel!=null)stegoCancel.Cancel();thread=stegoThread;}if(thread!=null && thread!=Thread.CurrentThread)thread.Join();}
    }
}
