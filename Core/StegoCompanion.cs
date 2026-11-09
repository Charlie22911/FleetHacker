using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MediaReviewer {
    internal sealed class StegoEngineCapability {
        public string Id {get;set;}public string State {get;set;}public string Reason {get;set;}
        public string[] Formats {get;set;}
    }
    internal sealed class StegoCapabilities {
        public string Product {get;set;}public int ProtocolVersion {get;set;}public string Nonce {get;set;}
        public StegoEngineCapability[] Engines {get;set;}
    }
    internal sealed class StegoPackageInfo {
        public bool Present,Trusted,Compatible,Ready;
        public string Message="";
        public StegoEngineCapability[] Engines=new StegoEngineCapability[0];
    }
    internal static class StegoCompanion {
        internal const string FileName="FleetHacker.Steganography.exe";
        internal static string EmbeddedPin(){
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Steganography.sha256")){
                if(stream==null)return "";
                using(var reader=new StreamReader(stream))return reader.ReadToEnd().Trim();
            }
        }
        static string ReadBounded(StreamReader reader,int maximum){
            var text=new StringBuilder();int ch;
            while((ch=reader.Read())!=-1 && ch!='\n'){
                if(text.Length==maximum)throw new InvalidDataException("Companion response exceeds its limit");
                text.Append((char)ch);
            }
            return text.ToString();
        }
        static StegoCapabilities Handshake(string path,CancellationToken token){
            using(var job=new DecoderJob(256,1))
            using(var process=new Process {StartInfo=new ProcessStartInfo(path,"--capabilities") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path),RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
                bool started=false,assigned=false;Task<string> reply=null;Task drain=null;
                try{
                    token.ThrowIfCancellationRequested();process.Start();started=true;job.Assign(process);assigned=true;
                    var errors=process.StandardError;drain=Task.Run(()=>{while(errors.Read()!=-1){}});
                    var output=process.StandardOutput;reply=Task.Run(()=>ReadBounded(output,65536));
                    string nonce=Guid.NewGuid().ToString("N");
                    process.StandardInput.WriteLine("FH-STEG-1 "+nonce);process.StandardInput.Flush();process.StandardInput.Close();
                    var clock=Stopwatch.StartNew();
                    while(!reply.IsCompleted){token.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>=10000)throw new TimeoutException("Companion capability check timed out");Thread.Sleep(20);}
                    token.ThrowIfCancellationRequested();
                    var caps=new JavaScriptSerializer {MaxJsonLength=65536,RecursionLimit=12}.Deserialize<StegoCapabilities>(reply.GetAwaiter().GetResult());
                    if(caps==null || caps.Product!="FleetHacker.Steganography" || caps.ProtocolVersion!=1 || caps.Nonce!=nonce || caps.Engines==null || caps.Engines.Length>16)throw new InvalidDataException("Incompatible companion protocol");
                    var ids=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    foreach(var engine in caps.Engines){
                        if(engine==null || String.IsNullOrEmpty(engine.Id) || engine.Id.Length>64 || !ids.Add(engine.Id) || engine.State!="unavailable" && engine.State!="available" || engine.Reason==null || engine.Reason.Length>1024 || engine.Formats==null || engine.Formats.Length>16 || engine.Formats.Any(f=>String.IsNullOrEmpty(f)||f.Length>16))throw new InvalidDataException("Invalid companion capability record");
                    }
                    return caps;
                }finally{
                    if(assigned)job.StopAndWait();
                    else if(started && !process.HasExited){process.StandardInput.Close();if(!process.WaitForExit(1000))process.Kill();}
                    if(started)process.WaitForExit();
                    if(reply!=null)try{reply.GetAwaiter().GetResult();}catch{}
                    if(drain!=null)try{drain.GetAwaiter().GetResult();}catch{}
                }
            }
        }
        internal static StegoPackageInfo Probe(string directory,string expectedHash,CancellationToken token){
            token.ThrowIfCancellationRequested();var info=new StegoPackageInfo();string path=Path.Combine(directory,FileName);
            try{
                if(!File.Exists(path))return info;info.Present=true;
                if(expectedHash==null || expectedHash.Length!=64 || !expectedHash.All(Uri.IsHexDigit)){info.Message="This application has no trusted analysis-package identity.";return info;}
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    if(file.Length<1024 || file.Length>8L*1024*1024*1024){info.Message="The analysis package has an invalid size.";return info;}
                    string digest;using(var hash=SHA256.Create()){
                        var buffer=new byte[65536];int count;
                        while((count=file.Read(buffer,0,buffer.Length))!=0){token.ThrowIfCancellationRequested();hash.TransformBlock(buffer,0,count,buffer,0);}
                        hash.TransformFinalBlock(new byte[0],0,0);digest=BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
                    }
                    token.ThrowIfCancellationRequested();
                    if(!String.Equals(digest,expectedHash,StringComparison.OrdinalIgnoreCase)){info.Message="The analysis package does not match this application build.";return info;}
                    info.Trusted=true;var caps=Handshake(path,token);info.Compatible=true;info.Engines=caps.Engines;
                    info.Ready=new[]{"aletheia","zsteg","binwalk"}.All(id=>caps.Engines.Any(e=>e.Id==id&&e.State=="available"));
                    info.Message=info.Ready ? "Analysis engines available." : "The companion is compatible. Required analysis engines are unavailable.";
                }
            }catch(OperationCanceledException){throw;}
            catch(Exception error){info.Message="Analysis package unavailable: "+error.GetBaseException().Message;}
            return info;
        }
    }
}
