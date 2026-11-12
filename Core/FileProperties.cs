using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
namespace MediaReviewer {
    public sealed class SavedFileProperties {
        public long? CreatedTicks;
        public long ModifiedTicks,Size;
        public string OwnerSid="",OwnerName="",OwnerState="unavailable",OwnerError="",ObservedAt="",ObservedPath="",FileIdentity="",Origin="file";
        internal long Cost {get{return 256+2L*(OwnerSid.Length+OwnerName.Length+OwnerState.Length+OwnerError.Length+ObservedAt.Length+ObservedPath.Length+FileIdentity.Length+Origin.Length);}}
        internal static string Time(long? ticks){return ticks.HasValue && ticks.Value>0 && ticks.Value<=DateTime.MaxValue.Ticks ? new DateTime(ticks.Value,DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) : "Unavailable";}
        internal string Owner {get{return OwnerName.Length>0 ? OwnerName : OwnerSid.Length>0 ? OwnerSid : Origin=="archive_member" ? "Not applicable to an archive entry" : "Unavailable";}}
        internal string Describe(string currentPath=null,string currentIdentity=null){
            var text=new StringBuilder();text.AppendLine("Windows owner: "+Owner).AppendLine("Owner SID: "+(OwnerSid.Length==0 ? "Unavailable" : OwnerSid));
            text.AppendLine("Date created: "+Time(CreatedTicks)).AppendLine((Origin=="archive_member" ? "Archive entry modified: " : "Date modified: ")+Time(ModifiedTicks));
            DateTime observed;string at=DateTime.TryParse(ObservedAt,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out observed) ? observed.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) : ObservedAt;
            text.AppendLine("Properties recorded: "+at).Append("Recorded path: ").Append(ObservedPath);
            if(currentPath!=null && (!String.Equals(currentPath,ObservedPath,StringComparison.OrdinalIgnoreCase) || currentIdentity!=null && currentIdentity!=FileIdentity))text.AppendLine().Append("These properties belong to an earlier source observation. Refresh or rescan to update them.");
            if(OwnerError.Length>0)text.AppendLine().Append(OwnerError);return text.ToString();
        }
        internal static SavedFileProperties Archive(string path,long size,long modified,string identity){return new SavedFileProperties {Size=size,ModifiedTicks=modified,FileIdentity=identity??"",Origin="archive_member",OwnerState="not_applicable",ObservedAt=Catalog.Now(),ObservedPath=Paths.Friendly(path),OwnerError="Archive entries do not record Windows ownership or a reliable filesystem creation date."};}
    }
    internal static class FilePropertyCapture {
        [DllImport("advapi32.dll")] static extern uint GetSecurityInfo(IntPtr handle,int kind,uint information,out IntPtr owner,out IntPtr group,out IntPtr dacl,out IntPtr sacl,out IntPtr descriptor);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
        static readonly object gate=new object();static readonly Dictionary<string,string> names=new Dictionary<string,string>(StringComparer.Ordinal);
        static string Name(SecurityIdentifier sid){
            lock(gate){string value;if(names.TryGetValue(sid.Value,out value))return value;if(names.Count>=512)return "";}
            string result="";try{result=sid.Translate(typeof(NTAccount)).Value;}catch(IdentityNotMappedException){}catch(SecurityException){}catch(SystemException){}
            if(result.Length>512)result=result.Substring(0,512);lock(gate){if(names.Count<512)names[sid.Value]=result;}return result;
        }
        internal static SavedFileProperties Read(FileStream source,string path){
            var stamp=FileAccessNative.Stamp(source);var result=new SavedFileProperties {CreatedTicks=stamp.CreatedTicks>0 ? (long?)stamp.CreatedTicks : null,ModifiedTicks=stamp.ModifiedTicks,Size=stamp.Size,FileIdentity=stamp.Identity,ObservedPath=Paths.Friendly(path),ObservedAt=Catalog.Now()};
            IntPtr owner,group,dacl,sacl,descriptor=IntPtr.Zero;
            try{
                uint code=GetSecurityInfo(source.SafeFileHandle.DangerousGetHandle(),1,1,out owner,out group,out dacl,out sacl,out descriptor);
                if(code==0 && owner!=IntPtr.Zero){var sid=new SecurityIdentifier(owner);result.OwnerSid=sid.Value;result.OwnerName=Name(sid);result.OwnerState="available";}
                else{result.OwnerState=code==5 ? "access_denied" : "unavailable";result.OwnerError=code==5 ? "Windows did not allow reading the file owner." : "Windows could not read the file owner (error "+code+").";}
            }catch(SecurityException){result.OwnerState="access_denied";result.OwnerError="Windows did not allow reading the file owner.";}
            catch(SystemException error){result.OwnerError="File owner unavailable: "+error.GetType().Name;}
            finally{if(descriptor!=IntPtr.Zero)LocalFree(descriptor);}
            return result;
        }
    }
    internal static class FilePropertiesData {
        static string Text(Dictionary<string,object> value,string key,int limit){object raw;string text=value.TryGetValue(key,out raw) && raw!=null ? Convert.ToString(raw,CultureInfo.InvariantCulture) : "";return text.Length<=limit ? text : text.Substring(0,limit);}
        static long? Number(Dictionary<string,object> value,string key){long number;return Int64.TryParse(Text(value,key,32),NumberStyles.Integer,CultureInfo.InvariantCulture,out number) && number>=0 && number<=DateTime.MaxValue.Ticks ? (long?)number : null;}
        internal static SavedFileProperties FromMetadata(object raw){var value=raw as Dictionary<string,object>;if(value==null)return null;return new SavedFileProperties {CreatedTicks=Number(value,"CreatedTicks"),ModifiedTicks=Number(value,"ModifiedTicks")??0,OwnerSid=Text(value,"OwnerSid",256),OwnerName=Text(value,"OwnerName",512),OwnerState=Text(value,"OwnerState",32),OwnerError=Text(value,"OwnerError",1024),ObservedAt=Text(value,"ObservedAt",64),ObservedPath=Text(value,"ObservedPath",65536),FileIdentity=Text(value,"FileIdentity",1024),Origin=Text(value,"Origin",32)};}
        internal const string Schema=@"CREATE TABLE file_properties(
 occurrence_id TEXT PRIMARY KEY REFERENCES occurrences(id) ON DELETE CASCADE,
 created_ticks INTEGER,modified_ticks INTEGER NOT NULL,owner_sid TEXT NOT NULL,owner_name TEXT NOT NULL,
 owner_state TEXT NOT NULL,owner_error TEXT NOT NULL,observed_at TEXT NOT NULL,observed_path TEXT NOT NULL,
 file_identity TEXT NOT NULL,size INTEGER NOT NULL,origin TEXT NOT NULL);";
        internal static void Store(SqliteConnection db,string occurrence,SavedFileProperties value){
            if(value==null)return;
            if(value.CreatedTicks.HasValue && (value.CreatedTicks<0 || value.CreatedTicks>DateTime.MaxValue.Ticks) || value.ModifiedTicks<0 || value.ModifiedTicks>DateTime.MaxValue.Ticks || value.Size<0 || value.OwnerSid.Length>256 || value.OwnerName.Length>512 || value.OwnerError.Length>1024 || value.ObservedAt.Length>64 || value.ObservedPath.Length>65536 || value.FileIdentity.Length>1024 || !new[]{"available","unavailable","access_denied","not_applicable"}.Contains(value.OwnerState) || !new[]{"file","archive_member"}.Contains(value.Origin))throw new InvalidDataException("File properties exceed their supported bounds.");
            db.Execute(@"INSERT INTO file_properties VALUES(?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(occurrence_id) DO UPDATE SET
 created_ticks=excluded.created_ticks,modified_ticks=excluded.modified_ticks,owner_sid=excluded.owner_sid,owner_name=excluded.owner_name,
 owner_state=excluded.owner_state,owner_error=excluded.owner_error,observed_at=excluded.observed_at,observed_path=excluded.observed_path,
 file_identity=excluded.file_identity,size=excluded.size,origin=excluded.origin",occurrence,value.CreatedTicks,value.ModifiedTicks,value.OwnerSid,value.OwnerName,value.OwnerState,value.OwnerError,value.ObservedAt,value.ObservedPath,value.FileIdentity,value.Size,value.Origin);
        }
        internal static SavedFileProperties Read(SqliteConnection db,string occurrence){
            if(occurrence==null)return null;
            var rows=db.Query("SELECT created_ticks,modified_ticks,substr(owner_sid,1,256),substr(owner_name,1,512),substr(owner_state,1,32),substr(owner_error,1,1024),substr(observed_at,1,64),substr(observed_path,1,65536),substr(file_identity,1,1024),size,substr(CAST(origin AS TEXT),1,33) FROM file_properties WHERE occurrence_id=?",new object[]{occurrence},1);
            if(rows.Count==0)return null;var row=rows[0];var result=new SavedFileProperties {CreatedTicks=row[0]==null ? (long?)null : (long)row[0],ModifiedTicks=(long)row[1],OwnerSid=(string)row[2],OwnerName=(string)row[3],OwnerState=(string)row[4],OwnerError=(string)row[5],ObservedAt=(string)row[6],ObservedPath=(string)row[7],FileIdentity=(string)row[8],Size=(long)row[9],Origin=(string)row[10]};
            if(result.Origin!="file" && result.Origin!="archive_member"){result.Origin="unknown";result.OwnerState="unavailable";result.OwnerError="Recorded file properties have an unsupported source type.";}return result;
        }
    }
    public sealed partial class ReviewerEngine {
        public string RequestFilePropertiesRefresh(long groupId,string occurrenceId){
            RequireReportWritable();
            if(groupId<1 || String.IsNullOrEmpty(occurrenceId) || occurrenceId.Length>131072)throw new ArgumentException("Select one recorded file location.");
            return EnqueueOperation("File properties",(db,token,progress)=>{
                var rows=db.Query(@"SELECT g.content_id,g.status,r.path,o.path,o.size,o.modified_ticks,o.file_identity
 FROM review_groups g JOIN group_occurrences o ON o.group_id=g.id JOIN source_roots r ON r.id=o.root_id
 WHERE g.id=? AND g.retired=0 AND o.id=? AND o.superseded=0 LIMIT 1",new object[]{groupId,occurrenceId},1);
                if(rows.Count!=1)throw new InvalidOperationException("This file location changed. Reload its details before refreshing.");
                var source=rows[0];if((string)source[1]=="EVIDENCE")throw new ProtectedDetailsException();
                if(Archives.IsPath((string)source[3]))throw new InvalidOperationException("Archive entries do not have Windows ownership. Rescan the archive to refresh its recorded modified date.");
                if(String.IsNullOrEmpty(source[6] as string))throw new InvalidOperationException("Rescan this source to record its file identity and ownership.");
                string path=Catalog.PreviewSourcePath((string)source[2],(string)source[3]);
                using(var file=VerifiedSourceLease(path,(long)source[4],(long)source[5],(string)source[6],(string)source[2])){
                    token.ThrowIfCancellationRequested();var before=FileAccessNative.Stamp(file);var properties=FilePropertyCapture.Read(file,path);
                    token.ThrowIfCancellationRequested();if(!before.Same(FileAccessNative.Stamp(file)))throw new FileChangedException("The source changed while reading its properties. Saved properties were retained.");
                    WaitWrite(w=>{RequireReportWritable();token.ThrowIfCancellationRequested();Verification.Transaction(w,()=>{
                        if(w.Scalar(@"SELECT 1 FROM review_groups g JOIN group_occurrences o ON o.group_id=g.id JOIN source_roots r ON r.id=o.root_id
 WHERE g.id=? AND g.content_id=? AND g.retired=0 AND g.status<>'EVIDENCE' AND o.id=? AND o.superseded=0
 AND r.path=? AND o.path=? AND o.size=? AND o.modified_ticks=? AND o.file_identity IS ?",groupId,source[0],occurrenceId,source[2],source[3],source[4],source[5],source[6])==null)throw new InvalidOperationException("The recorded source changed. Saved properties were retained.");
                        FilePropertiesData.Store(w,occurrenceId,properties);Catalog.Bump(w);
                    });return null;},properties.Cost+2048);
                }
                return "File ownership and dates saved.";
            },2048+occurrenceId.Length*2L);
        }
    }
}
