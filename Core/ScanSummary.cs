using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace MediaReviewer {
    public static partial class Catalog {
        internal static long FinishedFileJobs(IEnumerable<object[]> rows){return rows.Where(r=>(string)r[0]=="file" && new[]{"complete","skipped","failed"}.Contains((string)r[1])).Sum(r=>(long)r[2]);}
        internal static string ScanStateSummary(SqliteConnection db,string scan,string state){
            bool complete=state=="complete" || state=="complete_errors",active=state=="running" || state=="cancelling" || state=="preparing";
            return ScanCompletionSummary(db,scan,complete,complete || active || state=="cancelled" ? null : "The scan did not finish; inspect its saved state.",active);
        }
        internal static string ScanCompletionSummary(SqliteConnection db,string scan,bool complete,string failure,bool active=false){
            var rows=db.Query("SELECT kind,state,total FROM scan_counters WHERE scan_id=?",new object[]{scan},40);
            Func<string,string,long> count=(kind,state)=>rows.Where(r=>(string)r[0]==kind && (string)r[1]==state).Sum(r=>(long)r[2]);
            long files=count("file","complete"),skipped=count("file","skipped"),reads=count("file","failed"),listing=count("directory","failed")+count("archive","failed"),previews=count("preview","complete"),badPreviews=count("preview","failed");
            long pending=rows.Where(r=>(string)r[1]=="pending" || (string)r[1]=="processing").Sum(r=>(long)r[2]);
            string heading=active ? "Scan in progress." : !complete ? failure==null ? "Scan cancelled; resume is available." : "Scan stopped: "+failure : listing>0 ? "Scan finished; some folders or archives could not be read." : reads>0 ? "Scan finished; some files could not be read." : badPreviews>0 ? "Scan completed with preview failures." : "Scan completed.";
            return heading+String.Format(CultureInfo.CurrentCulture," {0:N0} files fingerprinted, {1:N0} skipped, {2:N0} file read failures, {3:N0} folder/archive failures. {4:N0} previews created, {5:N0} preview"+(badPreviews==1 ? "" : "s")+" failed, {6:N0} jobs pending.",files,skipped,reads,listing,previews,badPreviews,pending)+(badPreviews>0 ? " Fingerprints remain saved when previews fail; AI cannot check items without usable previews." : "");
        }
    }
}
