using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
namespace MediaReviewer {
    static class StegoSmoke {
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        static void Wait(Func<bool> condition,int seconds=90){var time=Stopwatch.StartNew();while(!condition()){if(time.Elapsed.TotalSeconds>seconds)throw new TimeoutException("Steganography integration timed out");Thread.Sleep(25);}}
        internal static int Run(string directory,string fixture){
            Directory.CreateDirectory(directory);string root=Directory.CreateDirectory(Path.Combine(directory,"原始照片")).FullName;
            string original=Path.Combine(root,Path.GetFileName(fixture));File.Copy(fixture,original);
            string digest;using(var input=File.OpenRead(original))using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();
            string dbpath=Path.Combine(directory,"analysis.sqlite");long group,content;
            string companionDirectory;using(var own=Process.GetCurrentProcess())companionDirectory=Path.GetDirectoryName(own.MainModule.FileName);
            int unassigned=0;var cleanup=Stopwatch.StartNew();bool isolated=false;
            try{using(var session=new StegoSession(companionDirectory,StegoCompanion.EmbeddedPin(),CancellationToken.None,(job,process)=>{unassigned=process.Id;throw new InvalidOperationException("Simulated isolation failure");})){} }
            catch(InvalidOperationException){isolated=true;}
            Check(isolated && unassigned!=0 && cleanup.Elapsed.TotalSeconds<15,"Isolation failure must stop an unassigned worker promptly");
            try{using(var owned=Process.GetProcessById(unassigned))Check(owned.HasExited,"Unassigned worker must not remain running");}catch(ArgumentException){}
            using(var engine=ReviewerEngine.Open(dbpath,true)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
                string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1});Wait(()=>engine.Status.ScanId==scan&&!engine.Status.RunningScan);
                using(var db=new SqliteConnection(dbpath,true)){var row=db.Query("SELECT id,content_id FROM review_groups WHERE retired=0",new object[0],2);Check(row.Count==1,"One fixture should produce one group");group=(long)row[0][0];content=(long)row[0][1];}
                if(Path.GetFileName(fixture)=="lsb.png"){
                    string cancelled=engine.RequestStegoScan();
                    Wait(()=>engine.GetOperationProgress(cancelled).Stage.StartsWith("Checking photo"),120);Thread.Sleep(1500);engine.CancelOperation(cancelled);UiResult cancellation=null;
                    Wait(()=>{UiResult item;while(engine.TryTakeResult(out item))if(item.RequestId==cancelled)cancellation=item;return cancellation!=null;},60);
                    Check(!cancellation.Success && engine.GetOperationProgress(cancelled).State=="cancelled" && !engine.StegoAnalysisActive,"Cancellation must stop the analysis session and return control: "+cancellation.Error+" "+cancellation.Value);
                    using(var db=new SqliteConnection(dbpath,true))Check(Convert.ToInt64(db.Scalar("SELECT count(*) FROM stego_runs"))==0,"Cancelled unfinished analysis must not be saved as a completed result");
                }
                string request=engine.RequestStegoScan();UiResult result=null;bool overlap=false;
                try{engine.RequestReport(new ReportOptions {Destination=Path.Combine(directory,"overlap.csv")});}catch(InvalidOperationException){overlap=true;}
                Check(overlap,"Reports must not start during hidden data analysis");
                Wait(()=>{UiResult item;while(engine.TryTakeResult(out item))if(item.RequestId==request)result=item;return result!=null;},900);
                Check(result.Success,result.Error);Check(result.Value.Contains("Checked 1 photo"),result.Value);
            }
            using(var db=new SqliteConnection(dbpath,false)){
                Check((string)db.Scalar("SELECT status FROM review_groups WHERE id=?",group)=="TO_REVIEW","Analysis must not change a compliance decision");
                var rows=db.Query("SELECT original_sha256,status,suspicion_index,result_json FROM stego_runs WHERE group_id=?",new object[]{group},2);
                Check(rows.Count==1 && (string)rows[0][0]==digest,"Results must be bound to original-byte SHA-256");
                Check((string)rows[0][1]=="partial","Incomplete Aletheia coverage must be stored as partial");
                Check(((string)rows[0][3]).Contains("Original Aletheia A-model mean") && !((string)rows[0][3]).Contains("\"state\": \"failed\""),"Original Aletheia inference must run successfully");
                if(Path.GetFileName(fixture)=="lsb.png" || Path.GetFileName(fixture)=="appended.jpg" || Path.GetFileName(fixture)=="a-lsb-png.png"){
                    Check(Convert.ToInt32(rows[0][2])==80 && ((string)rows[0][3]).Contains("validated_payload"),"The generated hidden file must be recovered and validated");
                    Check(StegoFindings.Count(db)==1 && ((string)db.Scalar("SELECT notes FROM review_groups WHERE id=?",group)).Contains("Confidence: Strong evidence") && ((string)rows[0][3]).Contains("reviewNote"),"Actual detector findings must populate Steganography and save confidence notes");
                    if(Path.GetFileName(fixture)=="a-lsb-png.png")Check(((string)db.Scalar("SELECT notes FROM review_groups WHERE id=?",group)).Contains("PNG image hidden in image bits"),"The recovered image must be identified as PNG in its saved review notes");
                }
                string json=(string)rows[0][3];db.Execute("UPDATE review_groups SET status='EVIDENCE' WHERE id=?",group);bool blocked=false;
                try{Catalog.SaveStego(db,group,content,digest,new string('a',64),json,original);}catch(InvalidOperationException){blocked=true;}
                Check(blocked && Convert.ToInt64(db.Scalar("SELECT count(*) FROM stego_runs"))==1,"A protected item must reject an analysis commit");
                db.Execute("UPDATE review_groups SET status='TO_REVIEW' WHERE id=?",group);blocked=false;
                try{Catalog.SaveStego(db,group,content,new string('0',64),new string('a',64),json,original);}catch(InvalidDataException){blocked=true;}
                Check(blocked,"Mismatched fingerprints must reject a result");
                db.Execute("DROP TABLE historical_evidence");db.Execute("DROP TABLE file_properties");db.Execute("DROP TABLE stego_flags");db.Execute("DROP TABLE stego_runs");db.Execute("PRAGMA user_version=13");Catalog.Initialize(db,false);
                Check(Convert.ToInt64(db.Scalar("PRAGMA user_version"))==Catalog.SchemaVersion && Convert.ToInt64(db.Scalar("SELECT count(*) FROM stego_runs"))==0 && (string)db.Scalar("SELECT status FROM review_groups WHERE id=?",group)=="TO_REVIEW","Schema 13 upgrade must add history and preserve decisions");
            }
            using(var input=File.OpenRead(original))using(var hash=SHA256.Create())Check(BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant()==digest,"Original photo must remain unchanged");
            Console.WriteLine("Actual engine analysis, original fingerprint, immutable review status, partial coverage, saved findings and protected/stale commit checks passed.");return 0;
        }
    }
}
