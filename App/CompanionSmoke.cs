using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace MediaReviewer {
    static class CompanionSmoke {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static object Value(object target,string name){return target.GetType().GetField(name).GetValue(target);}
        internal static int Run(string directory){
            var type=Assembly.GetExecutingAssembly().GetType("MediaReviewer.StegoCompanion");
            Check(type!=null,"A trusted optional companion discovery API is required");
            var probe=type.GetMethod("Probe",BindingFlags.Static|BindingFlags.NonPublic);
            Check(probe!=null,"The companion probe must accept a pinned package hash");
            Directory.CreateDirectory(directory);
            Func<string,string,CancellationToken,object> run=(folder,hash,token)=>probe.Invoke(null,new object[]{folder,hash,token});
            var missing=run(directory,new string('0',64),CancellationToken.None);
            Check(!(bool)Value(missing,"Present"),"An absent companion must not expose analysis");
            string filename=Path.Combine(directory,"FleetHacker.Steganography.exe");File.WriteAllText(filename,"not an executable");
            var invalid=run(directory,new string('0',64),CancellationToken.None);
            Check((bool)Value(invalid,"Present")&&!(bool)Value(invalid,"Trusted"),"A neighboring file must not be trusted by filename alone");
            string own;using(var current=Process.GetCurrentProcess())own=Path.Combine(Path.GetDirectoryName(current.MainModule.FileName),"FleetHacker.Steganography.exe");
            Check(File.Exists(own),"The development companion must be built before this check");File.Copy(own,filename,true);
            string pin;using(var hash=SHA256.Create())using(var input=File.OpenRead(filename))pin=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();
            var valid=run(directory,pin,CancellationToken.None);
            Check((bool)Value(valid,"Trusted")&&(bool)Value(valid,"Compatible"),"The pinned companion must complete a compatible handshake");
            Check(!(bool)Value(valid,"Ready"),"A package without validated engines must not advertise full analysis readiness");
            byte[] changed=File.ReadAllBytes(filename);changed[changed.Length-1]^=1;File.WriteAllBytes(filename,changed);
            var altered=run(directory,pin,CancellationToken.None);
            Check(!(bool)Value(altered,"Trusted"),"Changing one byte of a valid package must reject its pinned identity");
            string fault=Path.Combine(directory,"fault.exe");
            Check(File.Exists(fault),"Failure fixtures must be compiled before this check");File.Copy(fault,filename,true);
            using(var hash=SHA256.Create())using(var input=File.OpenRead(filename))pin=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();
            foreach(string mode in new[]{"protocol","oversized","crash","hang"}){
                File.WriteAllText(Path.Combine(directory,"fixture-mode.txt"),mode);
                var rejected=run(directory,pin,CancellationToken.None);
                Check((bool)Value(rejected,"Trusted")&&!(bool)Value(rejected,"Compatible"),"Invalid worker response must not enable analysis: "+mode);
            }
            using(var running=new CancellationTokenSource()){
                running.CancelAfter(200);bool observed=false;
                try{run(directory,pin,running.Token);}catch(TargetInvocationException e){observed=e.InnerException is OperationCanceledException;}
                Check(observed,"A running companion check must be cancellable");
            }
            int child=Int32.Parse(File.ReadAllText(Path.Combine(directory,"fixture.pid")));
            try{using(var worker=Process.GetProcessById(child))Check(worker.HasExited,"Cancelled companion must not leave an owned worker running");}catch(ArgumentException){}
            using(var cancelled=new CancellationTokenSource()){
                cancelled.Cancel();bool observed=false;
                try{run(directory,pin,cancelled.Token);}catch(TargetInvocationException e){observed=e.InnerException is OperationCanceledException;}
                Check(observed,"Companion probing must honor cancellation");
            }
            Console.WriteLine("Companion absence, tampering, version handshake, incomplete coverage, malformed/oversized output, crash, timeout and running cancellation passed.");return 0;
        }
    }
}
