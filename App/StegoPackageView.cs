using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        readonly CancellationTokenSource stegoStop=new CancellationTokenSource();
        Task<StegoPackageInfo> stegoProbe;
        StegoPackageInfo stegoPackage;
        DateTime stegoNextProbe=DateTime.MinValue;
        string stegoFileStamp;
        void TickStegoPackage(){
            if(closing)return;
            if(stegoProbe!=null && stegoProbe.IsCompleted){
                if(stegoProbe.Status==TaskStatus.RanToCompletion)stegoPackage=stegoProbe.Result;
                stegoProbe=null;stegoNextProbe=DateTime.UtcNow.AddSeconds(5);UpdateStegoControls();
            }
            if(stegoProbe==null && DateTime.UtcNow>=stegoNextProbe){
                string directory;using(var current=Process.GetCurrentProcess())directory=Path.GetDirectoryName(current.MainModule.FileName);
                string pin=StegoCompanion.EmbeddedPin();var previous=stegoPackage;
                stegoProbe=Task.Run(()=>{
                    // Metadata only avoids repeating the availability handshake. An analysis
                    // launch must independently verify the package hash again.
                    string stamp;
                    try{var file=new FileInfo(Path.Combine(directory,StegoCompanion.FileName));stamp=file.Exists ? file.Length+":"+file.LastWriteTimeUtc.Ticks : "absent";}
                    catch(IOException){stamp=Guid.NewGuid().ToString("N");}
                    catch(UnauthorizedAccessException){stamp=Guid.NewGuid().ToString("N");}
                    if(previous!=null && stamp==stegoFileStamp)return previous;
                    var result=StegoCompanion.Probe(directory,pin,stegoStop.Token);stegoFileStamp=stamp;return result;
                });
            }
        }
        void UpdateStegoControls(){
            var button=Button("SteganographyButton");bool present=stegoPackage!=null && stegoPackage.Present;
            button.Visibility=present ? Visibility.Visible : Visibility.Collapsed;button.IsEnabled=present&&!closing;
            button.ToolTip=present ? stegoPackage.Message : null;
        }
        void ShowStegoPackage(){
            var package=stegoPackage;if(package==null)return;
            string engines=String.Join("\n",package.Engines.Select(e=>e.Id+": "+e.State+"\n"+e.Reason));
            ShowText("Hidden data analysis package",package.Message+"\n\n"+engines);
        }
        void StopStegoPackage(){stegoStop.Cancel();}
        void DisposeStegoPackage(){
            stegoStop.Cancel();if(stegoProbe!=null)try{stegoProbe.GetAwaiter().GetResult();}catch(OperationCanceledException){}
            stegoStop.Dispose();
        }
    }
}
