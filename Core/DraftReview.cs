using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.ComponentModel;
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
        const string GemmaSideKey="ai.draft.gemma.side";
        const string GemmaCriteriaKey="ai.draft.gemma.criteria";
        const string GemmaCustomKey="ai.draft.gemma.custom";
        const string GemmaCustomThresholdKey="ai.draft.gemma.custom.threshold";
        static string ModelId(bool gemma){return gemma ? Gemma : Model;}
        static string ThresholdKey(bool gemma){return gemma ? "ai.draft.gemma.threshold" : "ai.draft.threshold";}
        static string ThresholdSql(bool gemma){return "COALESCE((SELECT CAST(value AS INTEGER) FROM settings WHERE key='"+ThresholdKey(gemma)+"' AND CAST(value AS INTEGER) BETWEEN 1 AND 99),"+(gemma ? DefaultGemmaThreshold : DefaultMarqoThreshold).ToString(CultureInfo.InvariantCulture)+")";}
        static string CurrentScore(string model){return "a.preview_revision=ap.revision AND ap.state='available' AND a.model='"+model+"'";}
        internal static string ScoreSql(string model){return "(SELECT a.score FROM ai_draft_scores a JOIN previews ap ON ap.content_id=a.content_id WHERE "+CurrentScore(model)+" AND a.content_id=g.content_id)";}
        internal static string SuggestionFilter(bool gemma,string model){return ScoreSql(model)+">="+ThresholdSql(gemma)+"/100.0";}
        static readonly string ReviewExtensions=ExtensionList(Formats.Deep);
        static readonly string MotionExtensions=ExtensionList(Formats.Deep.Where(Formats.IsPlayable));
        static string ExtensionList(IEnumerable<string> extensions){return String.Join(",",extensions.Select(x=>"'"+x+"'"));}
        const int BatchSize=16;

        internal static bool PrefersGemma(SqliteConnection db){return (string)db.Scalar("SELECT value FROM settings WHERE key=?",ModelKey)==Gemma;}
        internal static bool GemmaCpuOnly(SqliteConnection db){return (string)db.Scalar("SELECT value FROM settings WHERE key=?",GemmaDeviceKey)=="cpu";}
        internal static int Threshold(SqliteConnection db,bool gemma){return Convert.ToInt32(db.Scalar("SELECT "+ThresholdSql(gemma)),CultureInfo.InvariantCulture);}
        internal static int ReadSide(SqliteConnection db){int side;return Int32.TryParse((string)db.Scalar("SELECT value FROM settings WHERE key=?",GemmaSideKey),out side) && (side==512 || side==1024) ? side : 256;}
        internal static int CheckSide(int side){if(side!=256 && side!=512 && side!=1024)throw new ArgumentOutOfRangeException("side","Choose Quick, Medium or Thorough.");return side;}
        internal static int FrameLimit(int side){return CheckSide(side)==256 ? 32768 : InspectionPlan.EncodedLimit;}
        internal static string ScoreModel(string criteria,int side){CheckSide(side);return GemmaCriteria.Model(criteria)+(side==256 ? "" : ":pixels-"+side.ToString(CultureInfo.InvariantCulture));}
        internal static string ReadCriteria(SqliteConnection db){return (string)db.Scalar("SELECT value FROM settings WHERE key=?",GemmaCriteriaKey)??"";}
        internal static string ReadCustomCriteria(SqliteConnection db){string current=ReadCriteria(db);return current!="" ? current : (string)db.Scalar("SELECT value FROM settings WHERE key=?",GemmaCustomKey)??"";}
        internal static int ReadCustomThreshold(SqliteConnection db){return ReadCriteria(db)!="" ? Threshold(db,true) : Convert.ToInt32(db.Scalar("SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM settings WHERE key=? AND CAST(value AS INTEGER) BETWEEN 1 AND 99),?)",GemmaCustomThresholdKey,DefaultGemmaThreshold),CultureInfo.InvariantCulture);}
        internal static long Count(SqliteConnection db,bool gemma,string model){
            return (long)db.Scalar("SELECT COUNT(*) FROM ai_draft_scores a JOIN previews ap ON ap.content_id=a.content_id JOIN review_groups g ON g.content_id=a.content_id WHERE "+CurrentScore(model)+" AND a.score>="+ThresholdSql(gemma)+"/100.0 AND g.status='TO_REVIEW' AND g.retired=0");
        }
        internal static void SaveThreshold(SqliteConnection db,int percent,bool gemma,bool cpuOnly,string criteria,int side=256){
            CheckSide(side);
            if(percent<1 || percent>99)throw new ArgumentOutOfRangeException("percent","Enter a threshold from 1 to 99 percent.");
            db.Execute("BEGIN IMMEDIATE");
            try{
                string previous=gemma ? ReadCriteria(db) : "",remembered=gemma ? ReadCustomCriteria(db) : "";
                int previousThreshold=gemma ? Threshold(db,true) : 0;
                db.Execute("INSERT INTO settings(key,value) VALUES(?,?),(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",ThresholdKey(gemma),percent.ToString(CultureInfo.InvariantCulture),ModelKey,ModelId(gemma));
                if(gemma)db.Execute("INSERT INTO settings(key,value) VALUES(?,?),(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",GemmaDeviceKey,cpuOnly ? "cpu" : "auto",GemmaSideKey,side.ToString(CultureInfo.InvariantCulture));
                if(gemma && criteria!=null){
                    string custom=criteria!="" ? criteria : previous;
                    if(custom!="")db.Execute("INSERT INTO settings(key,value) VALUES(?,?),(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",GemmaCustomKey,custom,GemmaCustomThresholdKey,(criteria!="" ? percent : previousThreshold).ToString(CultureInfo.InvariantCulture));
                    if(criteria!=previous){
                        if(criteria!="" && remembered!="" && remembered!=criteria)foreach(int resolution in new[]{256,512,1024})db.Execute("DELETE FROM ai_draft_scores WHERE model=?",ScoreModel(remembered,resolution));
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

        internal static string Run(ReviewerEngine engine,SqliteConnection reader,IDraftClassifier classifier,bool gemma,string criteria,CancellationToken token,Action<long,long,string> progress,int side=256){
            side=gemma ? CheckSide(side) : 256;
            string model=gemma ? ScoreModel(criteria,side) : Model;
            int batchSize=gemma ? 1 : BatchSize;
            long ceiling=(long)reader.Scalar("SELECT COALESCE(MAX(content_id),0) FROM previews"),cursor=0,examined=0,scored=0,skipped=0,motionSkipped=0;
            string eligible=@"p.content_id>? AND p.content_id<=? AND p.state='available' AND length(p.bytes) BETWEEN 1 AND 32768
                AND NOT EXISTS(SELECT 1 FROM ai_draft_scores a WHERE a.content_id=p.content_id AND a.preview_revision=p.revision AND a.model=?)
                AND EXISTS(SELECT 1 FROM review_groups g WHERE g.content_id=p.content_id AND g.retired=0 AND g.status='TO_REVIEW'
                    AND EXISTS(SELECT 1 FROM group_occurrences o WHERE o.group_id=g.id AND o.extension IN("+ReviewExtensions+@") AND (o.superseded=0 OR NOT EXISTS(SELECT 1 FROM group_occurrences live WHERE live.group_id=g.id AND live.superseded=0))))";
            string motionGroup=@"(SELECT MIN(g.id) FROM review_groups g WHERE g.content_id=p.content_id AND g.retired=0 AND g.status='TO_REVIEW'
                AND EXISTS(SELECT 1 FROM group_occurrences o WHERE o.group_id=g.id AND o.extension IN("+MotionExtensions+@") AND (o.superseded=0 OR NOT EXISTS(SELECT 1 FROM group_occurrences live WHERE live.group_id=g.id AND live.superseded=0))))";
                string sourceGroup=motionGroup.Replace(MotionExtensions,ReviewExtensions);
            long total=(long)reader.Scalar("SELECT COUNT(*) FROM previews p WHERE "+eligible,0,ceiling,model);
            progress(0,total,"items examined");
            using(var decoder=new DecoderHost(Native.RuntimeDirectory)){
                while(true){
                    token.ThrowIfCancellationRequested();
                    var rows=reader.Query("SELECT p.content_id,p.revision,p.bytes,"+motionGroup+","+sourceGroup+" FROM previews p WHERE "+eligible+" ORDER BY p.content_id LIMIT "+batchSize,
                        new object[]{cursor,ceiling,model},batchSize);
                    if(rows.Count==0)break;
                    var batch=new List<ScoreRecord>(batchSize);
                    try{
                        foreach(var row in rows){
                            token.ThrowIfCancellationRequested();cursor=(long)row[0];
                            bool motion=row[3]!=null;
                            if(motion)progress(examined,total,"items examined · sampling motion frames");
                            double? score=motion ? ScoreMotion(reader,decoder,classifier,(long)row[3],cursor,token,side) : side>256 ? ScoreOriginal(reader,decoder,classifier,(long)row[4],cursor,token,side) : ScoreFrame(classifier,(byte[])row[2],token);
                            progress(++examined,total,"items examined"+(gemma && !String.IsNullOrEmpty(classifier.Device) ? " · "+classifier.Device : ""));
                            if(!score.HasValue){if(motion)motionSkipped++;else skipped++;continue;}
                            batch.Add(new ScoreRecord {Content=cursor,Revision=(long)row[1],Score=score.Value});
                        }
                    }finally{
                        foreach(var row in rows){var bytes=(byte[])row[2];Array.Clear(bytes,0,bytes.Length);}
                        if(batch.Count!=0)scored+=(int)engine.WaitWrite(db=>Store(db,batch,model));
                    }
                }
            }
            long remaining=(long)reader.Scalar("SELECT COUNT(*) FROM previews p WHERE "+eligible,0,ceiling,model);
            long noPreview=(long)reader.Scalar(@"SELECT COUNT(*) FROM (SELECT DISTINCT g.content_id FROM review_groups g WHERE g.retired=0 AND g.status='TO_REVIEW' AND EXISTS(SELECT 1 FROM group_occurrences o WHERE o.group_id=g.id AND o.extension IN("+ReviewExtensions+@") AND (o.superseded=0 OR NOT EXISTS(SELECT 1 FROM group_occurrences live WHERE live.group_id=g.id AND live.superseded=0)))) g LEFT JOIN previews p ON p.content_id=g.content_id WHERE p.content_id IS NULL OR p.state<>'available' OR p.bytes IS NULL OR length(p.bytes) NOT BETWEEN 1 AND 32768");
            string summary="AI draft check "+(remaining>0 || noPreview>0 || skipped>0 || motionSkipped>0 ? "finished with unscored items" : "complete")+
                ". "+scored.ToString("N0")+" new scores saved. "+remaining.ToString("N0")+" eligible items still lack a current AI score. "+
                noPreview.ToString("N0")+" unreviewed items have no usable preview and could not be checked. "+skipped.ToString("N0")+" still items and "+motionSkipped.ToString("N0")+" motion items could not be scored."+(gemma && side>256 ? " Larger scans require an unchanged, accessible original file." : "")+
                (gemma && !String.IsNullOrEmpty(classifier.Device) ? " Device: "+classifier.Device+"." : "")+" Open AI suggestions to review the flagged items.";
            engine.WaitWrite(db=>{Catalog.Log(db,"info","AI draft summary","",summary);return null;},4096);return summary;
        }

        static double? ScoreFrame(IDraftClassifier classifier,byte[] bytes,CancellationToken token){
            token.ThrowIfCancellationRequested();
            double? score=classifier.Score(bytes,token);
            if(score.HasValue && (Double.IsNaN(score.Value) || score.Value<0 || score.Value>1))
                throw new InvalidOperationException("The AI worker returned an invalid score.");
            return score;
        }

        static IEnumerable<object[]> AnalysisSources(SqliteConnection reader,long preferred,long content,CancellationToken token){
            var groups=new List<long>{preferred};
            if(reader.Scalar("SELECT 1 FROM contents WHERE id=? AND algorithm='sha256' AND algorithm_version=1 AND sampling=''",content)!=null)
                groups.AddRange(reader.Query("SELECT id FROM review_groups WHERE content_id=? AND id<>? AND retired=0 AND status='TO_REVIEW' ORDER BY id LIMIT 7",new object[]{content,preferred},7).Select(row=>(long)row[0]));
            foreach(long group in groups)foreach(var source in Catalog.RecordedSources(reader,group,content,false,token).Take(3))yield return source;
        }

        static double? ScoreOriginal(SqliteConnection reader,DecoderHost decoder,IDraftClassifier classifier,long group,long content,CancellationToken token,int side){
            foreach(var source in AnalysisSources(reader,group,content,token).Take(8)){
                string root=(string)source[0],relative=(string)source[1],identity=(string)source[4];
                if(String.IsNullOrEmpty(identity))continue;
                PreviewResult frame;
                try{
                    string path=Catalog.PreviewSourcePath(root,relative);long size=(long)source[2],ticks=(long)source[3];
                    frame=Archives.IsPath(path) ? Archives.WithPreviewFile(path,size,ticks,identity,token,root,
                        (file,stamp)=>decoder.CreateScoped(file,stamp.Size,stamp.ModifiedTicks,stamp.Identity,side,FrameLimit(side),token,Native.RuntimeDirectory)) :
                        decoder.CreateScoped(path,size,ticks,identity,side,FrameLimit(side),token,root);
                }catch(InvalidDataException){continue;}catch(IOException){continue;}catch(Win32Exception){continue;}catch(UnauthorizedAccessException){continue;}
                try{return ScoreFrame(classifier,frame.Bytes,token);}
                finally{Array.Clear(frame.Bytes,0,frame.Bytes.Length);}
            }
            return null;
        }

        static double? ScoreMotion(SqliteConnection reader,DecoderHost decoder,IDraftClassifier classifier,long group,long content,CancellationToken token,int side){
            foreach(var source in AnalysisSources(reader,group,content,token).Take(8)){
                string root=(string)source[0],relative=(string)source[1],identity=(string)source[4];
                if(String.IsNullOrEmpty(identity))continue;
                PreviewResult[] frames;
                try{
                    string path=Catalog.PreviewSourcePath(root,relative);
                    long size=(long)source[2],ticks=(long)source[3];
                    frames=Archives.IsPath(path) ? Archives.WithPreviewFile(path,size,ticks,identity,token,root,
                        (file,stamp)=>decoder.CreateMotion(file,stamp.Size,stamp.ModifiedTicks,stamp.Identity,token,Native.RuntimeDirectory,side,FrameLimit(side))) :
                        decoder.CreateMotion(path,size,ticks,identity,token,root,side,FrameLimit(side));
                }catch(InvalidDataException){continue;}
                catch(IOException){continue;}
                catch(Win32Exception){continue;}
                catch(UnauthorizedAccessException){continue;}
                try{
                    double? highest=null;
                    foreach(var frame in frames){
                        var score=ScoreFrame(classifier,frame.Bytes,token);
                        if(score.HasValue && (!highest.HasValue || score.Value>highest.Value))highest=score;
                    }
                    return highest;
                }finally{foreach(var frame in frames)Array.Clear(frame.Bytes,0,frame.Bytes.Length);}
            }
            return null;
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

        internal void ConfigureGemma(bool available,bool cpuOnly,string criteria,int side=256){
            lock(scanGate){
                if(!Ready || Faulted || closing || draftReviewActive)return;
                if(available)OpenGemmaSession(cpuOnly,criteria,side);
                else RetireGemma();
            }
        }

        internal string GemmaStatus {
            get{lock(scanGate)return gemmaSession==null ? "Gemma will load when the database is ready." : gemmaSession.Status;}
        }

        GemmaSession OpenGemmaSession(bool cpuOnly,string criteria,int side=256,bool retryFailed=false){
            if(gemmaSession==null || !gemmaSession.Matches(cpuOnly,criteria,side) || retryFailed && gemmaSession.Failed){
                RetireGemma();
                gemmaSession=new GemmaSession(cpuOnly,criteria,gemmaRetired,side);
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
                if(draftReviewActive || StegoAnalysisActive || scan!=null && !scan.Finished)throw new InvalidOperationException("Wait for the scan or analysis to finish.");
                draftReviewActive=true;
                try{
                    return EnqueueOperation("AI draft check",(db,token,progress)=>{
                        string criteria=gemma ? DraftReview.ReadCriteria(db) : "";int side=gemma ? DraftReview.ReadSide(db) : 256;
                        if(classifier!=null){
                            using(var model=classifier(criteria))return DraftReview.Run(this,db,model,gemma,criteria,token,progress,side);
                        }
                        GemmaSession session;
                        lock(scanGate)session=OpenGemmaSession(cpuOnly,criteria,side,true);
                        if(!session.Ready)progress(0,-1,"items examined · waiting for Gemma to load");
                        var worker=session.Wait(token);
                        try{return DraftReview.Run(this,db,worker,true,criteria,token,progress,side);}
                        catch{lock(scanGate)RetireGemma();throw;}
                    },cleanup:()=>draftReviewActive=false);
                }catch{draftReviewActive=false;throw;}
            }
        }
        public string RequestDraftThreshold(int percent,bool gemma=false,bool cpuOnly=false,string criteria=null,int side=0){
            if(side!=0)DraftReview.CheckSide(side);
            if(!ready || closing || faulted)throw new InvalidOperationException("Database is not ready.");
            var id=Guid.NewGuid().ToString("N");
            string normalized=gemma && criteria!=null ? GemmaCriteria.Normalize(criteria) : null;
            var work=new DbWork {Body=db=>{
                    int resolution=gemma && side!=0 ? side : DraftReview.ReadSide(db);
                    bool changed=gemma && (normalized!=null && normalized!=DraftReview.ReadCriteria(db) || resolution!=DraftReview.ReadSide(db));
                    if(changed)lock(scanGate){
                        if(draftReviewActive)throw new InvalidOperationException("Finish or stop the draft check before changing Gemma criteria.");
                        draftCriteriaSaving=true;
                    }
                    try{DraftReview.SaveThreshold(db,percent,gemma,cpuOnly,normalized,resolution);}
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
