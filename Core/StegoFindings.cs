using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace MediaReviewer {
    internal static class StegoFindings {
        internal const string Bucket="STEGANOGRAPHY";
        internal const string Filter="g.retired=0 AND g.status<>'EVIDENCE' AND EXISTS(SELECT 1 FROM stego_flags sf JOIN stego_runs sr ON sr.id=sf.run_id WHERE sf.group_id=g.id AND sf.content_id=g.content_id AND sf.review_operation='' AND sf.qualifies=1 AND json_extract(sr.result_json,'$.classification.policyVersion')='evidence-1' AND json_extract(sr.result_json,'$.classification.thresholdPercent')=COALESCE((SELECT CAST(value AS INTEGER) FROM settings WHERE key='stego.confidence' AND value=CAST(CAST(value AS INTEGER) AS TEXT) AND CAST(value AS INTEGER) BETWEEN 50 AND 100),90))";
        internal const string ScoreSql="(SELECT sr.suspicion_index FROM stego_flags sf JOIN stego_runs sr ON sr.id=sf.run_id WHERE sf.group_id=g.id AND sf.content_id=g.content_id)";
        static JavaScriptSerializer Json(){return new JavaScriptSerializer {MaxJsonLength=1048576,RecursionLimit=32};}
        static string Text(Dictionary<string,object> item,string key,int maximum=150){object value;if(!item.TryGetValue(key,out value)||value==null)return "";string text=Convert.ToString(value,CultureInfo.InvariantCulture).Replace('\0',' ');return text.Substring(0,Math.Min(maximum,text.Length));}
        static List<Dictionary<string,object>> Records(Dictionary<string,object> result,string key){object value;if(!result.TryGetValue(key,out value)||!(value is IEnumerable))return new List<Dictionary<string,object>>();return ((IEnumerable)value).Cast<object>().OfType<Dictionary<string,object>>().Take(1000).ToList();}
        internal static bool Finished(Dictionary<string,object> result){return new[]{"completed","partial","unsupported","limited"}.Contains(Text(result,"status")) && !Records(result,"engines").Any(e=>new[]{"failed","timed_out","unavailable","cancelled"}.Contains(Text(e,"state")));}
        static int Rank(Dictionary<string,object> f){string kind=Text(f,"kind");return kind=="validated_payload" ? 3 : kind=="statistical" ? 2 : kind=="recognized_structure" ? 0 : 1;}
        static string Method(Dictionary<string,object> f){
            string method=Text(f,"method");if(Text(f,"kind")=="validated_payload")return method.StartsWith("b",StringComparison.Ordinal) ? "ZIP archive hidden in image bits ("+(method.IndexOf(",lsb,",StringComparison.Ordinal)>=0 ? "LSB" : method.IndexOf(",msb,",StringComparison.Ordinal)>=0 ? "MSB" : "bit planes")+")" : "Embedded ZIP archive";
            switch(method){case "lsbr":return "Possible LSB replacement";case "lsbm":return "Possible LSB matching";case "hill":return "Possible HILL";case "uniw":return "Possible S-UNIWARD";case "juniw":return "Possible J-UNIWARD";case "steghide":return "Possible Steghide";case "outguess":return "Possible OutGuess";case "nsf5":return "Possible nsF5";default:return method.StartsWith("b",StringComparison.Ordinal) ? "Possible data in image bit planes ("+method+")" : "Unconfirmed "+(method.Length==0 ? "hidden-data indicator" : method)+" indicator";}
        }
        internal static string Note(Dictionary<string,object> value,string at){
            object score;long index=value.TryGetValue("suspicionIndex",out score)&&score!=null ? Convert.ToInt64(score) : 0;
            var findings=Records(value,"findings").Where(f=>Rank(f)>0).OrderByDescending(Rank).ToArray();
            int rank=findings.Select(Rank).DefaultIfEmpty(1).Max();
            var text=new StringBuilder("[Steganography scan]\n");DateTime time;string date=DateTime.TryParse(at,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time) ? time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) : at;
            text.AppendLine("Checked: "+date).AppendLine("Confidence: "+(rank==3 ? "Strong evidence - embedded file validated" : rank==2 ? "Possible indication - model result requires review" : "Low - unconfirmed pattern; may be ordinary image data"));
            object classification;if(value.TryGetValue("classification",out classification)){var json=Json();var c=json.Deserialize<Dictionary<string,object>>(json.Serialize(classification));text.AppendLine("Automatic review: "+(Convert.ToBoolean(c["qualifies"]) ? "Flagged" : "Saved lead")+" | Threshold: "+c["thresholdPercent"]+"%");text.AppendLine(Convert.ToString(c["reason"]));}
            text.AppendLine("Suspicion index: "+index+"/100 (indicator strength, not a probability)");
            foreach(var finding in findings.Take(6)){
                text.Append("Type: ").Append(Method(finding)).Append(" | Tool: ").Append(Text(finding,"engine",64));object raw;
                if(finding.TryGetValue("value",out raw)&&raw!=null)text.Append(" | Detector score: ").Append(Convert.ToDouble(raw).ToString("0.0000",CultureInfo.InvariantCulture));text.AppendLine();
                string payload=Text(finding,"payloadSha256",64);if(payload.Length>0)text.AppendLine("Payload SHA-256: "+payload);
            }
            if(findings.Length==0)text.AppendLine("Type: Unconfirmed hidden-data indicator");
            if(!Finished(value))text.AppendLine("Some checks did not finish. Run the scan again to retry them.");
            else if(Text(value,"status")=="partial")text.AppendLine("Limited check: some types of hidden data may not be detected.");
            text.Append("Original SHA-256: ").Append(Text(value,"sha256",64));return WindowsLines(text.ToString());
        }
        static string WindowsLines(string value){return value.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n");}
        static int FindNote(string text,string note,out int length){
            length=0;if(note.Length==0)return -1;
            foreach(string variant in new[]{note,WindowsLines(note),note.Replace("\r\n","\n").Replace("\r","\n")}.Distinct()){
                int index=text.IndexOf(variant,StringComparison.Ordinal);if(index>=0){length=variant.Length;return index;}
            }return -1;
        }
        static string Key(Dictionary<string,object> value){return Identity.Digest(String.Join("\n",Records(value,"findings").Where(f=>Rank(f)>0).Select(f=>Text(f,"kind")+":"+Text(f,"strength")+":"+Text(f,"engine")+":"+Text(f,"method")+":"+Text(f,"payloadSha256",64)).Distinct().OrderBy(s=>s,StringComparer.Ordinal)));}
        static string PreviousNote(SqliteConnection db,long group,long content,string run){return (string)db.Scalar("SELECT COALESCE(json_extract(result_json,'$.reviewNote'),'') FROM stego_runs WHERE group_id=? AND content_id=? AND id<>? AND length(COALESCE(json_extract(result_json,'$.reviewNote'),''))>0 ORDER BY created_at DESC,id DESC LIMIT 1",group,content,run)??"";}
        static string ExistingNotes(SqliteConnection db,long group){
            var row=db.Query("SELECT substr(CAST(notes AS BLOB),1,49153),EXISTS(SELECT 1 FROM imported_fields WHERE owner_table='review_groups' AND owner_rowid=g.id AND column_name='notes') FROM review_groups g WHERE id=?",new object[]{group},1);
            if(row.Count==0 || (long)row[0][1]!=0)return null;
            try{string text=Catalog.ReviewUtf8.GetString(row[0][0] as byte[] ?? new byte[0]);return text.Length>16384 || text.IndexOf('\0')>=0 ? null : text;}catch(DecoderFallbackException){return null;}
        }
        static void AppendNote(SqliteConnection db,long group,string note,string previous){
            string current=ExistingNotes(db,group);if(current==null)return;
            int length;int index=FindNote(current,previous,out length);
            string merged=index>=0 ? current.Remove(index,length).Insert(index,note) : current.Length==0 ? note : current+"\n\n"+note;
            if(merged.Length<=16384 && merged!=current)db.Execute("UPDATE review_groups SET notes=? WHERE id=?",merged,group);
        }
        internal static void Attach(SqliteConnection db,string run,long group,long content,string sha,string package,Dictionary<string,object> result,string at){
            int threshold=Threshold(db);var decision=StegoConfidence.Evaluate(result,threshold);result["classification"]=new {policyVersion=StegoConfidence.Version,thresholdPercent=threshold,qualifies=decision.Qualifies,evidenceKind=decision.EvidenceKind,confidence=decision.Confidence,reason=decision.Reason};
            long before=Catalog.ReviewRevision(db,group);object score;bool suspect=result.TryGetValue("suspicionIndex",out score) && score!=null && Convert.ToInt64(score)>0;
            if(!suspect){
                object ownedNote;string owned=result.TryGetValue("reviewNote",out ownedNote) && ownedNote is string ? (string)ownedNote : "";result["reviewNote"]="";
                if(Finished(result)){
                    string old=owned.Length>0 ? owned : PreviousNote(db,group,content,run);
                    if(old.Length>0){string clear="[Steganography scan]\nChecked: "+at+"\nResult: No current hidden-data indicators were found by the checks that ran.\nSome types of hidden data may not be detected.\nOriginal SHA-256: "+sha;clear=WindowsLines(clear);AppendNote(db,group,clear,old);result["reviewNote"]=clear;}
                    if(db.Execute("DELETE FROM stego_flags WHERE group_id=? AND content_id=?",group,content)>0 && Catalog.ReviewRevision(db,group)==before)db.Execute("UPDATE review_groups SET review_revision=review_revision+1 WHERE id=?",group);
                }return;
            }
            object priorNote;string previous=result.TryGetValue("reviewNote",out priorNote) && priorNote is string && ((string)priorNote).Length>0 ? (string)priorNote : PreviousNote(db,group,content,run),note=Note(result,at),key=Key(result),review="";
            var prior=db.Query("SELECT sf.review_operation,sf.indicator_key,sr.original_sha256,sr.package_sha256,sf.content_id,sf.qualifies FROM stego_flags sf JOIN stego_runs sr ON sr.id=sf.run_id WHERE sf.group_id=?",new object[]{group},1);
            if(prior.Count==1 && (long)prior[0][4]==content && (string)prior[0][1]==key && (string)prior[0][2]==sha && (string)prior[0][3]==package)review=(string)prior[0][0];
            bool material=prior.Count==0 || (long)prior[0][5]!=(decision.Qualifies ? 1 : 0) || (long)prior[0][4]!=content || (string)prior[0][1]!=key || (string)prior[0][2]!=sha || (string)prior[0][3]!=package;
            AppendNote(db,group,note,previous);if(material && Catalog.ReviewRevision(db,group)==before)db.Execute("UPDATE review_groups SET review_revision=review_revision+1 WHERE id=?",group);result["reviewNote"]=note;
            // The new run row is inserted by the caller before this flag is published.
            db.Execute("INSERT INTO stego_flags(group_id,content_id,run_id,indicator_key,review_operation,qualifies) VALUES(?,?,?,?,?,?) ON CONFLICT(group_id) DO UPDATE SET content_id=excluded.content_id,run_id=excluded.run_id,indicator_key=excluded.indicator_key,review_operation=excluded.review_operation,qualifies=excluded.qualifies",group,content,run,key,review,decision.Qualifies ? 1 : 0);
        }
        internal static int Threshold(SqliteConnection db){int n;string raw=db.Scalar("SELECT value FROM settings WHERE key='stego.confidence'") as string;return Int32.TryParse(raw,out n) && n>=50 && n<=100 && raw==n.ToString(CultureInfo.InvariantCulture) ? n : 90;}
        internal static void SetThreshold(SqliteConnection db,int value,CancellationToken token){if(value<50 || value>100)throw new ArgumentOutOfRangeException("value");Catalog.PreviewTransaction(db,()=>{db.Execute("INSERT INTO settings(key,value) VALUES('stego.confidence',?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",value.ToString(CultureInfo.InvariantCulture));Catalog.Bump(db);});Restore(db,token);}
        internal static long Count(SqliteConnection db){return Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM review_groups g WHERE "+Filter));}
        internal static string ReviewNotes(SqliteConnection db,long group,string entered){
            string canonical=(string)db.Scalar("SELECT COALESCE(json_extract(sr.result_json,'$.reviewNote'),'') FROM stego_flags sf JOIN stego_runs sr ON sr.id=sf.run_id JOIN review_groups g ON g.id=sf.group_id WHERE g.id=? AND g.content_id=sf.content_id",group)??"";
            if(canonical.Length==0)return entered;string current=ExistingNotes(db,group);if(entered.Length==0 && current!=null)return current;
            int length;if(FindNote(entered,canonical,out length)>=0)return entered;string merged=entered.Length==0 ? canonical : entered+"\n\n"+canonical;return merged.Length<=16384 ? merged : entered;
        }
        internal static string InspectorNote(SqliteConnection db,long group,long content,string shown){
            string note=(string)db.Scalar("SELECT COALESCE(json_extract(sr.result_json,'$.reviewNote'),'') FROM stego_flags sf JOIN stego_runs sr ON sr.id=sf.run_id WHERE sf.group_id=? AND sf.content_id=?",group,content)??(string)db.Scalar("SELECT COALESCE(json_extract(result_json,'$.reviewNote'),'') FROM stego_runs WHERE group_id=? AND content_id=? ORDER BY created_at DESC,id DESC LIMIT 1",group,content)??"";int length;return note.Length==0 || FindNote(shown,note,out length)>=0 ? "" : note;
        }
        internal static void Restore(SqliteConnection db,CancellationToken token){
            string after="";while(true){token.ThrowIfCancellationRequested();var rows=db.Query("SELECT r.id,r.group_id,r.content_id,r.original_sha256,r.package_sha256,r.result_json,r.created_at FROM stego_runs r JOIN review_groups g ON g.id=r.group_id AND g.content_id=r.content_id WHERE r.id>? AND g.retired=0 AND g.status<>'EVIDENCE' AND (json_extract(r.result_json,'$.classification.policyVersion') IS NULL OR json_extract(r.result_json,'$.classification.policyVersion')<>'evidence-1' OR json_extract(r.result_json,'$.classification.thresholdPercent')<>"+Threshold(db)+" OR (r.suspicion_index>0 AND NOT EXISTS(SELECT 1 FROM stego_flags sf WHERE sf.group_id=g.id AND sf.content_id=g.content_id AND sf.run_id=r.id)) OR json_type(r.result_json,'$.reviewNote') IS NULL) AND r.id=(SELECT s.id FROM stego_runs s WHERE s.group_id=g.id AND s.content_id=g.content_id AND (s.suspicion_index>0 OR ("+StegoScan.Finished+")) ORDER BY s.created_at DESC,s.id DESC LIMIT 1) ORDER BY r.id LIMIT 1",new object[]{after},1);if(rows.Count==0)return;
                var r=rows[0];after=(string)r[0];var value=Json().Deserialize<Dictionary<string,object>>((string)r[5]);Catalog.PreviewTransaction(db,()=>{Attach(db,(string)r[0],(long)r[1],(long)r[2],(string)r[3],(string)r[4],value,(string)r[6]);db.Execute("UPDATE stego_runs SET result_json=? WHERE id=?",Json().Serialize(value),r[0]);Catalog.Bump(db);});
            }
        }
    }
}
