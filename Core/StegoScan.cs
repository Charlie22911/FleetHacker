using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace MediaReviewer {
    public sealed class StegoPhotoResult {
        public long GroupId;public string Name{get;set;}public string Outcome{get;set;}public string CheckedAt{get;set;}
    }
    public sealed class StegoScanState {
        public long Pending,Checked,Revision,Suspects;public int Page;public bool More;public List<StegoPhotoResult> Results=new List<StegoPhotoResult>();
    }
    internal static class StegoScan {
        static readonly string Photos="g.retired=0 AND g.status<>'EVIDENCE' AND EXISTS(SELECT 1 FROM group_occurrences o WHERE o.group_id=g.id AND o.superseded=0 AND o.extension IN("+String.Join(",",Formats.Deep.Where(e=>!Formats.IsVideo(e)).Select(e=>"'"+e+"'"))+") )";
        const string Latest=" LEFT JOIN stego_runs s ON s.id=(SELECT r.id FROM stego_runs r WHERE r.group_id=g.id AND r.content_id=g.content_id AND r.package_sha256=?1 ORDER BY r.created_at DESC,r.id DESC LIMIT 1) ";
        internal const string Finished="s.status IN('completed','partial','unsupported','limited') AND NOT EXISTS(SELECT 1 FROM json_each(s.result_json,'$.engines') e WHERE json_extract(e.value,'$.state') IN('failed','timed_out','unavailable','cancelled'))";
        static string PendingFilter {get{return Photos+" AND (s.id IS NULL OR NOT("+Finished+"))";}}
        internal static long Pending(SqliteConnection db,string package,long ceiling){return Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM review_groups g"+Latest+"WHERE "+PendingFilter+" AND g.id<=?2",package,ceiling));}
        internal static IEnumerable<long> Candidates(SqliteConnection db,string package,long ceiling,CancellationToken token){
            long after=0;while(true){token.ThrowIfCancellationRequested();var rows=db.Query("SELECT g.id FROM review_groups g"+Latest+"WHERE "+PendingFilter+" AND g.id>?2 AND g.id<=?3 ORDER BY g.id LIMIT 16",new object[]{package,after,ceiling},16);if(rows.Count==0)yield break;
                foreach(var row in rows){token.ThrowIfCancellationRequested();after=(long)row[0];yield return after;}
            }
        }
        internal static string Outcome(string state,object index,bool incomplete=false){
            if(state==null)return "Not checked";
            long points=index==null ? 0 : Convert.ToInt64(index);
            if(points>=80)return incomplete ? "Hidden data found (limited check)" : "Hidden data found";
            if(points>0)return incomplete ? "Needs review (limited check)" : "Needs review";
            if(incomplete)return "Check incomplete";
            if(state=="failed")return "Could not check";
            if(state=="unsupported")return "Photo type not supported";
            if(state=="limited")return "Photo exceeds scan limits";
            return state=="partial" ? "Limited check" : "No signs found";
        }
        internal static StegoScanState State(SqliteConnection db,string package,int page){
            long revision=Catalog.Revision(db);
            var counts=db.Query("SELECT COUNT(*),COALESCE(SUM(CASE WHEN s.id IS NULL OR NOT("+Finished+") THEN 1 ELSE 0 END),0) FROM review_groups g"+Latest+"WHERE "+Photos,new object[]{package},1)[0];
            var result=new StegoScanState {Page=page,Revision=revision,Suspects=StegoFindings.Count(db),Pending=(long)counts[1],Checked=(long)counts[0]-(long)counts[1]};
            var rows=db.Query("SELECT g.id,COALESCE((SELECT substr(o.name,1,256) FROM group_occurrences o WHERE o.group_id=g.id AND o.superseded=0 ORDER BY o.occurrence_order LIMIT 1),'Photo'),s.status,s.suspicion_index,s.created_at,CASE WHEN ("+Finished+") THEN 1 ELSE 0 END FROM review_groups g"+Latest+"WHERE "+Photos+" ORDER BY s.created_at DESC,g.id LIMIT 51 OFFSET ?2",new object[]{package,(page-1)*50L},51);
            result.More=rows.Count>50;
            foreach(var row in rows.Take(50)){DateTime at;string date=DateTime.TryParse((string)row[4],out at) ? at.ToLocalTime().ToString("g") : "";result.Results.Add(new StegoPhotoResult {GroupId=(long)row[0],Name=(string)row[1],Outcome=Outcome((string)row[2],row[3],(long)row[5]==0 && (string)row[2]=="partial"),CheckedAt=date});}return result;
        }
    }
    public sealed partial class ReviewerEngine {
        public string RequestStegoScanState(int page=1){
            if(!ready || closing || faulted)throw new InvalidOperationException("Open a database before scanning photos.");if(page<1||page>1000000)throw new ArgumentOutOfRangeException("page");
            string id=Guid.NewGuid().ToString("N"),package=StegoCompanion.EmbeddedPin();
            var work=new DbWork {Body=db=>StegoScan.State(db,package,page),Done=value=>Respond(new UiResult {RequestId=id,Kind="Hidden data scan",Success=true,StegoState=(StegoScanState)value}),Failed=error=>Respond(new UiResult {RequestId=id,Kind="Hidden data scan",Error=error.Message})};
            if(!reads.TryAdd(work,1024))throw new InvalidOperationException("The database is busy. Try again shortly.");return id;
        }
    }
}
