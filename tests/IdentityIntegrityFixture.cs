using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using MediaReviewer;

// Compiled separately by IdentityIntegrity.ps1; never included in the application.
static class IdentityIntegrityFixture {
    static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/3Z8AAAAASUVORK5CYII=");
    static string root;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Wait(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>50)throw new Exception("Identity regression timed out");Thread.Sleep(10);}}
    static UiResult Result(ReviewerEngine engine,string id,bool success=true){UiResult found=null;Wait(()=>{UiResult r;while(engine.TryTakeResult(out r))if(r.RequestId==id)found=r;return found!=null;});Check(found.Success==success,found.Error??"Operation unexpectedly succeeded");return found;}
    static string FileAt(string directory,string name,int length=2*1024*1024){Directory.CreateDirectory(directory);string path=Path.Combine(directory,name);byte[] bytes=new byte[length];Array.Copy(Png,bytes,Png.Length);File.WriteAllBytes(path,bytes);return path;}
    static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    // Independent fixture construction for identities written by earlier releases.
    static FileRecord Legacy(string path,string relative){
        byte[] bytes=File.ReadAllBytes(path);var p=new FingerprintPolicy {Mode="Sampled",ThresholdBytes=0};
        using(var sha=SHA256.Create()){
            Identity.AddText(sha,"MCR-SAMPLED-SHA256-V1\0"+bytes.LongLength.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\0");
            foreach(var range in Identity.Ranges(bytes.LongLength,p)){Identity.AddText(sha,range[0]+":"+range[1]+";");sha.TransformBlock(bytes,(int)range[0],(int)(range[1]-range[0]),bytes,(int)range[0]);}
            sha.TransformFinalBlock(new byte[0],0,0);
            using(var file=System.IO.File.OpenRead(path)){var stamp=FileAccessNative.Stamp(file);return new FileRecord {RelativePath=relative,Name=Path.GetFileName(relative),Extension="png",Size=stamp.Size,ModifiedTicks=stamp.ModifiedTicks,FileIdentity=stamp.Identity,Algorithm="sampled-sha256-v1",AlgorithmVersion=1,Sampling=p.Parameters,Hash="q2:"+BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant()};}
        }
    }
    static void Hashes(){
        string first=FileAt(Path.Combine(root,"hash"),"a.png"),second=Path.Combine(Path.GetDirectoryName(first),"b.png");
        byte[] bytes=File.ReadAllBytes(first);bytes[150000]=1;File.WriteAllBytes(second,bytes);
        Check(Legacy(first,"a.png").Hash==Legacy(second,"b.png").Hash,"Fixture mutation must lie outside every legacy sample window");
        var oldPolicy=new FingerprintPolicy {Mode="Sampled",ThresholdBytes=0};
        var a=Identity.Read(first,oldPolicy,CancellationToken.None);var b=Identity.Read(second,oldPolicy,CancellationToken.None);
        Check(a.Algorithm=="sha256" && a.Sampling=="" && a.Hash==Digest(File.ReadAllBytes(first)) && a.BytesRead==a.Size,"A requested legacy policy generated an incomplete new fingerprint");
        Check(b.Hash==Digest(bytes) && a.Hash!=b.Hash,"Files with identical legacy sample windows must have different complete identities");
        var restored=ScanOptions.FromJson("{\"RootPath\":\""+Path.GetDirectoryName(first).Replace("\\","\\\\")+"\",\"FingerprintPolicy\":{\"Mode\":\"Sampled\",\"ThresholdBytes\":0}}");
        Check(restored.FingerprintPolicy.Mode=="Full" && new ScanOptions().FingerprintPolicy.Mode=="Full","New and resumed scan policies must use complete SHA-256");
    }
    static void Scans(){
        string folder=Path.Combine(root,"scan");FileAt(folder,"one.png");FileAt(folder,"two.png");
        byte[] bytes=File.ReadAllBytes(Path.Combine(folder,"two.png"));bytes[150000]=1;File.WriteAllBytes(Path.Combine(folder,"two.png"),bytes);
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"scan.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
            string scan=engine.StartScan(new ScanOptions {RootPath=folder,WorkerCount=1,Extensions=new[]{"png"},FingerprintPolicy=new FingerprintPolicy {Mode="Sampled",ThresholdBytes=0}});Wait(()=>!engine.Status.RunningScan);
            var rows=Result(engine,engine.RequestPage(new PageRequest {Generation=1})).Page.Rows;
            Check(rows.Count==2 && rows.All(x=>x.Algorithm=="sha256"),"New scan retained sampled identities or combined a sampled collision");
            bytes[150000]=2;File.WriteAllBytes(Path.Combine(folder,"resumed.png"),bytes);
            engine.WaitWrite(db=>{string json=(string)db.Scalar("SELECT config_json FROM scans WHERE id=?",scan);json=json.Replace("\"Mode\":\"Full\"","\"Mode\":\"Sampled\"");db.Execute("UPDATE scans SET state='cancelled',completed_at=NULL,config_json=? WHERE id=?",json,scan);db.Execute("UPDATE scan_users SET state='pending' WHERE scan_id=? AND ordinal=0",scan);db.Execute("INSERT INTO jobs(scan_id,user_ordinal,kind,path,path_key,state) VALUES(?,0,'file','resumed.png','RESUMED.PNG','pending')",scan);db.Execute("INSERT OR REPLACE INTO settings VALUES('ui.v1.full_hash','0')");return null;});
            engine.ResumeScan(scan,1);Wait(()=>!engine.Status.RunningScan);rows=Result(engine,engine.RequestPage(new PageRequest {Generation=2})).Page.Rows;
            Check(rows.Count==3 && rows.All(x=>x.Algorithm=="sha256") && String.IsNullOrEmpty(engine.Status.Error),"A resumed sampled manifest or saved preference generated a new sampled identity");
            Check(ScanOptions.FromJson((string)engine.WaitWrite(db=>db.Scalar("SELECT config_json FROM scans WHERE id=?",scan))).FingerprintPolicy.Mode=="Full","Resumed policy was not normalized");
        }
    }
    static long Seed(ReviewerEngine engine,string folder,string name,bool legacy){
        string path=Path.Combine(folder,name);FileRecord item=legacy ? Legacy(path,name) : Identity.Read(path,FingerprintPolicy.Full(),CancellationToken.None);
        item.RelativePath=name;item.Name=name;item.Extension="png";
        return (long)engine.WaitWrite(db=>{item.RootId=Catalog.Root(db,folder);return (object)Catalog.Publish(db,item);});
    }
    static void Reconnects(){
        string original=Path.Combine(root,"original"),replacement=Path.Combine(root,"replacement");
        FileAt(original,"full.png");FileAt(original,"legacy.png");Directory.CreateDirectory(replacement);
        foreach(string file in Directory.GetFiles(original))File.Copy(file,Path.Combine(replacement,Path.GetFileName(file)));
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"reconnect.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);Seed(engine,original,"full.png",false);long legacy=Seed(engine,original,"legacy.png",true);
            Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),GroupIds=new[]{legacy},Status="COMPLIANT",Notes="Original review"}));
            string batch=engine.RequestReconnectAll(replacement);Result(engine,batch);
            var state=Result(engine,engine.RequestReconnectAllStatus(batch)).Reconnection;
            Check(state.ConnectedRoots==0 && state.Candidates==1 && state.Full==1,"Mixed reconnect root was automatically accepted or lost pending result counts");
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM root_source_heads"))==0,"Mixed reconnect changed the active root binding");
            string check=(string)engine.WaitWrite(db=>db.Scalar("SELECT id FROM source_connections WHERE state='ready'"));Check(check!=null,"Mixed reconnect must retain an explicit Sources acceptance route");
            Result(engine,engine.RequestResumeReconnectAll(batch));Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM root_source_heads"))==0,"Resuming completed review work silently applied it");
            Result(engine,engine.RequestApplySourceConnection(check,true));
            Result(engine,engine.RequestFullVerification(legacy,null),false);
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.id=? AND g.status='COMPLIANT' AND c.algorithm='sampled-sha256-v1'",legacy))==1,"Replacement candidate inherited or rewrote the old reviewed identity");
        }
        string clean=Path.Combine(root,"clean");FileAt(clean,"full.png");
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"clean.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);Seed(engine,clean,"full.png",false);string batch=engine.RequestReconnectAll(clean);Result(engine,batch);
            Check(Result(engine,engine.RequestReconnectAllStatus(batch)).Reconnection.ConnectedRoots==1,"Entirely full-verified root did not connect");
        }
    }
    static void OriginalVerification(){
        string folder=Path.Combine(root,"available-original");FileAt(folder,"original.png");
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"available-original.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);long group=Seed(engine,folder,"original.png",true);
            Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),GroupIds=new[]{group},Status="COMPLIANT",Notes="Keep my review"}));
            var preview=Result(engine,engine.RequestFullVerification(group,null)).IdentityPreview;
            Check(preview!=null && preview.CanApply,"Accessible recorded original did not produce a migration preview");
            Result(engine,engine.RequestIdentityResolution(preview,Guid.NewGuid().ToString("N")));
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.id=? AND c.algorithm='sha256' AND g.notes='Keep my review' AND g.status='COMPLIANT'",group))==1,"Verified original migration lost its notes or decision");
        }
    }
    static void ReboundCandidate(){
        string original=Path.Combine(root,"rebound-original"),replacement=Path.Combine(root,"rebound-replacement");string first=FileAt(original,"original.png");Directory.CreateDirectory(replacement);
        byte[] bytes=File.ReadAllBytes(first);bytes[150000]=5;File.WriteAllBytes(Path.Combine(replacement,"original.png"),bytes);
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"rebound.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);long group=Seed(engine,original,"original.png",true);
            Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),GroupIds=new[]{group},Status="COMPLIANT",Notes="Original only"}));
            string rootId=(string)engine.WaitWrite(db=>db.Scalar("SELECT root_id FROM review_groups WHERE id=?",group));
            string check=engine.RequestRootRemap(rootId,replacement,false);Result(engine,check);Result(engine,engine.RequestApplySourceConnection(check,true));
            Result(engine,engine.RequestFullVerification(group,null),false);
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM identity_runs WHERE state='published'"))==0,"Replacement sampled collision was published as the original");
        }
    }
    static void ProblemRoots(){
        foreach(bool missing in new[]{true,false}){
            string name=missing ? "missing" : "failed",original=Path.Combine(root,name+"-original"),replacement=Path.Combine(root,name+"-replacement");
            FileAt(original,"match.png");string different=FileAt(original,"problem.png");Directory.CreateDirectory(replacement);File.Copy(Path.Combine(original,"match.png"),Path.Combine(replacement,"match.png"));
            if(!missing){byte[] changed=File.ReadAllBytes(different);changed[1000]=9;File.WriteAllBytes(Path.Combine(replacement,"problem.png"),changed);}
            using(var engine=ReviewerEngine.Open(Path.Combine(root,name+".sqlite"),true)){
                Wait(()=>engine.Ready||engine.Faulted);Seed(engine,original,"match.png",false);Seed(engine,original,"problem.png",false);
                string batch=engine.RequestReconnectAll(replacement);Result(engine,batch);var state=Result(engine,engine.RequestReconnectAllStatus(batch)).Reconnection;
                Check(state.ConnectedRoots==0 && state.CompletedRoots==1 && (missing ? state.Missing==1 : state.Failed==1),"A problem root was autoapplied or its terminal counts were lost: "+name);
                string pending=(string)engine.WaitWrite(db=>db.Scalar("SELECT id FROM source_connections WHERE state='ready'"));Check(pending!=null,"Problem root has no preserved check: "+name);
                if(missing)Result(engine,engine.RequestApplySourceConnection(pending,false),false);
                else Result(engine,engine.RequestApplySourceConnection(pending,true),false);
                Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM root_source_heads"))==0,"A failed or unaccepted incomplete check changed the source binding");
            }
        }
    }
    static void LegacySafety(){
        string folder=Path.Combine(root,"legacy-safety");FileAt(folder,"changed.png");FileAt(folder,"protected.png",2*1024*1024+1);FileAt(folder,"offline.png",2*1024*1024+2);
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"legacy-safety.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);long changed=Seed(engine,folder,"changed.png",true),protectedGroup=Seed(engine,folder,"protected.png",true),offline=Seed(engine,folder,"offline.png",true);
            Check(changed!=protectedGroup && changed!=offline,"Legacy safety fixtures must have separate candidate identities");
            string file=Path.Combine(folder,"changed.png");byte[] bytes=File.ReadAllBytes(file);bytes[150000]=2;File.WriteAllBytes(file,bytes);File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddMinutes(3));
            Result(engine,engine.RequestFullVerification(changed,null),false);
            File.Delete(Path.Combine(folder,"offline.png"));Result(engine,engine.RequestFullVerification(offline,null),false);
            engine.WaitWrite(db=>{db.Execute("UPDATE review_groups SET status='EVIDENCE',notes='Protected original' WHERE id=?",protectedGroup);return null;});
            Result(engine,engine.RequestFullVerification(protectedGroup,null),false);
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM review_groups WHERE id=? AND status='EVIDENCE' AND notes='Protected original'",protectedGroup))==1,"Legacy safety checks changed a protected review");
        }
    }
    static void SharedPendingRoot(){
        string fullRoot=Path.Combine(root,"shared-full"),legacyRoot=Path.Combine(root,"shared-legacy"),replacement=Path.Combine(root,"shared-replacement");FileAt(fullRoot,"full.png");FileAt(legacyRoot,"legacy.png");Directory.CreateDirectory(replacement);
        File.Copy(Path.Combine(fullRoot,"full.png"),Path.Combine(replacement,"full.png"));File.Copy(Path.Combine(legacyRoot,"legacy.png"),Path.Combine(replacement,"legacy.png"));
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"shared.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);Seed(engine,fullRoot,"full.png",false);Seed(engine,legacyRoot,"legacy.png",true);
            string batch=engine.RequestReconnectAll(replacement);Result(engine,batch);string check=(string)engine.WaitWrite(db=>db.Scalar("SELECT id FROM source_connections WHERE state='ready'"));
            Result(engine,engine.RequestApplySourceConnection(check,true));Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM root_source_heads"))==2,"Explicit pending acceptance must support separate roots sharing a checked reconnect folder");
        }
    }
    static void ReviewedConflict(){
        string folder=Path.Combine(root,"review-conflict");string original=FileAt(folder,"legacy.png");File.Copy(original,Path.Combine(folder,"verified.png"));
        using(var engine=ReviewerEngine.Open(Path.Combine(root,"review-conflict.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);long legacy=Seed(engine,folder,"legacy.png",true),full=Seed(engine,folder,"verified.png",false);
            Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),GroupIds=new[]{legacy},Status="COMPLIANT",Notes="Original sampled review"}));
            // Construct a previously saved conflicting review without initiating a new
            // protected-original capture, which is a separate vault workflow.
            engine.WaitWrite(db=>{db.Execute("UPDATE review_groups SET status='NON_COMPLIANT',notes='Conflicting verified review' WHERE id=?",full);return null;});
            var preview=Result(engine,engine.RequestFullVerification(legacy,null)).IdentityPreview;Check(preview.CanApply && preview.HasConflicts,"Conflicting reviewed groups were not exposed for explicit resolution");
            Result(engine,engine.RequestIdentityResolution(preview,Guid.NewGuid().ToString("N")),false);
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM review_groups WHERE (id=? AND status='COMPLIANT' AND notes='Original sampled review') OR (id=? AND status='NON_COMPLIANT' AND notes='Conflicting verified review')",legacy,full))==2,"Conflict handling rewrote a reviewed decision");
        }
    }
    static string LargeFile(string folder,string name,int mebibytes,byte identity){
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,name);var buffer=new byte[1024*1024];Array.Copy(Png,buffer,Png.Length);buffer[buffer.Length-1]=identity;
        using(var file=File.Create(path)){file.Write(buffer,0,buffer.Length);Array.Clear(buffer,0,buffer.Length);for(int i=1;i<mebibytes;i++)file.Write(buffer,0,buffer.Length);}return path;
    }
    static void CancelResume(){
        string folder=Path.Combine(root,"cancel-resume"),database=Path.Combine(root,"cancel-resume.sqlite");for(int i=0;i<5;i++)LargeFile(folder,i+".png",32,(byte)i);FileAt(folder,"legacy.png");string batch;
        using(var engine=ReviewerEngine.Open(database,true)){
            Wait(()=>engine.Ready||engine.Faulted);for(int i=0;i<5;i++)Seed(engine,folder,i+".png",false);Seed(engine,folder,"legacy.png",true);
            batch=engine.RequestReconnectAll(folder);Wait(()=>(long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM source_connection_files"))>=1);
            var watch=Stopwatch.StartNew();engine.CancelOperation(batch);var result=Result(engine,batch,false);watch.Stop();
            File.WriteAllText(Path.Combine(root,"reconnect-cancel-ms.txt"),watch.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),new System.Text.UTF8Encoding(false));
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM root_source_heads"))==0,"Cancellation committed an unchecked mixed root");
        }
        using(var engine=ReviewerEngine.Open(database,false)){
            Wait(()=>engine.Ready||engine.Faulted);Result(engine,engine.RequestResumeReconnectAll(batch));var state=Result(engine,engine.RequestReconnectAllStatus(batch)).Reconnection;
            Check(state.ConnectedRoots==0 && state.CompletedRoots==1 && state.Full==5 && state.Candidates==1,"Interrupted mixed root was accepted or lost its durable counts on restart/resume");
            Check((long)engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM source_connections WHERE state='ready'"))==1,"Restart/resume lost the pending acceptance route");
        }
    }
    static void Benchmark(){
        string folder=Path.Combine(root,"full-hash-benchmark");long size=4L*64*1024*1024;for(int i=0;i<4;i++)LargeFile(folder,i+".png",64,(byte)i);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();using(var process=Process.GetCurrentProcess())using(var engine=ReviewerEngine.Open(Path.Combine(root,"benchmark.sqlite"),true)){
            Wait(()=>engine.Ready||engine.Faulted);process.Refresh();long before=process.PrivateMemorySize64,peak=before,retained=0;int queue=0,pings=0;double writerMax=0;var watch=Stopwatch.StartNew();
            engine.StartScan(new ScanOptions {RootPath=folder,WorkerCount=2,Extensions=new[]{"png"}});
            while(engine.Status.RunningScan){process.Refresh();peak=Math.Max(peak,process.PrivateMemorySize64);var status=engine.Status;retained=Math.Max(retained,status.RetainedBytes);queue=Math.Max(queue,status.QueueCount);
                if(watch.ElapsedMilliseconds>=pings*100){var ping=Stopwatch.StartNew();engine.WaitWrite(db=>db.Scalar("SELECT 1"));ping.Stop();writerMax=Math.Max(writerMax,ping.Elapsed.TotalMilliseconds);pings++;}Thread.Sleep(10);}
            watch.Stop();Check(String.IsNullOrEmpty(engine.Status.Error) && engine.Status.ProcessedFiles==4 && engine.Status.BytesRead==size,"Benchmark did not finish the complete full-read scan workload");
            long synchronous=(long)engine.WaitWrite(db=>db.Scalar("PRAGMA synchronous"));Check(synchronous==2,"Benchmark changed FULL write durability");
            string text=String.Format(System.Globalization.CultureInfo.InvariantCulture,"Local warm/cache-uncontrolled full scan; 4 x 64 MiB PNG (1-pixel image plus padding); workers=2; bytes={0}; seconds={1:F3}; MiB/s={2:F2}; parentPrivateBefore={3}; parentPrivatePeak={4}; parentPrivateDelta={5}; retainedQueuePeakBytes={6}; queuePeakItems={7}; writerPingMaxMs={8:F3}; writerPings={9}; SQLiteSynchronous={10}; child decoder memory excluded; no network source available.",size,watch.Elapsed.TotalSeconds,size/1048576.0/watch.Elapsed.TotalSeconds,before,peak,peak-before,retained,queue,writerMax,pings,synchronous);
            File.WriteAllText(Path.Combine(root,"benchmark.txt"),text,new System.Text.UTF8Encoding(false));
        }
    }
    static void OldDatabase(string database,string original,string replacement){
        using(var db=new SqliteConnection(database,false)){
            db.Script("PRAGMA foreign_keys=ON;"+Catalog.Schema+Migrations.Schema2+Migrations.Schema3+Migrations.Schema4+Migrations.Schema5+Migrations.Schema6+Migrations.Schema7+Migrations.Schema8+Migrations.Schema9+Migrations.Schema10+Migrations.Schema11+Migrations.Schema12+Migrations.Schema13+Migrations.Schema14+Migrations.Schema15+" PRAGMA application_id="+Catalog.ApplicationId+"; PRAGMA user_version=15;");
            db.Execute("INSERT INTO app_info VALUES('workspace_id','identity-migration-fixture')");db.Execute("INSERT INTO app_info VALUES('revision','0')");
            var item=Legacy(Path.Combine(original,"legacy.png"),"legacy.png");item.RootId=Catalog.Root(db,original);long group=Catalog.Publish(db,item);db.Execute("UPDATE review_groups SET status='COMPLIANT',notes='Saved original notes' WHERE id=?",group);
            string occurrence=(string)db.Scalar("SELECT id FROM occurrences LIMIT 1"),check=Guid.NewGuid().ToString("N"),batch=Guid.NewGuid().ToString("N");
            using(var stream=System.IO.File.OpenRead(Path.Combine(replacement,"legacy.png"))){var stamp=FileAccessNative.Stamp(stream);
                db.Execute("INSERT INTO source_connections(id,root_id,path,path_key,base_head,identity_revision,state,created_at,applied_at,ceiling,total,candidate_count) VALUES(?,?,?,?,'',1,'applied',?,?,1,1,1)",check,item.RootId,replacement,Paths.Key(replacement),Catalog.Now(),Catalog.Now());
                db.Execute("INSERT INTO source_connection_files VALUES(?,?,?,?,?,?,?,?,?,?,'candidate','')",check,occurrence,(long)db.Scalar("SELECT content_id FROM review_groups WHERE id=?",group),"legacy.png",item.Size,item.ModifiedTicks,item.FileIdentity,stamp.Size,stamp.ModifiedTicks,stamp.Identity);
            }
            db.Execute("INSERT INTO root_source_heads VALUES(?,?)",item.RootId,check);db.Execute("INSERT INTO source_observations VALUES(?,?,?,?,?)",occurrence,check,item.Size,item.ModifiedTicks,item.FileIdentity);
            db.Execute("INSERT INTO source_reconnect_batches(id,path,state,created_at,root_ceiling,manifest_complete) VALUES(?,?,'complete',?,1,1)",batch,replacement,Catalog.Now());
            db.Execute("INSERT INTO source_reconnect_items(batch_id,root_id,root_order,state,connection_id,candidate_count) VALUES(?,?,1,'connected',?,1)",batch,item.RootId,check);
        }
    }
    static void Migration(){
        string original=Path.Combine(root,"migration-original"),replacement=Path.Combine(root,"migration-replacement");FileAt(original,"legacy.png");Directory.CreateDirectory(replacement);File.Copy(Path.Combine(original,"legacy.png"),Path.Combine(replacement,"legacy.png"));
        string database=Path.Combine(root,"migration.sqlite");OldDatabase(database,original,replacement);
        using(var db=new SqliteConnection(database,false)){
            Catalog.Initialize(db,false);
            Check((long)db.Scalar("PRAGMA user_version")==Catalog.SchemaVersion && (string)db.Scalar("SELECT state FROM source_connection_files")=="candidate_unverified","Upgrade did not invalidate old candidate provenance");
            Check((long)db.Scalar("SELECT COUNT(*) FROM source_observations")==0 && (string)db.Scalar("SELECT state FROM source_reconnect_items")=="review","Upgrade left a candidate observation or automatic acceptance trusted");
            Check((string)db.Scalar("SELECT notes FROM review_groups")=="Saved original notes" && (string)db.Scalar("SELECT status FROM review_groups")=="COMPLIANT","Upgrade lost original notes or decisions");
            bool rejected=false;try{Verification.Source(db,(long)db.Scalar("SELECT id FROM review_groups"),true);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Upgraded candidate provenance was promotable");
            Check((string)db.Scalar("PRAGMA quick_check")=="ok" && db.Query("PRAGMA foreign_key_check",new object[0],1).Count==0,"Upgraded database integrity failed");
        }
        string backup=Directory.GetFiles(root,"migration.sqlite.pre-upgrade-v15-*.sqlite").Single();using(var db=new SqliteConnection(backup,true)){Check((long)db.Scalar("PRAGMA user_version")==15 && (string)db.Scalar("SELECT state FROM source_connection_files")=="candidate" && (string)db.Scalar("SELECT notes FROM review_groups")=="Saved original notes","Pre-upgrade backup did not retain the exact old identity/review state");}
        string rollback=Path.Combine(root,"migration-rollback.sqlite");OldDatabase(rollback,original,replacement);
        using(var db=new SqliteConnection(rollback,false)){
            db.Script("CREATE TRIGGER injected_identity_migration_failure BEFORE UPDATE OF state ON source_connection_files WHEN NEW.state='candidate_unverified' BEGIN SELECT RAISE(ABORT,'injected identity migration failure');END;");
            bool failed=false;try{Catalog.Initialize(db,false);}catch(DatabaseException){failed=true;}Check(failed,"Migration failure injection did not run");
            Check((long)db.Scalar("PRAGMA user_version")==15 && (string)db.Scalar("SELECT state FROM source_connection_files")=="candidate" && (string)db.Scalar("SELECT notes FROM review_groups")=="Saved original notes","Failed migration partially changed identity or review state");
            Check(!db.Query("PRAGMA table_info(stego_flags)",new object[0],20).Any(r=>(string)r[1]=="qualifies"),"Migration rollback retained a partial schema change");
        }
    }
    static void PreviouslyPromotedCandidate(){
        string original=Path.Combine(root,"promoted-original"),replacement=Path.Combine(root,"promoted-replacement"),database=Path.Combine(root,"promoted.sqlite");string file=FileAt(original,"legacy.png");Directory.CreateDirectory(replacement);
        byte[] bytes=File.ReadAllBytes(file);bytes[150000]=77;File.WriteAllBytes(Path.Combine(replacement,"legacy.png"),bytes);OldDatabase(database,original,replacement);string priorHead;
        using(var db=new SqliteConnection(database,false)){
            long group=(long)db.Scalar("SELECT id FROM review_groups"),source=(long)db.Scalar("SELECT content_id FROM review_groups");string rootId=(string)db.Scalar("SELECT root_id FROM review_groups"),occurrence=(string)db.Scalar("SELECT id FROM occurrences"),hash=Digest(bytes),run=Guid.NewGuid().ToString("N");priorHead=(string)db.Scalar("SELECT connection_id FROM root_source_heads");
            // Reconstruct the old resolver's unsafe outcome: it had retained the review
            // while treating the replacement's full digest as proof of a sampled original.
            db.Execute("INSERT INTO contents(algorithm,algorithm_version,size,digest,sampling,legacy_hash,first_seen,last_seen) VALUES('sha256',1,?,?,'',?,?,?)",bytes.Length,hash,hash,Catalog.Now(),Catalog.Now());long full=db.LastId;
            db.Execute("INSERT INTO identity_runs(id,group_id,source_content,root_id,state,created_at) VALUES(?,?,?,?,'published',?)",run,group,source,rootId,Catalog.Now());db.Execute("INSERT INTO identity_plan VALUES(?,?,?,?,0,1)",run,hash,full,group);db.Execute("INSERT INTO resolved_locations VALUES(?,?,?,?)",run,group,hash,occurrence);db.Execute("UPDATE review_groups SET content_id=? WHERE id=?",full,group);
            Catalog.Initialize(db,false);
        }
        using(var engine=ReviewerEngine.Open(database,false)){
            Wait(()=>engine.Ready||engine.Faulted);string batch=engine.RequestReconnectAll(replacement);Result(engine,batch);var state=Result(engine,engine.RequestReconnectAllStatus(batch)).Reconnection;
            Check(state.ConnectedRoots==0 && state.Failed==1,"A previously promoted sampled replacement bypassed the migration provenance guard on a new full reconnect");
            Check((string)engine.WaitWrite(db=>db.Scalar("SELECT connection_id FROM root_source_heads"))==priorHead,"Rechecking a historically promoted candidate silently changed its binding");
            Check((string)engine.WaitWrite(db=>db.Scalar("SELECT notes FROM review_groups"))=="Saved original notes","Historical candidate handling lost the saved review");
        }
    }
    [STAThread] static int Main(){
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
        if(Environment.CommandLine.Contains("--preview-worker"))return DecoderProgram.Run();
        root=Environment.GetEnvironmentVariable("FLEETHACKER_IDENTITY_TEST_ROOT");Directory.CreateDirectory(root);var results=new List<string>();int failed=0;
        try{Native.Initialize(Path.Combine(root,"native"));}catch(Exception e){File.WriteAllText(Path.Combine(root,"result.txt"),e.ToString(),new System.Text.UTF8Encoding(false));return 2;}
        string filter=Environment.GetEnvironmentVariable("FLEETHACKER_IDENTITY_CASE");
        foreach(var test in new Action[]{Hashes,Scans,Reconnects,ReboundCandidate,ProblemRoots,SharedPendingRoot,OriginalVerification,LegacySafety,ReviewedConflict,CancelResume,Migration,PreviouslyPromotedCandidate}.Where(t=>String.IsNullOrEmpty(filter)||t.Method.Name==filter))try{test();results.Add("PASS "+test.Method.Name);}catch(Exception e){failed++;results.Add("FAIL "+test.Method.Name+": "+e);}
        if(results.Count==0){failed++;results.Add("FAIL Unknown identity regression case: "+filter);}
        if(Environment.GetEnvironmentVariable("FLEETHACKER_IDENTITY_BENCHMARK")=="1")try{Benchmark();results.Add("PASS Benchmark");}catch(Exception e){failed++;results.Add("FAIL Benchmark: "+e);}
        File.WriteAllLines(Path.Combine(root,"result.txt"),results,new System.Text.UTF8Encoding(false));return failed==0 ? 0 : 1;
    }
}
