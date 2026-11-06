using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MediaReviewer {
    internal static class MediaScratch {
        const string Prefix="MediaComplianceReviewer-";
        const string Marker="media-owner-v1";
        static readonly object gate=new object();
        static FileStream owner;
        static IDisposable directoryPin;

        static byte[] Signature(string directory) {
            return Encoding.ASCII.GetBytes("MCR-MEDIA-1\n"+Path.GetFileName(directory)+"\n");
        }
        static bool IsDirectoryName(string name) {
            Guid id;
            return name.StartsWith(Prefix,StringComparison.Ordinal) && Guid.TryParseExact(name.Substring(Prefix.Length),"N",out id);
        }
        static bool IsPrivateDirectory(string directory) {
            return !String.IsNullOrEmpty(directory) && IsDirectoryName(Path.GetFileName(directory)) &&
                String.Equals(Paths.Source(Path.GetDirectoryName(directory)),Paths.Source(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase);
        }
        static bool ValidMarker(FileStream stream,string directory) {
            byte[] expected=Signature(directory);
            using(var reader=new BinaryReader(stream,Encoding.UTF8,true))
                return stream.Length==expected.Length && reader.ReadBytes(expected.Length).SequenceEqual(expected);
        }
        internal static string EnsureDirectory() {
            lock(gate){
                string directory=Native.RuntimeDirectory;
                if(owner!=null)return directory;
                Directory.CreateDirectory(directory);
                var pin=FileAccessNative.PinScratchDirectory(directory);
                FileStream marker=null;
                try{
                    marker=new FileStream(Path.Combine(directory,Marker),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
                    byte[] signature=Signature(directory);
                    marker.Write(signature,0,signature.Length);
                    marker.Flush(true);
                    directoryPin=pin;owner=marker;
                    return directory;
                }catch{
                    if(marker!=null)marker.Dispose();
                    pin.Dispose();
                    throw;
                }
            }
        }
        static bool ExtensionSafe(string extension) {
            return extension.Length>=2 && extension.Length<=17 && extension[0]=='.' &&
                extension.Skip(1).All(c=>c>='0' && c<='9' || c>='a' && c<='z' || c>='A' && c<='Z');
        }
        internal static string NewArchivePath(string extension) {
            return Path.Combine(EnsureDirectory(),"archive-preview-"+Guid.NewGuid().ToString("N")+(ExtensionSafe(extension) ? extension : ""));
        }
        static bool HasId(string name,string prefix) {
            Guid id;
            return name.StartsWith(prefix,StringComparison.Ordinal) && Guid.TryParseExact(name.Substring(prefix.Length),"N",out id);
        }
        internal static bool IsPlaybackPath(string path) {
            return Path.GetExtension(path)==".wmv" && HasId(Path.GetFileNameWithoutExtension(path),"playback-");
        }
        static bool IsArchivePath(string path) {
            string extension=Path.GetExtension(path);
            return (extension.Length==0 || ExtensionSafe(extension)) && HasId(Path.GetFileNameWithoutExtension(path),"archive-preview-");
        }
        static bool OwnedName(string path) {
            return IsPlaybackPath(path) || IsArchivePath(path) || Path.GetFileName(path)=="dependency-preview.png";
        }
        internal static IDisposable PlaybackLease(string output) {
            if(!IsPlaybackPath(output))throw new InvalidDataException("Invalid temporary playback destination.");
            return HelperLease(Path.GetDirectoryName(output));
        }
        internal static IDisposable ArchiveLease(string source) {
            if(!IsArchivePath(source))return null;
            string directory=Path.GetDirectoryName(source);
            return IsPrivateDirectory(directory) ? HelperLease(directory) : null;
        }
        static IDisposable HelperLease(string directory) {
            if(!IsPrivateDirectory(directory))throw new InvalidDataException("Invalid private media directory.");
            var pin=FileAccessNative.PinScratchDirectory(directory);
            FileStream stream=null;
            try{
                string marker=Path.Combine(directory,Marker);
                stream=FileAccessNative.OpenScratchMarker(marker,false);
                if(!ValidMarker(stream,directory))throw new InvalidDataException("Invalid private media owner.");
                return new Lease(stream,pin);
            }catch{
                if(stream!=null)stream.Dispose();
                pin.Dispose();
                throw;
            }
        }
        sealed class Lease:IDisposable {
            readonly IDisposable file,pin;
            internal Lease(IDisposable file,IDisposable pin){this.file=file;this.pin=pin;}
            public void Dispose(){try{file.Dispose();}finally{pin.Dispose();}}
        }
        internal static void Release(string file,string identity=null) {
            if(!String.IsNullOrEmpty(file) && Path.GetDirectoryName(file)==Native.RuntimeDirectory && OwnedName(file))
                FileAccessNative.DeleteScratchFile(file,identity);
        }
        internal static void QueueRecovery() {
            new Thread(()=>Recover(Path.GetTempPath())) {IsBackground=true,Name="MediaReviewer scratch recovery"}.Start();
        }
        internal static void Recover(string temp) {
            try{
                foreach(string directory in Directory.EnumerateDirectories(temp,Prefix+"*").Take(128))
                    if(directory!=Native.RuntimeDirectory && IsPrivateDirectory(directory))RecoverDirectory(directory);
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        static void RecoverDirectory(string directory) {
            try{
                string marker=Path.Combine(directory,Marker),identity;
                using(FileAccessNative.PinScratchDirectory(directory)){
                    using(var stream=FileAccessNative.OpenScratchMarker(marker,true)){
                        if(!ValidMarker(stream,directory))return;
                        identity=FileAccessNative.Stamp(stream).Identity;
                        foreach(string file in Directory.EnumerateFileSystemEntries(directory).Take(128)){
                            if(file==marker)continue;
                            if(OwnedName(file))FileAccessNative.DeleteScratchFile(file,null);
                        }
                        // Keep proof of ownership if a locked file, unknown entry or unfinished batch remains.
                        if(Directory.EnumerateFileSystemEntries(directory).Any(entry=>entry!=marker))return;
                    }
                    if(!FileAccessNative.DeleteScratchFile(marker,identity))return;
                }
                Directory.Delete(directory,false);
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.ComponentModel.Win32Exception){}
        }
        internal static void Shutdown() {
            lock(gate){
                if(owner==null)return;
                owner.Dispose();owner=null;
                directoryPin.Dispose();directoryPin=null;
                RecoverDirectory(Native.RuntimeDirectory);
            }
        }
    }
}
