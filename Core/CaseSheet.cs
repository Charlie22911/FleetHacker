using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace MediaReviewer {
    public sealed partial class ReviewerEngine {
        sealed class RecordedSource {
            internal string Root,Relative,Identity;
            internal long Size,Ticks;
            internal string FilePath {get{return Catalog.PreviewSourcePath(Root,Relative);}}
        }

        static RecordedSource CurrentSource(SqliteConnection db,MediaRow selected) {
            var rows=db.Query(@"SELECT s.path,o.path,o.file_identity,o.size,o.modified_ticks
FROM review_groups g JOIN group_occurrences o ON o.group_id=g.id JOIN source_roots s ON s.id=o.root_id
WHERE g.id=? AND g.content_id=? AND g.retired=0 AND g.status<>'EVIDENCE' AND o.root_id=? AND o.path_key=? AND o.superseded=0 LIMIT 1",
                new object[]{selected.GroupId,selected.ContentId,selected.RootId,Paths.Key(selected.RelativePath??"")},1);
            if(rows.Count==0)throw new InvalidOperationException("This selected location changed or is no longer available. Refresh and select it again.");
            var row=rows[0];
            return new RecordedSource {Root=(string)row[0],Relative=(string)row[1],Identity=(string)row[2],Size=(long)row[3],Ticks=(long)row[4]};
        }

        static MediaRow SourceSelection(MediaRow row) {
            if(row==null || row.GroupId<=0 || row.ContentId<=0 || row.Status=="EVIDENCE" || (row.RelativePath??"").Length>32767 || (row.RootId??"").Length>4096)throw new ArgumentException("Select an ordinary review item with a recorded source.");
            return new MediaRow {GroupId=row.GroupId,ContentId=row.ContentId,RootId=row.RootId,RelativePath=row.RelativePath};
        }

        static FileStream OpenRecordedSource(RecordedSource source) {
            if(Archives.IsPath(source.Relative))throw new InvalidOperationException("This location is an archive member. Use in-app inspection or export a verified retained original.");
            if(String.IsNullOrEmpty(source.Identity))throw new InvalidOperationException("This location has no verified Windows file identity. Reconnect or rescan its source.");
            return VerifiedSourceLease(source.FilePath,source.Size,source.Ticks,source.Identity,source.Root);
        }

        static string VerifyLaunchable(RecordedSource source,FileStream file,bool videoOnly) {
            string path=Paths.OpenedSourceWithinRoot(file,source.Root);
            string extension=Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            string recordedExtension=Path.GetExtension(source.Relative).TrimStart('.').ToLowerInvariant();
            if(extension!=recordedExtension || !Formats.Supported(extension) || extension=="svg" || Formats.IsRaw(extension) || videoOnly && !Formats.IsPlayable(extension))
                throw new InvalidOperationException("This action requires a verified media extension. Use in-app inspection for this item.");
            byte[] header;using(var input=new BinaryReader(file,Encoding.UTF8,true))header=input.ReadBytes(512);
            string detected=ImageHeader.Sniff(header);
            if(detected==null || !Formats.IncludesDetected(detected,new[]{extension}))throw new InvalidDataException("The original's media signature does not match its extension. Use in-app inspection.");
            return path;
        }

        internal string RequestOpenOriginal(MediaRow row) {
            var selected=SourceSelection(row);
            return EnqueueOperation("OpenOriginal",(db,token,progress)=>{
                var source=CurrentSource(db,selected);
                using(var file=OpenRecordedSource(source)){
                    string path=VerifyLaunchable(source,file,false);
                    token.ThrowIfCancellationRequested();
                    using(var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=true})){}
                }
                return "Opened the verified media path in its Windows application.";
            },2048+2L*(selected.RelativePath??"").Length);
        }

        internal string RequestCaseSheet(MediaRow row) {
            var selected=SourceSelection(row);
            return EnqueueOperation("CaseSheet",(db,token,progress)=>{
                var review=Catalog.Inspector(db,selected.GroupId);
                if(review.ContentId!=selected.ContentId || review.Status!="NON_COMPLIANT")throw new InvalidOperationException("Select a current Non-compliant review to open its case sheet.");
                var source=CurrentSource(db,selected);
                var text=new StringBuilder();
                text.AppendLine("Review group: "+review.GroupId).AppendLine("Source root: "+source.Root).AppendLine("Recorded path: "+source.Relative);
                text.AppendLine("Original size: "+source.Size.ToString("N0")+" bytes");
                text.AppendLine("Recorded last write: "+When(source.Ticks));
                text.AppendLine((review.Algorithm=="sha256" ? "Full SHA-256: " : "Candidate fingerprint (not full-file proof): ")+review.Digest);
                text.AppendLine("Reviewer: "+review.Reviewer).AppendLine("Reviewed: "+review.ReviewedAt).AppendLine("Notes: "+review.Notes);
                string owner="Unavailable",created="Unavailable",accessed="Unavailable",availability="";
                try {
                    using(var file=OpenRecordedSource(source)){
                        token.ThrowIfCancellationRequested();string path=Paths.Io(source.FilePath);
                        created=When(File.GetCreationTimeUtc(path).Ticks);accessed=When(File.GetLastAccessTimeUtc(path).Ticks);
                        try{owner=ReadFileOwner(path);}catch(UnauthorizedAccessException){}catch(SecurityException){}catch(IOException){}
                    }
                }catch(InvalidOperationException error){availability=error.Message;}catch(UnauthorizedAccessException error){availability=error.Message;}
                catch(IOException error){availability=error.Message;}catch(System.ComponentModel.Win32Exception error){availability=error.Message;}
                text.AppendLine().AppendLine("Windows file owner: "+owner).AppendLine("File created: "+created).AppendLine("Last access timestamp: "+accessed);
                if(availability.Length!=0)text.AppendLine("Source information unavailable: "+availability);
                return text.ToString();
            },2048+2L*(selected.RelativePath??"").Length);
        }

        static string ReadFileOwner(string path) {
            var id=File.GetAccessControl(path,AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
            if(id==null)return "Unavailable";
            try{return id.Translate(typeof(NTAccount)).Value;}catch(IdentityNotMappedException){return id.Value;}
        }
        static string When(long ticks){return ticks>0 && ticks<=DateTime.MaxValue.Ticks ? new DateTime(ticks,DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) : "Unavailable";}
    }
}
