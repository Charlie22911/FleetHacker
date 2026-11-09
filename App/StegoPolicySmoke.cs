using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
namespace MediaReviewer {
    static class StegoPolicySmoke {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static Dictionary<string,object> Result(object finding){return new Dictionary<string,object>{{"status","partial"},{"suspicionIndex",40},{"findings",new[]{finding}}};}
        internal static int Run(string directory){
            Directory.CreateDirectory(directory);
            var lead=Result(new Dictionary<string,object>{{"kind","statistical"},{"engine","aletheia"},{"value",0.9999},{"confidence",1.0},{"reliability","complete"},{"calibrationId","untrusted-import"}});
            foreach(int threshold in new[]{50,90,100})Check(!StegoConfidence.Evaluate(lead,threshold).Qualifies,"Uncalibrated model output must remain a saved lead at every threshold.");
            var payload=Result(new Dictionary<string,object>{{"kind","validated_payload"},{"engine","binwalk"},{"payloadSha256",new string('a',64)}});
            Check(StegoConfidence.Evaluate(payload,100).Qualifies && StegoConfidence.Evaluate(payload,90).EvidenceKind=="verified","Verified payloads must qualify independently of numeric confidence.");
            var invalid=Result(new Dictionary<string,object>{{"kind","validated_payload"},{"engine","binwalk"},{"payloadSha256","invalid"}});Check(!StegoConfidence.Evaluate(invalid,90).Qualifies,"Malformed payload identity cannot qualify.");
            foreach(double score in new[]{0.8999,0.90,0.9001})Check(!StegoConfidence.Evaluate(Result(new {kind="statistical",value=score}),90).Qualifies,"Raw boundary scores cannot establish reliable confidence.");
            Check(new StegoScanOptions().MinimumConfidencePercent==90,"A new confidence setting defaults to90.");
            bool rejected=false;try{new StegoScanOptions {Scope=StegoScanScope.Selected}.Snapshot();}catch(ArgumentException){rejected=true;}Check(rejected,"Empty selection must never become an all-photo scan.");
            long[] ids={2,1,2};var snapshot=new StegoScanOptions {Scope=StegoScanScope.Selected,SelectedGroupIds=ids}.Snapshot();ids[0]=99;Check(snapshot.SelectedGroupIds.SequenceEqual(new long[]{1,2}),"Scan selection must be immutable and deduplicated.");
            string path=Path.Combine(directory,"policy.sqlite");using(var db=new SqliteConnection(path,false)){
                Catalog.Initialize(db,true);Check(StegoFindings.Threshold(db)==90,"A fresh database must use90%.");
                StegoFindings.SetThreshold(db,95,CancellationToken.None);Check(StegoFindings.Threshold(db)==95,"Confidence must persist in the database.");
            }using(var db=new SqliteConnection(path,false))Check(StegoFindings.Threshold(db)==95,"Confidence must survive restart.");
            Console.WriteLine("Steganography evidence policy, invalid claims, confidence defaults/persistence and selection snapshots passed.");return 0;
        }
    }
}
