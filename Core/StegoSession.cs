using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace MediaReviewer {
    internal interface IStegoSession:IDisposable {string Work{get;}string Nonce{get;}string Analyze(string snapshot,string digest,CancellationToken token);}
    internal sealed class StegoSession:IStegoSession {
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=2097152,RecursionLimit=32};
        readonly string nonce=Guid.NewGuid().ToString("N");
        internal readonly string Work;
        internal string Nonce {get{return nonce;}}
        string IStegoSession.Work {get{return Work;}}string IStegoSession.Nonce {get{return nonce;}}
        string IStegoSession.Analyze(string snapshot,string digest,CancellationToken token){return Analyze(snapshot,digest,token);}
        FileStream pin,lease;Process process;DecoderJob job;Task<string> drain;bool started,assigned,disposed;
        internal StegoSession(string directory,string expected,CancellationToken token,Action<DecoderJob,Process> assign=null){
            Work=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Temp","FHStego-"+nonce);
            try{
                string path=Path.Combine(directory,StegoCompanion.FileName);
                pin=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                using(var hash=SHA256.Create()){
                    byte[] block=new byte[65536];int n;while((n=pin.Read(block,0,block.Length))>0){token.ThrowIfCancellationRequested();hash.TransformBlock(block,0,n,block,0);}hash.TransformFinalBlock(new byte[0],0,0);
                    if(expected==null || expected.Length!=64 || !String.Equals(BitConverter.ToString(hash.Hash).Replace("-",""),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The analysis package does not match this application build");
                }
                var access=new DirectorySecurity();access.SetAccessRuleProtection(true,false);
                var inheritance=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
                access.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
                access.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
                Directory.CreateDirectory(Work,access);
                lease=new FileStream(Path.Combine(Work,".fh-stego-owner"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
                byte[] marker=Encoding.ASCII.GetBytes(nonce);lease.Write(marker,0,marker.Length);lease.Flush();
                lease.Dispose();lease=new FileStream(Path.Combine(Work,".fh-stego-owner"),FileMode.Open,FileAccess.Read,FileShare.Read);
                job=DecoderJob.Analysis();
                process=new Process {StartInfo=new ProcessStartInfo(path,"--analyze"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}};
                process.Start();started=true;if(assign==null)job.Assign(process);else assign(job,process);assigned=true;
                drain=Task.Run(()=>{var message=new StringBuilder();int ch;while((ch=process.StandardError.Read())!=-1)if(message.Length<8192)message.Append((char)ch);return message.ToString();});
                process.StandardInput.WriteLine("FH-STEG-1 "+nonce);process.StandardInput.WriteLine(StegoCompanion.WireJson(json.Serialize(new {Work=Work})));process.StandardInput.Flush();
                Frame("ready",240000,token);
            }catch{Dispose();throw;}
        }
        Dictionary<string,object> Frame(string stage,int milliseconds,CancellationToken token){
            var read=Task.Run(()=>{
                var text=new StringBuilder();int ch;while((ch=process.StandardOutput.Read())!=-1 && ch!='\n'){if(text.Length>=2097152)throw new InvalidDataException("Analysis response exceeds its limit");text.Append((char)ch);}if(ch==-1)throw new EndOfStreamException("Analysis worker closed its response stream");
                return json.Deserialize<Dictionary<string,object>>(text.ToString());
            });
            var clock=Stopwatch.StartNew();
            try{
                while(!read.IsCompleted){token.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>milliseconds)throw new TimeoutException("Analysis exceeded its processing-time limit");Thread.Sleep(25);}
                token.ThrowIfCancellationRequested();var value=read.GetAwaiter().GetResult();
                if(!value.ContainsKey("Product") || (string)value["Product"]!="FleetHacker.Steganography" || !value.ContainsKey("ProtocolVersion") || Convert.ToInt32(value["ProtocolVersion"])!=1 || !value.ContainsKey("Nonce") || (string)value["Nonce"]!=nonce || !value.ContainsKey("Stage") || (string)value["Stage"]!=stage)throw new InvalidDataException("Invalid analysis response identity");return value;
            }catch(Exception failure){Stop();try{read.GetAwaiter().GetResult();}catch{}if(failure is EndOfStreamException){string detail=drain==null ? "" : drain.GetAwaiter().GetResult();throw new IOException("Analysis worker ended before completing its response. "+detail,failure);}throw;}
        }
        internal string Analyze(string snapshot,string digest,CancellationToken token){
            if(disposed)throw new ObjectDisposedException("Analysis session");token.ThrowIfCancellationRequested();
            process.StandardInput.WriteLine(StegoCompanion.WireJson(json.Serialize(new {path=snapshot})));process.StandardInput.Flush();
            var frame=Frame("result",600000,token);string result=frame.ContainsKey("Result") ? frame["Result"] as string : null;
            if(result==null || result.Length>1048576)throw new InvalidDataException("Invalid analysis result");
            var parsed=json.Deserialize<Dictionary<string,object>>(result);
            if(!parsed.ContainsKey("source") || !String.Equals(parsed["source"] as string,snapshot,StringComparison.OrdinalIgnoreCase) || !parsed.ContainsKey("sha256") || !String.Equals(parsed["sha256"] as string,digest,StringComparison.Ordinal))throw new InvalidDataException("Analysis result does not match the original-byte snapshot");
            return result;
        }
        void Stop(){if(job!=null){if(assigned)job.StopAndWait();job.Dispose();job=null;}if(process!=null && started){if(!process.HasExited)process.Kill();process.WaitForExit();}}
        public void Dispose(){
            if(disposed)return;disposed=true;
            try{Stop();if(drain!=null)try{drain.GetAwaiter().GetResult();}catch{}if(process!=null)process.Dispose();}
            finally{if(pin!=null)pin.Dispose();if(lease!=null)lease.Dispose();
                if(Directory.Exists(Work) && Path.GetFileName(Work)=="FHStego-"+nonce && (File.GetAttributes(Work)&FileAttributes.ReparsePoint)==0 && File.Exists(Path.Combine(Work,".fh-stego-owner")) && File.ReadAllText(Path.Combine(Work,".fh-stego-owner"))==nonce){
                    // Only this session's nonce-marked workspace may be removed. Reject links.
                    try{DeleteOwned(Work);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }
        }
        static void DeleteOwned(string directory){foreach(string path in Directory.EnumerateFileSystemEntries(directory)){var flags=File.GetAttributes(path);if((flags&FileAttributes.ReparsePoint)!=0)throw new IOException("Unexpected link in analysis workspace");if((flags&FileAttributes.Directory)!=0)DeleteOwned(path);else File.Delete(path);}Directory.Delete(directory);}
    }
}
