using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
namespace MediaReviewer {
    public static partial class Catalog {
        internal static bool CleanAbandonedVerificationBatch(SqliteConnection db) {
            string run=(string)db.Scalar("SELECT id FROM identity_runs WHERE state='abandoned' LIMIT 1");
            if(run==null)return false;
            Verification.Transaction(db,()=>{
                foreach(string table in new[]{"resolved_locations","identity_checks","identity_plan"}){
                    if(db.Execute("DELETE FROM "+table+" WHERE rowid IN(SELECT rowid FROM "+table+" WHERE run_id=? LIMIT 64)",run)>0)
                        return;
                }
                db.Execute("DELETE FROM identity_runs WHERE id=? AND state='abandoned'",run);
            });
            return true;
        }
    }
    public sealed partial class ReviewerEngine {
        public string RequestIntegrity() {
            return EnqueueOperation("Integrity",(db,token,progress)=>{
                long instructions=0;
                Sql.ProgressCallback check=state=>{instructions+=2000;progress(instructions,-1,"SQLite steps");return token.IsCancellationRequested ? 1 : 0;};
                Sql.sqlite3_progress_handler(db.Handle,2000,check,IntPtr.Zero);
                try{token.ThrowIfCancellationRequested();string value=(string)db.Scalar("PRAGMA integrity_check");token.ThrowIfCancellationRequested();if(value!="ok")throw new InvalidDataException("Integrity check: "+value);return "Database integrity check: ok";}
                catch(DatabaseException){token.ThrowIfCancellationRequested();throw;}
                finally{Sql.sqlite3_progress_handler(db.Handle,0,null,IntPtr.Zero);GC.KeepAlive(check);}
            });
        }
        public string RequestDatabaseCleanup() {
            return EnqueueOperation("DatabaseCleanup",(db,token,progress)=>OwnSourceConnection(()=>{
                long cleaned=0;
                while(true){
                    token.ThrowIfCancellationRequested();
                    string run=(string)db.Scalar("SELECT id FROM identity_runs WHERE state<>'published' LIMIT 1");
                    if(run==null)break;
                    foreach(string table in new[]{"resolved_locations","identity_checks"}){
                        while((bool)WaitWrite(w=>{bool more=w.Scalar("SELECT 1 FROM "+table+" WHERE run_id=? LIMIT 1",run)!=null;if(more)Verification.Transaction(w,()=>w.Execute("DELETE FROM "+table+" WHERE rowid IN(SELECT rowid FROM "+table+" WHERE run_id=? LIMIT 64)",run));return (object)more;})){token.ThrowIfCancellationRequested();}
                    }
                    WaitWrite(w=>{Verification.Transaction(w,()=>{w.Execute("DELETE FROM identity_plan WHERE run_id=?",run);w.Execute("DELETE FROM identity_runs WHERE id=? AND state<>'published'",run);});return null;});
                    cleaned++;progress(cleaned,-1,"stale verification runs cleaned");
                }
                while(true){
                    token.ThrowIfCancellationRequested();
                    string obsolete=(string)db.Scalar("SELECT id FROM source_connections WHERE state='superseded' LIMIT 1");
                    if(obsolete==null)break;
                    while((bool)WaitWrite(w=>{long n=w.Execute("DELETE FROM source_connection_files WHERE rowid IN(SELECT rowid FROM source_connection_files WHERE connection_id=? LIMIT 200)",obsolete);return (object)(n>0);})){token.ThrowIfCancellationRequested();}
                    WaitWrite(w=>{w.Execute("DELETE FROM source_connections WHERE id=? AND state='superseded'",obsolete);return null;});
                    cleaned++;progress(cleaned,-1,"superseded source checks cleaned");
                }
                WaitWrite(w=>{
                    w.Execute("DELETE FROM preview_retry_batches WHERE id IN(SELECT id FROM preview_retry_batches WHERE state IN('complete','discarded') ORDER BY created_at DESC LIMIT -1 OFFSET 20)");
                    w.Script("PRAGMA wal_checkpoint(PASSIVE); PRAGMA optimize;");
                    return null;
                });
                return "Database cleanup complete ("+cleaned+" stale staging records removed; WAL checkpointed and query planner optimized).";
            },"Wait for the current scan or report to finish before cleaning up staging data."));
        }
        public string RequestDiscardIdentityRun(string runId) {
            if(String.IsNullOrEmpty(runId) || runId.Length>64)throw new ArgumentException("Invalid verification run identifier");
            return EnqueueOperation("IdentityCleanup",(db,token,progress)=>{
                if(db.Scalar("SELECT 1 FROM identity_runs WHERE id=? AND state='published'",runId)!=null)return "Verification run was already published.";
                foreach(string table in new[]{"resolved_locations","identity_checks"}){
                    while((bool)WaitWrite(w=>{bool more=w.Scalar("SELECT 1 FROM "+table+" WHERE run_id=? LIMIT 1",runId)!=null;if(more)Verification.Transaction(w,()=>w.Execute("DELETE FROM "+table+" WHERE rowid IN(SELECT rowid FROM "+table+" WHERE run_id=? LIMIT 64)",runId));return (object)more;})){token.ThrowIfCancellationRequested();}
                }
                WaitWrite(w=>{Verification.Transaction(w,()=>{w.Execute("DELETE FROM identity_plan WHERE run_id=?",runId);w.Execute("DELETE FROM identity_runs WHERE id=? AND state<>'published'",runId);});return null;});
                return "Unapplied verification preview discarded.";
            });
        }
        public string RequestLog() {
            if(!Ready || closing) throw new InvalidOperationException("Open a database first"); string id=Guid.NewGuid().ToString("N");
            var work=new DbWork { Body=db=> {
                var text=new StringBuilder(); text.AppendLine("Media Compliance Reviewer for Windows "+Product.Version+" — recent log"); text.AppendLine("Log may contain source names and paths.");
                text.AppendLine("Access denied: Windows refused the read/list request. Local administrator elevation does not grant remote share or NTFS permissions. File encryption can also restrict reads. The scan does not change source permissions.");
                text.AppendLine("Unrecognized media header: the extension alone does not establish the file type. Fingerprints remain saved when preview generation fails. Earlier log entries retain their original diagnostic detail.");text.AppendLine();
                foreach(object[] row in db.Query("SELECT at,level,stage,path,message FROM diagnostics ORDER BY id DESC LIMIT 500",new object[0],500)) { string line=row[0]+" ["+row[1]+"] "+row[2]+" | "+row[3]+" | "+row[4]; if(text.Length+line.Length>262144){text.AppendLine("Display is bounded; additional log details remain in the database.");break;} text.AppendLine(line); } return text.ToString();
            },Done=value=>Respond(new UiResult { RequestId=id,Kind="Log",Success=true,Value=(string)value }),Failed=error=>Respond(new UiResult { RequestId=id,Kind="Log",Error=error.Message }) };
            if(!reads.TryAdd(work,1024)) throw new InvalidOperationException("Reader queue is busy"); return id;
        }
    }
}
