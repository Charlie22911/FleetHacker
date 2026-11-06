using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaReviewer {
    internal interface IDraftClassifier : IDisposable {
        string Device {get;}
        double? Score(byte[] preview,CancellationToken token);
    }

    internal static class DraftReview {
        internal const string Bucket="AI_SUGGESTIONS",Model="Marqo/nsfw-image-detection-384:1",Gemma="Google/gemma-4-E2B-it-Q4_0+Q8_0:1";
        internal const int DefaultMarqoThreshold=7,DefaultGemmaThreshold=55;
        const string ModelKey="ai.draft.model";
        const string GemmaDeviceKey="ai.draft.gemma.device";
        const string GemmaCriteriaKey="ai.draft.gemma.criteria";
        static string ModelId(bool gemma){return gemma ? Gemma : Model;}
        static string ThresholdKey(bool gemma){return gemma ? "ai.draft.gemma.threshold" : "ai.draft.threshold";}
        static string ThresholdSql(bool gemma){return "COALESCE((SELECT CAST(value AS INTEGER) FROM settings WHERE key='"+ThresholdKey(gemma)+"' AND CAST(value AS INTEGER) BETWEEN 1 AND 99),"+(gemma ? DefaultGemmaThreshold : DefaultMarqoThreshold).ToString(CultureInfo.InvariantCulture)+")";}
        static string CurrentScore(string model){return "a.preview_revision=ap.revision AND ap.state='available' AND a.model='"+model+"'";}
        internal static string ScoreSql(string model){return "(SELECT a.score FROM ai_draft_scores a JOIN previews ap ON ap.content_id=a.content_id WHERE "+CurrentScore(model)+" AND a.content_id=g.content_id)";}
        internal static string SuggestionFilter(bool gemma,string model){return ScoreSql(model)+">="+ThresholdSql(gemma)+"/100.0";}
        static readonly string ImageExtensions=String.Join(",",Formats.Deep.Where(x=>!Formats.IsPlayable(x)).Select(x=>"'"+x+"'"));
        const int BatchSize=16;

        internal static bool PrefersGemma(SqliteConnection db){return (string)db.Scalar("SELECT value FROM settings WHERE key=?",ModelKey)==Gemma;}
        internal static bool GemmaCpuOnly(SqliteConnection db){return (string)db.Scalar("SELECT value FROM settings WHERE key=?",GemmaDeviceKey)=="cpu";}
        internal static int Threshold(SqliteConnection db,bool gemma){return Convert.ToInt32(db.Scalar("SELECT "+ThresholdSql(gemma)),CultureInfo.InvariantCulture);}
        internal static string ReadCriteria(SqliteConnection db){return (string)db.Scalar("SELECT value FROM settings WHERE key=?",GemmaCriteriaKey)??"";}
        internal static long Count(SqliteConnection db,bool gemma,string model){
            return (long)db.Scalar("SELECT COUNT(*) FROM ai_draft_scores a JOIN previews ap ON ap.content_id=a.content_id JOIN review_groups g ON g.content_id=a.content_id WHERE "+CurrentScore(model)+" AND a.score>="+ThresholdSql(gemma)+"/100.0 AND g.status='TO_REVIEW' AND g.retired=0");
        }
        internal static void SaveThreshold(SqliteConnection db,int percent,bool gemma,bool cpuOnly,string criteria){
            if(percent<1 || percent>99)throw new ArgumentOutOfRangeException("percent","Enter a threshold from 1 to 99 percent.");
            db.Execute("BEGIN IMMEDIATE");
            try{
                db.Execute("INSERT INTO settings(key,value) VALUES(?,?),(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",ThresholdKey(gemma),percent.ToString(CultureInfo.InvariantCulture),ModelKey,ModelId(gemma));
                if(gemma)db.Execute("INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",GemmaDeviceKey,cpuOnly ? "cpu" : "auto");
                if(gemma && criteria!=null){
                    string previous=ReadCriteria(db);
                    if(criteria!=previous){
                        if(previous!="")db.Execute("DELETE FROM ai_draft_scores WHERE model=?",GemmaCriteria.Model(previous));
                        db.Execute("INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",GemmaCriteriaKey,criteria);
                    }
                }
                Catalog.Bump(db);db.Execute("COMMIT");
            }catch{Catalog.Rollback(db);throw;}
        }

        sealed class ScoreRecord {
            internal long Content,Revision;
            internal double Score;
        }

        internal static string Run(ReviewerEngine engine,SqliteConnection reader,IDraftClassifier classifier,bool gemma,string criteria,CancellationToken token,Action<long,long,string> progress){
            string model=gemma ? GemmaCriteria.Model(criteria) : Model;
            int batchSize=gemma ? 1 : BatchSize;
            long ceiling=(long)reader.Scalar("SELECT COALESCE(MAX(content_id),0) FROM previews"),cursor=0,examined=0,scored=0,skipped=0;
            string eligible=@"p.content_id>? AND p.content_id<=? AND p.state='available' AND length(p.bytes) BETWEEN 1 AND 32768
                AND NOT EXISTS(SELECT 1 FROM ai_draft_scores a WHERE a.content_id=p.content_id AND a.preview_revision=p.revision AND a.model=?)
                AND EXISTS(SELECT 1 FROM review_groups g WHERE g.content_id=p.content_id AND g.retired=0 AND g.status='TO_REVIEW'
                    AND EXISTS(SELECT 1 FROM group_occurrences o WHERE o.group_id=g.id AND o.extension IN("+ImageExtensions+") AND (o.superseded=0 OR NOT EXISTS(SELECT 1 FROM group_occurrences live WHERE live.group_id=g.id AND live.superseded=0))))";
            progress(0,-1,"previews examined");
            while(true){
                token.ThrowIfCancellationRequested();
                var rows=reader.Query("SELECT p.content_id,p.revision,p.bytes FROM previews p WHERE "+eligible+" ORDER BY p.content_id LIMIT "+batchSize,
                    new object[]{cursor,ceiling,model},batchSize);
                if(rows.Count==0)break;
                var batch=new List<ScoreRecord>(batchSize);
                try{
                    foreach(var row in rows){
                        token.ThrowIfCancellationRequested();cursor=(long)row[0];
                        double? score=classifier.Score((byte[])row[2],token);
                        progress(++examined,-1,"previews examined"+(gemma && !String.IsNullOrEmpty(classifier.Device) ? " · "+classifier.Device : ""));
                        if(!score.HasValue){skipped++;continue;}
                        if(Double.IsNaN(score.Value) || score.Value<0 || score.Value>1)throw new InvalidOperationException("The AI worker returned an invalid score.");
                        batch.Add(new ScoreRecord {Content=cursor,Revision=(long)row[1],Score=score.Value});
                    }
                }finally{
                    foreach(var row in rows){var bytes=(byte[])row[2];Array.Clear(bytes,0,bytes.Length);}
                    if(batch.Count!=0)scored+=(int)engine.WaitWrite(db=>Store(db,batch,model));
                }
            }
            return "AI draft check complete"+(gemma && !String.IsNullOrEmpty(classifier.Device) ? " · "+classifier.Device : "")+". "+scored.ToString("N0")+" new scores saved, "+skipped.ToString("N0")+" unreadable previews skipped. Open AI suggestions to review the flagged items.";
        }

        static int Store(SqliteConnection db,List<ScoreRecord> batch,string model){
            db.Execute("BEGIN IMMEDIATE");
            try{
                int changed=0;
                using(var statement=db.Prepare(@"INSERT INTO ai_draft_scores(content_id,preview_revision,model,score)
                    SELECT content_id,revision,?,? FROM previews WHERE content_id=? AND revision=? AND state='available'
                    ON CONFLICT(content_id,model) DO UPDATE SET preview_revision=excluded.preview_revision,score=excluded.score")){
                    foreach(var row in batch){statement.Bind(model,row.Score,row.Content,row.Revision);statement.Step();changed+=Sql.sqlite3_changes(db.Handle);}
                }
                if(changed!=0)Catalog.Bump(db);
                db.Execute("COMMIT");return changed;
            }catch{Catalog.Rollback(db);throw;}
        }
    }

    public sealed partial class ReviewerEngine {
        volatile bool draftReviewActive;
        bool draftCriteriaSaving;
        GemmaSession gemmaSession;
        Task gemmaRetired;

        internal void ConfigureGemma(bool available,bool cpuOnly,string criteria){
            lock(scanGate){
                if(!Ready || Faulted || closing || draftReviewActive)return;
                if(available)OpenGemmaSession(cpuOnly,criteria);
                else RetireGemma();
            }
        }

        internal string GemmaStatus {
            get{lock(scanGate)return gemmaSession==null ? "Gemma will load when the catalog is ready." : gemmaSession.Status;}
        }

        GemmaSession OpenGemmaSession(bool cpuOnly,string criteria,bool retryFailed=false){
            if(gemmaSession==null || !gemmaSession.Matches(cpuOnly,criteria) || retryFailed && gemmaSession.Failed){
                RetireGemma();
                gemmaSession=new GemmaSession(cpuOnly,criteria,gemmaRetired);
            }
            return gemmaSession;
        }

        void RetireGemma(){
            if(gemmaSession==null)return;
            gemmaRetired=gemmaSession.Retire();gemmaSession=null;
        }

        void StopGemma(){
            Task retired;
            lock(scanGate){RetireGemma();retired=gemmaRetired;}
            if(retired!=null)try{retired.GetAwaiter().GetResult();}
            catch(Exception error){Update(s=>s.Error="Gemma worker cleanup failed: "+error.GetBaseException().Message);}
        }
        public bool DraftReviewActive {get{return draftReviewActive;}}
        public string RequestDraftReview(bool gemma=false,bool cpuOnly=false){
            if(gemma)return EnqueueDraftReview(null,true,cpuOnly);
            return RequestDraftReview(()=>new DraftModelWorker());
        }
        internal string RequestDraftReview(Func<IDraftClassifier> classifier,bool gemma=false){
            return EnqueueDraftReview(criteria=>classifier(),gemma);
        }
        string EnqueueDraftReview(Func<string,IDraftClassifier> classifier,bool gemma,bool cpuOnly=false){
            lock(scanGate){
                if(draftCriteriaSaving)throw new InvalidOperationException("Wait for the Gemma criteria to finish saving.");
                if(draftReviewActive || scan!=null && !scan.Finished)throw new InvalidOperationException("Wait for the scan or AI draft check to finish.");
                draftReviewActive=true;
                try{
                    return EnqueueOperation("AI draft check",(db,token,progress)=>{
                        string criteria=gemma ? DraftReview.ReadCriteria(db) : "";
                        if(classifier!=null){
                            using(var model=classifier(criteria))return DraftReview.Run(this,db,model,gemma,criteria,token,progress);
                        }
                        GemmaSession session;
                        lock(scanGate)session=OpenGemmaSession(cpuOnly,criteria,true);
                        if(!session.Ready)progress(0,-1,"previews examined · waiting for Gemma to load");
                        var worker=session.Wait(token);
                        try{return DraftReview.Run(this,db,worker,true,criteria,token,progress);}
                        catch{lock(scanGate)RetireGemma();throw;}
                    },cleanup:()=>draftReviewActive=false);
                }catch{draftReviewActive=false;throw;}
            }
        }
        public string RequestDraftThreshold(int percent,bool gemma=false,bool cpuOnly=false,string criteria=null){
            if(!ready || closing || faulted)throw new InvalidOperationException("Database is not ready.");
            var id=Guid.NewGuid().ToString("N");
            string normalized=gemma && criteria!=null ? GemmaCriteria.Normalize(criteria) : null;
            var work=new DbWork {Body=db=>{
                    bool changed=normalized!=null && normalized!=DraftReview.ReadCriteria(db);
                    if(changed)lock(scanGate){
                        if(draftReviewActive)throw new InvalidOperationException("Finish or stop the draft check before changing Gemma criteria.");
                        draftCriteriaSaving=true;
                    }
                    try{DraftReview.SaveThreshold(db,percent,gemma,cpuOnly,normalized);}
                    finally{if(changed)lock(scanGate)draftCriteriaSaving=false;}
                    return null;
                },
                Done=value=>Respond(new UiResult {RequestId=id,Kind="AI threshold",Success=true,Value="AI settings saved."}),
                Failed=error=>Respond(new UiResult {RequestId=id,Kind="AI threshold",Error=error.Message})};
            if(!controls.TryAdd(work,1024))throw new InvalidOperationException("The database is busy. Try saving the AI threshold again.");
            return id;
        }
    }
}
