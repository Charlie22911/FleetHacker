using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace MediaReviewer {
    internal sealed class StegoConfidenceDecision {
        internal bool Qualifies; internal string EvidenceKind="lead",Reason="Reliable confidence is unavailable."; internal double? Confidence=null;
    }
    internal static class StegoConfidence {
        internal const string Version="evidence-1";
        internal static StegoConfidenceDecision Evaluate(Dictionary<string,object> result,int threshold){
            if(threshold<50 || threshold>100)throw new ArgumentOutOfRangeException("threshold");
            var decision=new StegoConfidenceDecision();object raw;
            if(!result.TryGetValue("findings",out raw) || !(raw is IEnumerable))return decision;
            foreach(var f in ((IEnumerable)raw).Cast<object>().OfType<Dictionary<string,object>>()){
                object kind,sha,engine;f.TryGetValue("kind",out kind);f.TryGetValue("engine",out engine);
                if(kind as string=="validated_payload" && (engine as string=="zsteg" || engine as string=="binwalk") && f.TryGetValue("payloadSha256",out sha) && sha is string && ((string)sha).Length==64 && ((string)sha).All(Uri.IsHexDigit))
                    return new StegoConfidenceDecision {Qualifies=true,EvidenceKind="verified",Reason="Embedded file structure and integrity were verified."};
                // Statistical qualification requires an independently evaluated calibration.
                // No calibration is approved for the currently bundled partial color models.
                // Raw model outputs and imported confidence claims must not enable this path.
            }
            return decision;
        }
    }
    public enum StegoScanScope {AllUnchecked,Selected}
    public sealed class StegoScanOptions {
        public StegoScanScope Scope=StegoScanScope.AllUnchecked;public long[] SelectedGroupIds=new long[0];public int MinimumConfidencePercent=90;
        internal StegoScanOptions Snapshot(){
            if(!Enum.IsDefined(typeof(StegoScanScope),Scope) || MinimumConfidencePercent<50 || MinimumConfidencePercent>100 || SelectedGroupIds==null || SelectedGroupIds.Length>10000 || SelectedGroupIds.Any(id=>id<=0))throw new ArgumentException("Invalid scan options.");
            var ids=SelectedGroupIds.Distinct().OrderBy(id=>id).ToArray();if(Scope==StegoScanScope.Selected && ids.Length==0)throw new ArgumentException("Select photos before starting a selected scan.");
            return new StegoScanOptions {Scope=Scope,SelectedGroupIds=ids,MinimumConfidencePercent=MinimumConfidencePercent};
        }
    }
}
