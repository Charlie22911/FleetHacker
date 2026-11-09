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
                Bump(db);
            });return id;
        }
    }
    public sealed partial class ReviewerEngine {
        readonly object stegoGate=new object();Thread stegoThread;CancellationTokenSource stegoCancel;OperationProgress stegoProgress;
        volatile bool stegoActive;
        public bool StegoAnalysisActive {get{return stegoActive;}}
        OperationProgress GetStegoProgress(string id){lock(stegoGate)return stegoProgress!=null && stegoProgress.RequestId==id ? stegoProgress.Copy() : null;}
        bool CancelStego(string id){lock(stegoGate){if(stegoProgress==null || stegoProgress.RequestId!=id)return false;if(stegoCancel!=null){stegoCancel.Cancel();stegoProgress.State="cancelling";stegoProgress.Stage="Stopping hidden data analysis";}return true;}}
        void StegoStage(string text,long done,long total){lock(stegoGate){stegoProgress.Stage=text;stegoProgress.Completed=done;stegoProgress.Total=total;stegoProgress.LastProgressUtc=DateTime.UtcNow;}}
        public string RequestStegoAnalysis(long[] groups){
            if(groups==null || groups.Length<1 || groups.Length>200 || groups.Any(g=>g<1))throw new ArgumentException("Select 1 through 200 ordinary items for analysis");
            var ids=groups.Distinct().ToArray();string id=Guid.NewGuid().ToString("N");
            lock(scanGate){
                if(!ready || closing || faulted || reportActive || sourceConnectionBusy || DraftReviewActive || stegoActive || scan!=null&&!scan.Finished)throw new InvalidOperationException("Wait for the scan or analysis to finish before starting hidden data analysis");
                if(stegoThread!=null && stegoThread.IsAlive)throw new InvalidOperationException("The previous analysis is finishing; try again shortly");
                lock(stegoGate){stegoCancel=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);stegoProgress=new OperationProgress {RequestId=id,Kind="Hidden data analysis",State="running",Stage="Preparing analysis package",Total=ids.Length,Unit="items"};stegoActive=true;
                    stegoThread=new Thread(()=>StegoMain(id,ids)){IsBackground=true,Name="FleetHacker hidden data analysis"};stegoThread.Start();}
            }return id;
        }
        void StegoMain(string request,long[] groups){
            string error="";int saved=0,skipped=0;var notes=new List<string>();CancellationToken token=stegoCancel.Token;
            try{
                string directory;using(var own=Process.GetCurrentProcess())directory=Path.GetDirectoryName(own.MainModule.FileName);
                string package=StegoCompanion.EmbeddedPin();
                using(var reader=new SqliteConnection(path,true))using(var session=new StegoSession(directory,package,token)){
                    reader.Script("PRAGMA query_only=ON; PRAGMA cache_size=-8192; PRAGMA mmap_size=0;");
                    for(int i=0;i<groups.Length;i++){
                        token.ThrowIfCancellationRequested();long group=groups[i];StegoStage("Analyzing item "+(i+1)+" of "+groups.Length,i,groups.Length);
                        var identity=reader.Query("SELECT g.content_id,c.algorithm,c.algorithm_version,c.sampling,c.digest FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.id=? AND g.retired=0 AND g.status<>'EVIDENCE'",new object[]{group},1);
                        if(identity.Count!=1){skipped++;notes.Add("Item "+group+": unavailable or protected");continue;}
                        var row=identity[0];long content=(long)row[0];string snapshot=Path.Combine(session.Work,"original-"+i+".input"),digest=null,recorded=null;Exception sourceError=null;
                        foreach(var source in Catalog.RecordedSources(reader,group,content,false,token)){
                            try{
                                recorded=Catalog.PreviewSourcePath((string)source[0],(string)source[1]);
                                if((long)source[2]>256L*1024*1024)throw new InvalidDataException("Original exceeds the 256 MiB analysis limit");
                                digest=StegoSource.Snapshot(recorded,(long)source[2],(long)source[3],source[4] as string,(string)source[0],session.Work,snapshot,session.Nonce,token);
                                if((string)row[1]=="sha256" && (long)row[2]==1 && (string)row[3]=="" && !String.Equals(digest,(string)row[4],StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Original fingerprint changed; scan its current contents");
                                sourceError=null;break;
                            }catch(OperationCanceledException){throw;}catch(Exception failure){sourceError=failure;digest=null;if(File.Exists(snapshot))File.Delete(snapshot);}
                        }
                        if(digest==null){skipped++;notes.Add("Item "+group+": "+(sourceError==null ? "No current original is connected" : sourceError.Message));continue;}
                        try{
                            string result=session.Analyze(snapshot,digest,token);token.ThrowIfCancellationRequested();
                            WaitWrite(db=>Catalog.SaveStego(db,group,content,digest,package,result,recorded),2100000);saved++;
                        }finally{File.Delete(snapshot);}
                        StegoStage("Saved "+saved+" analysis result(s)",i+1,groups.Length);
                    }
                }
            }catch(Exception failure){error=token.IsCancellationRequested ? "Hidden data analysis cancelled. Completed results were retained." : failure.Message;}
            finally{
                string summary="Saved "+saved+" analysis result(s); "+skipped+" skipped. Compliance decisions were unchanged.";
                if(notes.Count>0)summary+="\n"+String.Join("\n",notes);
                lock(stegoGate){stegoProgress.State=error.Length==0 ? "complete" : token.IsCancellationRequested ? "cancelled" : "failed";stegoProgress.Stage=error.Length==0 ? summary : error;stegoProgress.Error=error;stegoCancel.Dispose();stegoCancel=null;stegoActive=false;}
                Respond(new UiResult {RequestId=request,Kind="Hidden data analysis",Success=error.Length==0,Error=error,Value=summary});
            }
        }
        public string RequestStegoHistory(long group){
            return EnqueueOperation("Hidden data results",(db,token,progress)=>{
                var rows=db.Query("SELECT s.created_at,s.status,s.suspicion_index,s.original_sha256,s.package_sha256,s.result_json FROM stego_runs s JOIN review_groups g ON g.id=s.group_id WHERE s.group_id=? AND g.retired=0 AND g.status<>'EVIDENCE' AND s.content_id=g.content_id ORDER BY s.created_at DESC,s.id DESC LIMIT 10",new object[]{group},10);
                if(rows.Count==0)return "No saved analysis for this item's current identity.";
                var text=new System.Text.StringBuilder();var json=new JavaScriptSerializer {MaxJsonLength=1048576,RecursionLimit=32};
                foreach(var row in rows){
                    text.AppendLine((string)row[0]+" | "+row[1]+" | suspicion index: "+(row[2]??"unavailable"));
                    text.AppendLine("Original SHA-256: "+row[3]);
                    var value=json.Deserialize<Dictionary<string,object>>((string)row[5]);object engineRecords;
                    if(value.TryGetValue("engines",out engineRecords))foreach(var item in (System.Collections.IEnumerable)engineRecords){var engine=(Dictionary<string,object>)item;text.AppendLine("  "+engine["id"]+": "+engine["state"]+(engine.ContainsKey("reason") ? " - "+engine["reason"] : ""));}
                    object findings;if(value.TryGetValue("findings",out findings))foreach(var item in ((System.Collections.IEnumerable)findings).Cast<Dictionary<string,object>>().Take(50))text.AppendLine("  "+item["engine"]+" / "+item["method"]+": "+item["explanation"]);
                    text.AppendLine("Export JSON for raw detector output, configuration, scores and coverage warnings.").AppendLine();
                }return text.ToString();
            });
        }
        public string RequestStegoJson(long group){return EnqueueOperation("Hidden data JSON",(db,token,progress)=>(string)db.Scalar("SELECT s.result_json FROM stego_runs s JOIN review_groups g ON g.id=s.group_id WHERE s.group_id=? AND g.retired=0 AND g.status<>'EVIDENCE' AND s.content_id=g.content_id ORDER BY s.created_at DESC,s.id DESC LIMIT 1",group)??"{}");}
        void StopStegoAnalysis(){Thread thread;lock(stegoGate){if(stegoCancel!=null)stegoCancel.Cancel();thread=stegoThread;}if(thread!=null && thread!=Thread.CurrentThread)thread.Join();}
    }
}
