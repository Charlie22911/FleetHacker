using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace MediaReviewer {
    static class ManagedSmoke {
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        [MethodImpl(MethodImplOptions.NoInlining)] static void DiagnosticException(){throw new InvalidOperationException("Diagnostic source-line fixture");}
        internal static void VerifySymbols(){
            try{DiagnosticException();}catch(InvalidOperationException error){
                Check(new StackTrace(error,true).GetFrames().Any(frame=>Path.GetFileName(frame.GetFileName())=="ManagedSmoke.cs" && frame.GetFileLineNumber()>0),"Matching diagnostic symbols must produce source filenames and line numbers");
                return;
            }
            throw new Exception("Diagnostic exception fixture did not run");
        }
        internal static int Run(string directory){
            Directory.CreateDirectory(directory);VerifySymbols();
            string path=Path.Combine(directory,"sha256.txt");File.WriteAllBytes(path,Encoding.ASCII.GetBytes("abc"));
            var identity=Identity.Read(path,new FingerprintPolicy {Mode="Sampled",ThresholdBytes=0},CancellationToken.None);
            Check(identity.Algorithm=="sha256" && identity.Hash=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","The managed scan entry must hash the complete file despite a legacy sampled preference");
            var queue=new BoundedQueue<object>(1,8);bool rejected=false;
            try{queue.TryAdd(new object(),9);}catch(InvalidOperationException){rejected=true;}
            Check(rejected && queue.Count==0,"Oversized result retention must fail without enqueueing partial work");
            var lead=new Dictionary<string,object>{{"findings",new[]{new Dictionary<string,object>{{"kind","statistical"},{"confidence",0.99},{"reliability","complete"}}}}};
            Check(!StegoConfidence.Evaluate(lead,90).Qualifies,"Uncalibrated raw statistical claims must remain leads");
            Console.WriteLine("Managed checks passed: full SHA-256, bounded result rejection, conservative confidence, matching source-line symbols. Native host and interactive WPF were not tested.");return 0;
        }
    }
}
