using System;
using System.Collections.Generic;
namespace MediaReviewer {
    public static partial class Catalog {
        // Protected cards do not require a reachable or exposed source location.
        internal static PageData ProtectedPage(SqliteConnection db,PageRequest r){
            var data=new PageData {Totals=Totals(db),Revision=Revision(db),PageSize=r.PageSize};
            string filter="status='EVIDENCE' AND retired=0";var args=new List<object>();if(!String.IsNullOrEmpty(r.RootId)){filter+=" AND root_id=?";args.Add(r.RootId);}
            if(r.Filter=="verified")filter+=" AND (EXISTS(SELECT 1 FROM original_links l WHERE l.group_id=review_groups.id) OR EXISTS(SELECT 1 FROM legacy_original_links l WHERE l.group_id=review_groups.id))";
            if(!String.IsNullOrEmpty(r.Search) || !String.IsNullOrEmpty(r.Folder) || !String.IsNullOrEmpty(r.Type) || (!String.IsNullOrEmpty(r.Filter) && r.Filter!="verified"))filter+=" AND 0";
            data.Total=Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM review_groups WHERE "+filter,args.ToArray()));data.Page=(int)Math.Min(r.Page,Math.Max(1,(data.Total+r.PageSize-1)/r.PageSize));
            args.Add(r.PageSize);args.Add((long)(data.Page-1)*r.PageSize);
            foreach(var row in db.Query("SELECT id,content_id,display_sequence,root_id,review_revision,NOT EXISTS(SELECT 1 FROM original_links l WHERE l.group_id=review_groups.id) AND EXISTS(SELECT 1 FROM historical_evidence h WHERE h.group_id=review_groups.id AND h.content_id=review_groups.content_id AND h.state='complete') FROM review_groups WHERE "+filter+" ORDER BY display_sequence LIMIT ? OFFSET ?",args.ToArray(),r.PageSize)){
                bool historical=(long)row[5]!=0;
                data.Rows.Add(new MediaRow {GroupId=(long)row[0],ContentId=(long)row[1],Sequence=(long)row[2],RootId=(string)row[3],RootPath="",RelativePath="",Name=(historical ? "Historical Evidence " : "Protected original ")+row[0],Extension="",Hash="",Status="EVIDENCE",Reviewer="",Notes="",PreviewState="protected",PreviewError="Unlock and inspect the saved Evidence",Algorithm=historical ? "historical" : "sha256",OriginalRetained=!historical,HistoricalEvidence=historical,ReviewRevision=(long)row[4]});
            }return data;
        }
    }
}
