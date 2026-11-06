using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace MediaReviewer {
    internal sealed class ProtectedDetailsException:InvalidOperationException {
        internal ProtectedDetailsException():base("This item is in Evidence. Use View saved original to see its authenticated capture details."){}
    }
    public sealed class InspectorData {
        public long GroupId,ContentId,Sequence,Size,ReviewRevision,LocationCount;
        public int AlgorithmVersion;
        public string RootPath,Status,Reviewer,Notes,ReviewedAt,Algorithm,Digest,Sampling,PreviewState,PreviewError;
        public bool TextReadOnly;
        internal long Cost {get{return 2048+2L*new[]{RootPath,Status,Reviewer,Notes,ReviewedAt,Algorithm,Digest,Sampling,PreviewState,PreviewError}.Sum(x=>(long)x.Length);}}
    }
    public sealed class LocationRow {
        public string Id {get;set;}public string Path {get;set;}public string RootPath {get;set;}
        public long Size {get;set;}public long ModifiedTicks {get;set;}public bool Historical {get;set;}
        public bool OtherCard {get;set;}
        public bool Sampled {get;set;}
        public string CardLabel {get{return !OtherCard ? "This item" : Sampled ? "Candidate" : "Exact match";}}
        public string FullPath {get{return RootPath.TrimEnd('\\')+"\\"+Path;}}
        internal long Cost {get{return 128+2L*(Id.Length+Path.Length+RootPath.Length);}}
    }
    public sealed class LocationPage {
        public long GroupId,ContentId;public string NextAfter="";public bool HasMore;public List<LocationRow> Rows=new List<LocationRow>();
        internal long Cost {get{return 512+NextAfter.Length*2L+Rows.Sum(x=>x.Cost);}}
    }
    public sealed class NotesRequest {
        public long GroupId,ContentId,ExpectedRevision;public string OperationId,Reviewer="",Notes="";
        internal NotesRequest Copy(){return (NotesRequest)MemberwiseClone();}
    }
    public static partial class Catalog {
        internal static readonly UTF8Encoding ReviewUtf8=new UTF8Encoding(false,true);
        sealed class ReviewText {
            internal string Text;internal bool ReadOnly;
            internal ReviewText(byte[] bytes,int limit,bool preserved){
                bytes=bytes??new byte[0];
                ReadOnly=preserved || bytes.Length>limit*3;
                try{Text=ReviewUtf8.GetString(bytes);}catch(DecoderFallbackException){Text=Encoding.UTF8.GetString(bytes);ReadOnly=true;}
                if(Text.Length>limit){ReadOnly=true;int take=Char.IsHighSurrogate(Text[limit-1]) ? limit-1 : limit;Text=Text.Substring(0,take);}
                if(Text.IndexOf('\0')>=0){ReadOnly=true;Text=Text.Replace('\0','\u2400');}
            }
        }
        internal static InspectorData Inspector(SqliteConnection db,long group) {
            var rows=db.Query(@"SELECT g.content_id,g.display_sequence,c.size,g.review_revision,substr(COALESCE((SELECT current_root.path FROM group_occurrences current_location JOIN source_roots current_root ON current_root.id=current_location.root_id WHERE current_location.group_id=g.id ORDER BY "+SourceOrder("current_location")+@" LIMIT 1),r.path),1,32768),g.status,substr(CAST(g.reviewer AS BLOB),1,385),substr(CAST(g.notes AS BLOB),1,49153),substr(COALESCE(g.reviewed_at,''),1,64),substr(c.algorithm,1,64),c.algorithm_version,substr(c.digest,1,256),substr(c.sampling,1,1024),substr(COALESCE(p.state,'pending'),1,32),substr(COALESCE(p.error,''),1,4096),EXISTS(SELECT 1 FROM imported_fields f WHERE f.owner_table='review_groups' AND f.owner_rowid=g.id AND f.column_name IN('notes','reviewer')),(SELECT COUNT(*) FROM group_occurrences o WHERE o.group_id=g.id) FROM review_groups g JOIN contents c ON c.id=g.content_id JOIN source_roots r ON r.id=g.root_id LEFT JOIN previews p ON p.content_id=g.content_id WHERE g.id=? AND g.retired=0",new object[]{group},1);
            if(rows.Count!=1)throw new InvalidOperationException("This item was replaced or removed; reload the gallery.");var v=rows[0];
            if((string)v[5]=="EVIDENCE")throw new ProtectedDetailsException();
            var reviewer=new ReviewText((byte[])v[6],128,(long)v[15]!=0);var notes=new ReviewText((byte[])v[7],16384,(long)v[15]!=0);
            return new InspectorData {GroupId=group,ContentId=(long)v[0],Sequence=(long)v[1],Size=(long)v[2],ReviewRevision=(long)v[3],RootPath=(string)v[4],Status=(string)v[5],Reviewer=reviewer.Text,Notes=notes.Text,ReviewedAt=(string)v[8],Algorithm=(string)v[9],AlgorithmVersion=Convert.ToInt32(v[10]),Digest=(string)v[11],Sampling=(string)v[12],PreviewState=(string)v[13],PreviewError=(string)v[14],TextReadOnly=reviewer.ReadOnly || notes.ReadOnly,LocationCount=(long)v[16]};
        }
        internal static LocationPage Locations(SqliteConnection db,long group,string after,int limit) {
            db.Execute("BEGIN");try{
                var groups=db.Query(@"SELECT g.content_id,
  CASE WHEN c.algorithm_version=1 AND
 ((c.algorithm='sha256' AND c.sampling='') OR c.algorithm='sampled-sha256-v1') THEN g.content_id END,
  c.algorithm='sampled-sha256-v1',g.status
 FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.id=? AND g.retired=0",new object[]{group},1);
                if(groups.Count==0)throw new InvalidOperationException("This item was replaced or removed; reload the gallery.");
                if((string)groups[0][3]=="EVIDENCE")throw new ProtectedDetailsException();
                var page=new LocationPage {GroupId=group,ContentId=(long)groups[0][0]};long cost=512;
                // Include the same fingerprint on other ordinary cards without merging their reviews.
                using(var statement=db.Prepare(@"SELECT substr(CAST(o.id AS BLOB),1,393217),substr(CAST(o.path AS BLOB),1,98302),
 o.size,o.modified_ticks,o.superseded,substr(r.path,1,32768),MIN(o.group_id<>?1)
 FROM group_occurrences o JOIN source_roots r ON r.id=o.root_id
 WHERE o.group_id IN (SELECT ?1 UNION ALL SELECT id FROM review_groups
   WHERE content_id=?2 AND retired=0 AND status<>'EVIDENCE' AND id<>?1)
 AND o.id>?3 COLLATE BINARY
 GROUP BY o.id COLLATE BINARY ORDER BY o.id COLLATE BINARY LIMIT ?4")){
                    statement.Bind(group,groups[0][1],after,limit+1);while(statement.Step()){
                        var idBytes=(byte[])statement.Value(0)??new byte[0];var pathBytes=(byte[])statement.Value(1)??new byte[0];if(idBytes.Length>393216 || pathBytes.Length>98301)throw new InvalidOperationException("A recorded location exceeds the display limit; its original data is preserved.");
                        var row=new LocationRow {Id=ReviewUtf8.GetString(idBytes),Path=ReviewUtf8.GetString(pathBytes),Size=(long)statement.Value(2),ModifiedTicks=(long)statement.Value(3),Historical=(long)statement.Value(4)!=0,RootPath=(string)statement.Value(5),OtherCard=(long)statement.Value(6)!=0,Sampled=(long)groups[0][2]!=0};
                        if(row.Id.Length>131072 || row.Path.Length>32767)throw new InvalidOperationException("A recorded location exceeds the display limit; its original data is preserved.");
                        if(page.Rows.Count==limit || cost+row.Cost+row.Id.Length*2L>2*1024*1024){page.HasMore=true;break;}
                        page.Rows.Add(row);cost+=row.Cost;page.NextAfter=row.Id;
                    }
                }db.Execute("COMMIT");return page;
            }catch{Rollback(db);throw;}
        }
        internal static bool Notes(SqliteConnection db,NotesRequest r) {
            string payload="notes-v1\n"+r.GroupId+"\n"+r.ContentId+"\n"+r.ExpectedRevision+"\n"+r.Reviewer.Length+":"+r.Reviewer+"\n"+r.Notes.Length+":"+r.Notes;
            string digest;using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-","").ToLowerInvariant();
            db.Execute("BEGIN IMMEDIATE");try{
                object receipt=db.Scalar("SELECT payload_hash FROM operations WHERE id=?",r.OperationId);if(receipt!=null){if((string)receipt!=digest)throw new InvalidOperationException("Operation identifier has a different payload; it cannot be replayed.");db.Execute("COMMIT");return true;}
                var rows=db.Query("SELECT content_id,review_revision,status,legacy_key,substr(CAST(reviewer AS BLOB),1,385),substr(CAST(notes AS BLOB),1,49153),EXISTS(SELECT 1 FROM imported_fields f WHERE f.owner_table='review_groups' AND f.owner_rowid=g.id AND f.column_name IN('notes','reviewer')) FROM review_groups g WHERE id=? AND retired=0",new object[]{r.GroupId},1);
                if(rows.Count!=0 && (string)rows[0][2]=="EVIDENCE")throw new ProtectedDetailsException();
                if(rows.Count!=1 || (long)rows[0][0]!=r.ContentId || (long)rows[0][1]!=r.ExpectedRevision)throw new InvalidOperationException("This item's identity or review changed. Your draft is retained; reload before saving again.");
                if(new ReviewText((byte[])rows[0][4],128,(long)rows[0][6]!=0).ReadOnly || new ReviewText((byte[])rows[0][5],16384,(long)rows[0][6]!=0).ReadOnly)throw new InvalidOperationException("Extended or unsupported text is preserved read-only by this editor.");
                string status=(string)rows[0][2],now=Now();db.Execute("UPDATE review_groups SET reviewer=?,notes=?,reviewed_at=? WHERE id=?",r.Reviewer,r.Notes,now,r.GroupId);
                db.Execute("INSERT INTO review_events VALUES(?,?,?,?,?,?,?,?,?)",Guid.NewGuid().ToString(),r.GroupId,rows[0][3],status,status,now,r.Reviewer,r.Notes,r.OperationId);
                long revision=Bump(db);db.Execute("INSERT INTO operations VALUES(?,?,?,?)",r.OperationId,digest,now,revision);db.Execute("COMMIT");return false;
            }catch{Rollback(db);throw;}
        }
    }
    public sealed partial class ReviewerEngine {
        public string RequestInspector(long groupId,long generation) {
            if(!ready || closing || faulted || groupId<1)throw new InvalidOperationException("Database or item is not ready");string id=Guid.NewGuid().ToString("N");
            var work=new DbWork {Body=db=>Catalog.Inspector(db,groupId),Done=value=>Respond(new UiResult {RequestId=id,Kind="Inspector",Generation=generation,Success=true,Inspector=(InspectorData)value}),Failed=error=>Respond(new UiResult {RequestId=id,Kind="Inspector",Generation=generation,Error=error.Message,ProtectedDetails=error is ProtectedDetailsException})};
            if(!reads.TryAdd(work,1024))throw new InvalidOperationException("Reader queue is busy; reload Details shortly.");return id;
        }
        public string RequestLocations(long groupId,string afterOccurrenceId,int limit=100) {
            if(!ready || closing || faulted)throw new InvalidOperationException("Database is not ready");if(groupId<1 || afterOccurrenceId==null || afterOccurrenceId.Length>131072 || limit<1 || limit>100)throw new ArgumentException("Choose a bounded location page of1 through100");string id=Guid.NewGuid().ToString("N");
            Catalog.ReviewUtf8.GetByteCount(afterOccurrenceId);
            var work=new DbWork {Body=db=>Catalog.Locations(db,groupId,afterOccurrenceId,limit),Done=value=>Respond(new UiResult {RequestId=id,Kind="Locations",Success=true,Locations=(LocationPage)value}),Failed=error=>Respond(new UiResult {RequestId=id,Kind="Locations",Error=error.Message,ProtectedDetails=error is ProtectedDetailsException})};
            if(!reads.TryAdd(work,1024+afterOccurrenceId.Length*2L))throw new InvalidOperationException("Reader queue is busy; reload Details shortly.");return id;
        }
        public string RequestNotes(NotesRequest request) { RequireReportWritable();
            if(!ready || closing || faulted)throw new InvalidOperationException("Database is not ready");if(request==null || request.GroupId<1 || request.ContentId<1 || request.ExpectedRevision<0 || String.IsNullOrEmpty(request.OperationId) || request.OperationId.Length>100 || request.Notes==null || request.Notes.Length>16384 || request.Reviewer==null || request.Reviewer.Length>128)throw new ArgumentException("Invalid notes action; use up to16384 note characters and128 reviewer characters");
            if(request.Notes.IndexOf('\0')>=0 || request.Reviewer.IndexOf('\0')>=0)throw new ArgumentException("Notes and reviewer labels cannot contain a null character.");Catalog.ReviewUtf8.GetByteCount(request.Notes);Catalog.ReviewUtf8.GetByteCount(request.Reviewer);
            var r=request.Copy();string id=Guid.NewGuid().ToString("N");var work=new DbWork {Body=db=>{RequireReportWritable();return (object)Catalog.Notes(db,r);},Done=value=>Respond(new UiResult {RequestId=id,Kind="Notes",OperationId=r.OperationId,Success=true,Reconciled=(bool)value}),Failed=error=>Respond(new UiResult {RequestId=id,Kind="Notes",OperationId=r.OperationId,Error=error.Message,ProtectedDetails=error is ProtectedDetailsException})};
            if(!controls.TryAdd(work,2048+2L*(r.Notes.Length+r.Reviewer.Length)))throw new InvalidOperationException("Review queue is busy; your draft is retained.");return id;
        }
    }
}
