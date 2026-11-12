using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
namespace MediaReviewer {
    internal static class HistoricalEvidence {
        internal const string Schema=@"CREATE TABLE historical_evidence(
 id TEXT PRIMARY KEY,request_id TEXT NOT NULL REFERENCES original_requests(id),group_id INTEGER NOT NULL REFERENCES review_groups(id),
 content_id INTEGER NOT NULL REFERENCES contents(id),decision_key TEXT NOT NULL,hash TEXT NOT NULL,size INTEGER NOT NULL,
 state TEXT NOT NULL,created_at TEXT NOT NULL,metadata_iv BLOB NOT NULL,metadata_ciphertext BLOB NOT NULL,preview_iv BLOB,preview_ciphertext BLOB);
CREATE INDEX historical_evidence_group ON historical_evidence(group_id,content_id,state);";
        internal static string CaptureId(SqliteConnection db,long group){return db.Scalar("SELECT h.id FROM historical_evidence h JOIN review_groups g ON g.id=h.group_id AND g.content_id=h.content_id AND g.legacy_key=h.decision_key WHERE h.group_id=? AND h.state='complete' ORDER BY h.rowid DESC LIMIT 1",group) as string;}
        internal static void Validate(Dictionary<string,object> metadata,long group,string capture,string key,string hash,long size){
            if(Convert.ToString(metadata["format"])!="media-reviewer-history/v1" || Convert.ToInt64(metadata["groupId"])!=group || (string)metadata["captureId"]!=capture || (string)metadata["decisionKey"]!=key || (string)metadata["recordedHash"]!=hash || Convert.ToInt64(metadata["originalSize"])!=size || Convert.ToBoolean(metadata["originalAvailable"]))throw new InvalidDataException("Authenticated historical Evidence disagrees with its saved identity.");
        }
    }
    public sealed partial class ReviewerEngine {
        string CaptureHistoricalEvidence(SqliteConnection db,ReviewRequest request,long group,object[] info,long epoch,CancellationToken token){
            RequireVault(epoch,token);var saved=Catalog.Inspector(db,group);string capture=Guid.NewGuid().ToString("N"),key=(string)info[1],hash=(string)info[2],workspace=EvidenceFormat.Workspace(db);long size=(long)info[3];
            var copies=new List<object>();SavedFileProperties properties=null;string path="",root="",name="Saved item "+group;bool bounded=saved.TextReadOnly;long locationCost=0;
            var locations=db.Query("SELECT DISTINCT substr(o.id,1,131072),substr(o.path,1,32767),substr(r.path,1,32768),o.size,o.modified_ticks,o.superseded,substr(COALESCE(o.file_identity,''),1,1024) FROM group_occurrences o JOIN source_roots r ON r.id=o.root_id WHERE o.group_id=? ORDER BY o.occurrence_order LIMIT 101",new object[]{group},101);
            foreach(var row in locations){token.ThrowIfCancellationRequested();var location=new LocationRow {Id=(string)row[0],Path=(string)row[1],RootPath=(string)row[2],Size=(long)row[3],ModifiedTicks=(long)row[4],Historical=(long)row[5]!=0,FileIdentity=(string)row[6]};location.Properties=FilePropertiesData.Read(db,location.Id);
                if(copies.Count==100 || locationCost+location.Cost>512*1024){bounded=true;break;}locationCost+=location.Cost;
                if(copies.Count==0){path=location.Path;root=location.RootPath;name=Path.GetFileName(path);properties=location.Properties;}
                copies.Add(new Dictionary<string,object>{{"path",location.Path},{"rootPath",location.RootPath},{"size",location.Size},{"modifiedTicks",location.ModifiedTicks},{"historical",location.Historical},{"extension",Path.GetExtension(location.Path).TrimStart('.')},{"fileProperties",location.Properties}});
            }
            var metadata=new Dictionary<string,object>{{"format","media-reviewer-history/v1"},{"captureId",capture},{"groupId",group},{"contentId",info[0]},{"decisionKey",key},{"recordedHash",hash},{"algorithm",saved.Algorithm},{"originalSize",size},{"originalAvailable",false},{"capturedAt",Catalog.Now()},{"name",name},{"path",path},{"rootPath",root},{"reviewer",request.Reviewer},{"notes",request.Notes},{"previousReviewer",saved.Reviewer},{"previousNotes",saved.Notes},{"steganographyNotes",saved.StegoNotes},{"occurrences",copies.ToArray()},{"fileProperties",properties}};
            if(bounded)metadata.Add("historyNotice","This protected view contains a bounded snapshot. All saved locations, review history and extended text remain preserved in the database and its portable backups.");
            byte[] metadataIv=EvidenceFormat.Random(12),cipher,previewIv=null,previewCipher=null;
            byte[] preview=db.Scalar("SELECT bytes FROM previews WHERE content_id=? AND state='available' AND length(bytes)<=32768",info[0]) as byte[];
            string earlier=HistoricalEvidence.CaptureId(db,group);if(preview==null && earlier!=null)using(var prior=ReadHistoricalEvidence(db,group,earlier,epoch,token)){if(prior.Preview!=null)preview=(byte[])prior.Preview.Clone();}
            metadata.Add("previewAvailable",preview!=null);
            byte[] plain=Encoding.UTF8.GetBytes(EvidenceFormat.Json().Serialize(metadata));
            try{cipher=Crypto.Encrypt(vaultKey,metadataIv,EvidenceFormat.Aad(workspace,key,hash,"metadata",4,capture,size,0),plain);}finally{Array.Clear(plain,0,plain.Length);}
            if(preview!=null)try{previewIv=EvidenceFormat.Random(12);previewCipher=Crypto.Encrypt(vaultKey,previewIv,EvidenceFormat.Aad(workspace,key,hash,"preview",4,capture,size,0),preview);}finally{Array.Clear(preview,0,preview.Length);}
            WaitWrite(w=>{RequireVault(epoch,token);w.Execute("INSERT INTO historical_evidence VALUES(?,?,?,?,?,?,?,'staging',?,?,?,?,?)",capture,request.OperationId,group,info[0],key,hash,size,Catalog.Now(),metadataIv,cipher,previewIv,previewCipher);return null;},cipher.Length+(previewCipher==null ? 0 : previewCipher.Length)+2048);
            using(var authenticated=ReadHistoricalEvidence(db,group,capture,epoch,token,true)){}return capture;
        }
        EvidenceInspection ReadHistoricalEvidence(SqliteConnection db,long group,string capture,long epoch,CancellationToken token,bool staged=false){
            RequireVault(epoch,token);var rows=db.Query("SELECT h.decision_key,h.hash,h.size,h.metadata_iv,h.metadata_ciphertext,h.preview_iv,h.preview_ciphertext,h.state,c.id,c.algorithm FROM historical_evidence h JOIN review_groups g ON g.id=h.group_id AND g.content_id=h.content_id AND g.legacy_key=h.decision_key JOIN contents c ON c.id=g.content_id AND c.digest=h.hash AND c.size=h.size WHERE h.id=? AND h.group_id=? AND length(h.decision_key)<=4096 AND length(h.hash)<=256 AND length(h.metadata_iv)=12 AND length(h.metadata_ciphertext)<=4194320 AND ((h.preview_ciphertext IS NULL AND h.preview_iv IS NULL) OR (length(h.preview_iv)=12 AND length(h.preview_ciphertext)<=32784))",new object[]{capture,group},1);
            if(rows.Count!=1 || (string)rows[0][7]!=(staged ? "staging" : "complete"))throw new InvalidDataException("No authenticated historical Evidence is available for this item.");var row=rows[0];string key=(string)row[0],hash=(string)row[1];long size=(long)row[2];var result=new EvidenceInspection {GroupId=group,Epoch=epoch,Hash=hash,Size=size,Historical=true};
            try{result.Metadata=Crypto.Decrypt(vaultKey,(byte[])row[3],EvidenceFormat.Aad(EvidenceFormat.Workspace(db),key,hash,"metadata",4,capture,size,0),(byte[])row[4]);
                var metadata=EvidenceFormat.Json().Deserialize<Dictionary<string,object>>(Catalog.ReviewUtf8.GetString(result.Metadata));HistoricalEvidence.Validate(metadata,group,capture,key,hash,size);
                if(Convert.ToInt64(metadata["contentId"])!=(long)row[8] || (string)metadata["algorithm"]!=(string)row[9] || Convert.ToBoolean(metadata["previewAvailable"])!=(row[5]!=null))throw new InvalidDataException("Historical Evidence no longer matches the selected content or saved preview.");
                if(row[5]!=null)result.Preview=Crypto.Decrypt(vaultKey,(byte[])row[5],EvidenceFormat.Aad(EvidenceFormat.Workspace(db),key,hash,"preview",4,capture,size,0),(byte[])row[6]);
                RequireVault(epoch,token);return result;
            }catch{result.Dispose();throw;}
        }
    }
}
