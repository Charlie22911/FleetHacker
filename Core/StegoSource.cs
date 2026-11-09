using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace MediaReviewer {
    // Original opening, network I/O and ZIP inflation stay outside the UI/database process.
    internal static class StegoSource {
        sealed class Request {public string Path{get;set;}public string Root{get;set;}public long Size{get;set;}public long Ticks{get;set;}public string Identity{get;set;}public string Work{get;set;}public string Destination{get;set;}}
        sealed class Reply {public bool Success{get;set;}public string Digest{get;set;}public long Size{get;set;}public string Error{get;set;}}
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=65536,RecursionLimit=8};
        static string Read(TextReader reader,int limit){var text=new StringBuilder();int ch;while((ch=reader.Read())!=-1 && ch!='\n'){if(text.Length>=limit)throw new InvalidDataException("Original-read response exceeds its limit");text.Append((char)ch);}if(ch==-1)throw new EndOfStreamException("Original reader closed its response stream");return text.ToString().TrimEnd('\r');}
        internal static string Snapshot(string path,long size,long ticks,string identity,string root,string work,string destination,string nonce,CancellationToken token){
            string executable;using(var own=Process.GetCurrentProcess())executable=own.MainModule.FileName;
            using(var job=new DecoderJob(384,1))using(var process=new Process {StartInfo=new ProcessStartInfo(executable,"--stego-source-worker"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable),RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}}){
                bool started=false,assigned=false;Task<string> read=null;Task drain=null;
                try{
                    token.ThrowIfCancellationRequested();process.Start();started=true;job.Assign(process);assigned=true;
                    drain=Task.Run(()=>{while(process.StandardError.Read()!=-1){}});read=Task.Run(()=>Read(process.StandardOutput,16384));
                    process.StandardInput.WriteLine("FH-STEG-SOURCE-1 "+nonce);process.StandardInput.WriteLine(StegoCompanion.WireJson(Json.Serialize(new Request {Path=path,Size=size,Ticks=ticks,Identity=identity,Root=root,Work=work,Destination=destination})));process.StandardInput.Flush();process.StandardInput.Close();
                    var clock=Stopwatch.StartNew();while(!read.IsCompleted){token.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>60000)throw new TimeoutException("Original read exceeded its 60-second limit");Thread.Sleep(25);}
                    token.ThrowIfCancellationRequested();var reply=Json.Deserialize<Reply>(read.GetAwaiter().GetResult());
                    if(reply==null || !reply.Success)throw new IOException(reply==null ? "Invalid original-read result" : reply.Error);
                    if(reply.Digest==null || reply.Digest.Length!=64 || reply.Size<0 || reply.Size>256L*1024*1024 || reply.Size!=size)throw new InvalidDataException("Invalid original snapshot identity");
                    // The response is checked against the resulting local file as well as recorded metadata.
                    using(var local=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read))using(var hash=SHA256.Create()){
                        if(local.Length!=reply.Size)throw new InvalidDataException("Snapshot size changed");byte[] bytes=new byte[65536];int count;
                        while((count=local.Read(bytes,0,bytes.Length))>0){token.ThrowIfCancellationRequested();hash.TransformBlock(bytes,0,count,bytes,0);}hash.TransformFinalBlock(new byte[0],0,0);
                        if(!String.Equals(BitConverter.ToString(hash.Hash).Replace("-",""),reply.Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Snapshot fingerprint changed");
                    }return reply.Digest;
                }finally{
                    if(assigned)job.StopAndWait();
                    if(started){if(!process.HasExited)process.Kill();process.WaitForExit();}
                    if(read!=null)try{read.GetAwaiter().GetResult();}catch{}
                    if(drain!=null)try{drain.GetAwaiter().GetResult();}catch{}
                }
            }
        }
        internal static int Worker(){
            Console.SetIn(new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false)));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});
            Reply result;
            try{
                string gate=Read(Console.In,128);const string prefix="FH-STEG-SOURCE-1 ";Guid id;
                if(!gate.StartsWith(prefix,StringComparison.Ordinal) || !Guid.TryParseExact(gate.Substring(prefix.Length),"N",out id))return 2;
                string nonce=id.ToString("N");var request=Json.Deserialize<Request>(Read(Console.In,65536));
                string work=Path.GetFullPath(request.Work),destination=Path.GetFullPath(request.Destination);
                if(Path.GetFileName(work)!="FHStego-"+nonce || (File.GetAttributes(work)&FileAttributes.ReparsePoint)!=0 || File.ReadAllText(Path.Combine(work,".fh-stego-owner"))!=nonce || !String.Equals(Path.GetDirectoryName(destination),work,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(destination).StartsWith("original-",StringComparison.Ordinal) || request.Size<0 || request.Size>256L*1024*1024)throw new InvalidDataException("Invalid original snapshot workspace");
                using(var source=SourceRead.Open(request.Path,request.Size,request.Ticks,request.Identity,CancellationToken.None,request.Root))using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var hash=SHA256.Create()){
                    byte[] bytes=new byte[65536];long total=0;int count;
                    while((count=source.Stream.Read(bytes,0,bytes.Length))>0){if((total+=count)>256L*1024*1024)throw new InvalidDataException("Original exceeds the analysis byte limit");output.Write(bytes,0,count);hash.TransformBlock(bytes,0,count,bytes,0);}source.Validate();hash.TransformFinalBlock(new byte[0],0,0);
                    result=new Reply {Success=true,Size=total,Digest=BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant()};
                }
            }catch(Exception failure){string message=failure.Message;result=new Reply {Success=false,Error=message.Substring(0,Math.Min(2048,message.Length))};}
            Console.Out.WriteLine(Json.Serialize(result));return 0;
        }
    }
}
