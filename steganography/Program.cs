using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]
namespace FleetHacker.Steganography {
    sealed class Entry {public string path{get;set;}public long size{get;set;}public string sha256{get;set;}}
    sealed class Manifest {public int ProtocolVersion{get;set;}public bool FullCoverage{get;set;}public object[] Engines{get;set;}public Entry[] Files{get;set;}}
    static class Program {
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=16000000,RecursionLimit=24};
        static string Read(TextReader reader,int limit){var text=new StringBuilder();int ch;while((ch=reader.Read())!=-1 && ch!='\n'){if(text.Length>=limit)throw new InvalidDataException("Response exceeds its limit");text.Append((char)ch);}if(ch==-1 && text.Length==0)throw new EndOfStreamException();return text.ToString().TrimEnd('\r');}
        static void Reply(object value){Console.Out.WriteLine(Json.Serialize(value));Console.Out.Flush();}
        static void Extract(string work,Manifest manifest){
            string root=Path.Combine(work,"engines");if(Directory.Exists(root))throw new IOException("Analysis directory already exists");Directory.CreateDirectory(root);
            var expected=manifest.Files.ToDictionary(e=>e.path,StringComparer.Ordinal);long total=0;
            if(expected.Count>40000)throw new InvalidDataException("Too many package files");
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("EnginePayload.zip")){
                if(resource==null)throw new InvalidDataException("Engine payload unavailable");
                using(var archive=new ZipArchive(resource,ZipArchiveMode.Read)){
                    if(archive.Entries.Count!=expected.Count)throw new InvalidDataException("Unexpected package file count");
                    foreach(var item in archive.Entries){
                        Entry pin;if(!expected.TryGetValue(item.FullName,out pin) || item.FullName.IndexOf('\\')>=0 || item.FullName.IndexOf(':')>=0 || item.FullName.Split('/').Any(p=>p==".."||p=="."||p.Length==0) || Path.IsPathRooted(item.FullName) || ((item.ExternalAttributes>>16)&0xf000)==0xa000 || item.Length!=pin.size || pin.size<0 || pin.size>1024L*1024*1024 || (total+=pin.size)>4L*1024*1024*1024)throw new InvalidDataException("Invalid engine package entry");
                        string destination=Path.GetFullPath(Path.Combine(root,item.FullName.Replace('/',Path.DirectorySeparatorChar)));
                        if(!destination.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Package entry escapes its root");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using(var source=item.Open())using(var target=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var hash=SHA256.Create()){
                            byte[] bytes=new byte[65536];long copied=0;int count;while((count=source.Read(bytes,0,bytes.Length))!=0){if((copied+=count)>pin.size)throw new InvalidDataException("Engine entry grew beyond its limit");target.Write(bytes,0,count);hash.TransformBlock(bytes,0,count,bytes,0);}hash.TransformFinalBlock(new byte[0],0,0);
                            if(copied!=pin.size || !String.Equals(BitConverter.ToString(hash.Hash).Replace("-",""),pin.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Engine file digest mismatch");
                        }
                        expected.Remove(item.FullName);
                    }
                }
            }
        }
        static int Main(string[] args){
            Console.SetIn(new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false)));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});
            Console.SetError(new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)){AutoFlush=true});
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            try{
                if(args.Length!=1 || args[0]!="--capabilities" && args[0]!="--analyze")return 2;
                string request=Read(Console.In,128);if(!request.StartsWith("FH-STEG-1 ",StringComparison.Ordinal))return 2;
                string nonce=request.Substring(10);Guid parsed;if(!Guid.TryParseExact(nonce,"N",out parsed))return 2;
                Manifest manifest;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(args[0]=="--capabilities" ? "EngineCapabilities.json" : "EngineManifest.json")){
                    if(stream==null)throw new InvalidDataException("Analysis manifest unavailable");using(var reader=new StreamReader(stream))manifest=Json.Deserialize<Manifest>(reader.ReadToEnd());
                }
                if(args[0]=="--capabilities"){Reply(new {Product="FleetHacker.Steganography",ProtocolVersion=1,Nonce=nonce,FullCoverage=manifest.FullCoverage,Engines=manifest.Engines});return 0;}
                var init=Json.Deserialize<Dictionary<string,string>>(Read(Console.In,65536));string work=Path.GetFullPath(init["Work"]);
                if(!Directory.Exists(work) || (File.GetAttributes(work)&FileAttributes.ReparsePoint)!=0 || File.ReadAllText(Path.Combine(work,".fh-stego-owner"))!=nonce)throw new InvalidDataException("Invalid analysis workspace");
                Extract(work,manifest);string root=Path.Combine(work,"engines");
                var start=new ProcessStartInfo(Path.Combine(root,"python/python/python.exe"),"-I \""+Path.Combine(root,"scripts/fh_runner.py")+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=work,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                start.EnvironmentVariables["TEMP"]=work;start.EnvironmentVariables["TMP"]=work;start.EnvironmentVariables.Remove("PYTHONPATH");start.EnvironmentVariables.Remove("PYTHONHOME");
                using(var worker=Process.Start(start)){
                    Task drain=Task.Run(()=>{while(worker.StandardError.Read()!=-1){}});
                    Reply(new {Product="FleetHacker.Steganography",ProtocolVersion=1,Nonce=nonce,Stage="ready"});
                    try{while(true){string line;try{line=Read(Console.In,65536);}catch(EndOfStreamException){break;}
                        worker.StandardInput.WriteLine(line);worker.StandardInput.Flush();string result=Read(worker.StandardOutput,1048576);
                        Reply(new {Product="FleetHacker.Steganography",ProtocolVersion=1,Nonce=nonce,Stage="result",Result=result});
                    }}finally{worker.StandardInput.Close();if(!worker.WaitForExit(2000))worker.Kill();worker.WaitForExit();drain.GetAwaiter().GetResult();}
                }
                return 0;
            }catch(Exception error){Console.Error.WriteLine(error.GetBaseException().Message);return 1;}
        }
    }
}
