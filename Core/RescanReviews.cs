using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
namespace MediaReviewer {
    public static partial class Catalog {
        // SQL compares complete saved text, including overflow fields, without loading it into a worker.
        static readonly string SameReview=@"b.status=a.status AND b.reviewer=a.reviewer AND b.notes=a.notes
 AND COALESCE((SELECT sha256 FROM imported_fields WHERE owner_table='review_groups' AND owner_rowid=b.id AND column_name='notes'),'')=COALESCE((SELECT sha256 FROM imported_fields WHERE owner_table='review_groups' AND owner_rowid=a.id AND column_name='notes'),'')
 AND COALESCE((SELECT sha256 FROM imported_fields WHERE owner_table='review_groups' AND owner_rowid=b.id AND column_name='reviewer'),'')=COALESCE((SELECT sha256 FROM imported_fields WHERE owner_table='review_groups' AND owner_rowid=a.id AND column_name='reviewer'),'')";
        static readonly string UntouchedReview=@"g.status='TO_REVIEW' AND g.reviewer='' AND g.notes='' AND g.reviewed_at IS NULL AND g.retired=0
 AND NOT EXISTS(SELECT 1 FROM review_events WHERE group_id=g.id)
 AND NOT EXISTS(SELECT 1 FROM imported_fields WHERE owner_table='review_groups' AND owner_rowid=g.id)
 AND NOT EXISTS(SELECT 1 FROM original_links WHERE group_id=g.id) AND NOT EXISTS(SELECT 1 FROM legacy_original_links WHERE group_id=g.id)
 AND NOT EXISTS(SELECT 1 FROM review_group_redirects WHERE target_group=g.id)
 AND NOT EXISTS(SELECT 1 FROM original_targets WHERE group_id=g.id)
 AND NOT EXISTS(SELECT 1 FROM evidence_items WHERE decision_key=g.legacy_key) AND NOT EXISTS(SELECT 1 FROM evidence_manifests WHERE decision_key=g.legacy_key)";
        internal static void RetireReplacedCandidates(SqliteConnection db,long canonical,string root,string pathKey,long content,string verifiedSample){
            if(db.Scalar("SELECT 1 FROM review_groups WHERE id=? AND retired=0 AND (status<>'TO_REVIEW' OR ? IS NOT NULL)",canonical,verifiedSample)==null)return;
            foreach(var row in db.Query("SELECT g.id FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.root_id=? AND g.content_id<>? AND c.algorithm='sampled-sha256-v1' AND (? IS NULL OR c.legacy_hash=?) AND "+UntouchedReview+" AND EXISTS(SELECT 1 FROM occurrences o WHERE o.root_id=g.root_id AND o.content_id=g.content_id AND o.path_key=? AND o.superseded=1) AND NOT EXISTS(SELECT 1 FROM group_occurrences active WHERE active.group_id=g.id AND active.superseded=0) LIMIT 16",new object[]{root,content,verifiedSample,verifiedSample,pathKey},16)){
                db.Execute("UPDATE review_groups SET retired=1 WHERE id=?",row[0]);Log(db,"info","Rescan duplicate retired",pathKey,"Untouched candidate group "+row[0]+" has been replaced by fully verified locations on group "+canonical+". Its history is retained.");
            }
        }
        internal static long ReuseReviewed(SqliteConnection db,long local,long content,string root,string path){
            object redirect=db.Scalar("SELECT target_group FROM review_group_redirects WHERE source_group=?",local);if(redirect!=null)return Convert.ToInt64(redirect);
            // Never retire a reviewed card, an edited draft, or a card with any saved action/original.
            if(db.Scalar("SELECT 1 FROM review_groups g WHERE g.id=? AND "+UntouchedReview,local)==null)return local;
            object target=db.Scalar("SELECT id FROM review_groups WHERE content_id=? AND retired=0 AND status<>'TO_REVIEW' ORDER BY display_sequence LIMIT 1",content);
            if(target==null)return local;
            if(db.Scalar("SELECT 1 FROM review_groups a JOIN review_groups b ON b.content_id=a.content_id WHERE a.id=? AND b.retired=0 AND b.status<>'TO_REVIEW' AND NOT ("+SameReview+") LIMIT 1",target)!=null){
                if(db.Scalar("SELECT 1 FROM diagnostics WHERE stage='Rescan review conflict' AND path=? LIMIT 1",root+"\\"+path)==null)Log(db,"warning","Rescan review conflict",root+"\\"+path,"Full SHA-256 matches conflicting saved reviews. Existing reviews are preserved; this root's item remains separate.");return local;
            }
            long canonical=Convert.ToInt64(target);
            db.Execute("INSERT INTO review_group_redirects VALUES(?,?,?,?)",local,canonical,root,content);
            db.Execute("UPDATE review_groups SET retired=1 WHERE id=?",local);
            Log(db,"info","Rescan review reused",path,"Verified full SHA-256 reuses reviewed group "+canonical+"; new source root "+root+". Previous group "+local+" retained as history.");return canonical;
        }
        // Prefer the latest committed scan's source without changing the card's publication sequence.
        internal static string SourceOrder(string alias){return alias+".superseded,CASE WHEN "+alias+".file_identity='Unvalidated reconnected source' THEN 1 ELSE 0 END,COALESCE((SELECT started_at FROM scans WHERE id="+alias+".last_scan_id),'') DESC,"+alias+".occurrence_order DESC";}
    }
    internal sealed partial class ScanRunner {
        string ReviewedSamples(FileRecord record){
            string sql="SELECT g.id FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE c.algorithm='sampled-sha256-v1' AND c.algorithm_version=1 AND c.size=?1 AND g.retired=0 AND g.status IN('COMPLIANT','NON_COMPLIANT') AND NOT EXISTS(SELECT 1 FROM scan_review_checks checked WHERE checked.scan_id=?2 AND checked.group_id=g.id AND checked.state IN('verified','unverified','conflict'))";
            if(record.Algorithm=="sampled-sha256-v1")sql+=" AND c.digest=?3 AND (c.sampling=?4 OR (?4='windows=10;maxWindowBytes=1048576' AND c.sampling='html-v4:q2:'||?3))";
            else sql+=" AND ?3 IS NOT NULL AND ?4 IS NOT NULL";
            return sql+" ORDER BY g.display_sequence LIMIT 9";
        }
        FileRecord MatchCatalogIdentity(FileRecord record,byte[] buffer){
            var token=cancellation.Token;token.ThrowIfCancellationRequested();
            string digest=record.Hash.StartsWith("q2:",StringComparison.Ordinal) ? record.Hash.Substring(3) : record.Hash;
            object[] args={record.Size,id,digest,record.Sampling};
            bool sampled=record.Algorithm=="sampled-sha256-v1";
            var check=(Tuple<bool,bool>)Write(db=>{
                const string fullAtSize="SELECT 1 FROM contents c JOIN review_groups g ON g.content_id=c.id WHERE c.algorithm='sha256' AND c.algorithm_version=1 AND c.sampling='' AND c.size=? AND g.retired=0";
                bool reviews=db.Scalar(fullAtSize+" AND g.status<>'TO_REVIEW' LIMIT 1",record.Size)!=null ||
                    db.Query(ReviewedSamples(record),args,9).Count>0;
                bool possibleCopy=!reviews && sampled && db.Scalar(fullAtSize+" LIMIT 1",record.Size)!=null;
                return Tuple.Create(reviews,possibleCopy);
            });
            if(!check.Item1 && !check.Item2)return record;
            FileRecord candidate=record;
            if(sampled){
                engine.Update(s=>s.Stage="Checking full identity against the catalog: "+record.RelativePath);
                var full=new FileRecord {RootId=record.RootId,ScanId=record.ScanId,JobId=record.JobId,UserOrdinal=record.UserOrdinal,RelativePath=record.RelativePath,Name=record.Name,Extension=record.Extension};
                FileAccessNative.FingerprintScoped(Full(record.RelativePath),full,buffer,token,n=>engine.Update(s=>{s.BytesRead+=n;s.LastProgressUtc=DateTime.UtcNow;}),0,FingerprintPolicy.Full(),options.RootPath);
                if(full.Size!=record.Size || full.ModifiedTicks!=record.ModifiedTicks || full.FileIdentity!=record.FileIdentity)throw new FileChangedException("Source changed between quick and full matching; retry must reopen it.");
                record=full;
            }
            if(check.Item2){
                // Same size only justifies a full read. Preserve the normal scan policy if the digest differs.
                bool exact=Write(db=>db.Scalar(@"SELECT 1 FROM contents c JOIN review_groups g ON g.content_id=c.id
 WHERE c.algorithm='sha256' AND c.algorithm_version=1 AND c.sampling='' AND c.size=? AND c.digest=? AND g.retired=0 LIMIT 1",
                    record.Size,record.Hash))!=null;
                if(!exact)return candidate;
                record.VerifiedCopySampleHash=candidate.Hash;
                return record;
            }
            bool entered=false;
            try{
                while(!(entered=Monitor.TryEnter(reviewMatchGate,50)))token.ThrowIfCancellationRequested();token.ThrowIfCancellationRequested();
                var groups=(List<object[]>)Write(db=>(object)db.Query(ReviewedSamples(candidate),args,9));
                if(groups.Count>8){Write(db=>{Catalog.Log(db,"warning","Rescan candidate verification",record.RelativePath,"More than eight saved candidate cards need verification. The new full identity is retained separately; use full verification to resolve the remaining candidates.");return null;});return record;}
                foreach(var row in groups){
                    token.ThrowIfCancellationRequested();long group=(long)row[0];
                    bool start=(bool)Write(db=>{
                        if(db.Scalar("SELECT 1 FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.id=? AND g.retired=0 AND g.status IN('COMPLIANT','NON_COMPLIANT') AND c.algorithm='sampled-sha256-v1'",group)==null)return (object)false;
                        db.Execute("INSERT INTO scan_review_checks(scan_id,group_id,state,error) VALUES(?,?,'checking','') ON CONFLICT(scan_id,group_id) DO UPDATE SET state='checking',error=''",id,group);return (object)true;
                    });
                    if(start)engine.VerifyReviewedRescan(group,record,id,token);
                }
                if(Write(db=>db.Scalar("SELECT 1 FROM scan_review_checks checked JOIN review_groups g ON g.id=checked.group_id JOIN contents c ON c.id=g.content_id WHERE checked.scan_id=? AND checked.state='conflict' AND checked.full_digest=? AND c.size=? LIMIT 1",id,record.Hash,record.Size))!=null)record.ReviewConflict=true;
            }finally{if(entered)Monitor.Exit(reviewMatchGate);}
            return record;
        }
    }
    public sealed partial class ReviewerEngine {
        // Called on an owned file worker, never the writer or UI thread.
        internal bool VerifyReviewedRescan(long group,FileRecord incoming,string scan,CancellationToken token){
            string run=Guid.NewGuid().ToString("N"),conflictHash="";bool published=false;
            try{
                using(var reader=new SqliteConnection(path,true)){
                    var p=VerifyFullCore(reader,group,null,token,(done,total,stage)=>Update(s=>{s.Stage="Checking saved review "+group+": "+stage;s.LastProgressUtc=DateTime.UtcNow;}),run);
                    if(p.HasConflicts && p.CanApply && p.Outcomes.Count==1)conflictHash=p.Outcomes[0].Hash;
                    if(!p.CanApply || p.HasConflicts || p.Outcomes.Count!=1 || p.Outcomes[0].ExistingGroupId!=0){
                        throw new InvalidDataException("Saved candidate does not resolve to a single independent full-file identity; its review is preserved separately.");
                    }
                    p.AutomaticRescan=true;
                    WaitWrite(w=>{Verification.CheckRevision(w,p);w.Execute("UPDATE identity_runs SET preview_json=? WHERE id=?",Verification.Json().Serialize(p),p.RunId);return null;});
                    ResolveIdentityCore(reader,p.RequestCopy(),"rescan-"+run,token,(done,total,stage)=>Update(s=>{s.Stage="Reusing saved review "+group+": "+stage;s.LastProgressUtc=DateTime.UtcNow;}));published=true;
                    WaitWrite(w=>{w.Execute("UPDATE scan_review_checks SET state='verified',error='' WHERE scan_id=? AND group_id=?",scan,group);return null;});return true;
                }
            }catch(OperationCanceledException){throw;}
            catch(DatabaseException){throw;}
            catch(Exception error){
                WaitWrite(w=>{w.Execute("UPDATE scan_review_checks SET state=?,error=?,full_digest=? WHERE scan_id=? AND group_id=?",conflictHash.Length==0 ? "unverified" : "conflict",error.Message,conflictHash,scan,group);Catalog.Log(w,"warning",conflictHash.Length==0 ? "Rescan candidate verification" : "Rescan review conflict",incoming.RelativePath,"Saved group "+group+": "+error.Message+" No decision transferred on quick-hash equality.");return null;});return false;
            }finally{
                // Keep each cleanup transaction bounded too. A published/uncertain outcome is
                // reconciled from its durable run before touching any staged rows.
                if(!published && !(bool)WaitWrite(w=>(object)(w.Scalar("SELECT 1 FROM identity_runs WHERE id=? AND state='published'",run)!=null))){
                    foreach(string table in new[]{"resolved_locations","identity_checks"}){
                        while((bool)WaitWrite(w=>{bool more=w.Scalar("SELECT 1 FROM "+table+" WHERE run_id=? LIMIT 1",run)!=null;if(more)Verification.Transaction(w,()=>w.Execute("DELETE FROM "+table+" WHERE rowid IN(SELECT rowid FROM "+table+" WHERE run_id=? LIMIT 32)",run));return (object)more;})){}
                    }
                    WaitWrite(w=>{Verification.Transaction(w,()=>{w.Execute("DELETE FROM identity_plan WHERE run_id=?",run);w.Execute("DELETE FROM identity_runs WHERE id=?",run);});return null;});
                }
            }
        }
    }
}
