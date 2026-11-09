using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        readonly CancellationTokenSource stegoStop=new CancellationTokenSource();
        Task<StegoPackageInfo> stegoProbe;
        StegoPackageInfo stegoPackage;
        DateTime stegoNextProbe=DateTime.MinValue;
        string stegoFileStamp;
        string stegoTask;
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
            var chosen=Selected().Select(c=>c.Record.GroupId).ToArray();
            var dialog=new Window {Owner=Window,Title="Hidden data analysis",Width=640,Height=460,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var panel=new StackPanel {Margin=new Thickness(20)};dialog.Content=panel;
            panel.Children.Add(new TextBlock {Text=package.Message+"\n\n"+engines+"\n\nSelect up to 200 items to analyze their original PNG, JPEG or BMP bytes. Originals and compliance decisions are unchanged.\n\nThe suspicion index is not a probability. No indicators does not prove hidden data is absent.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
            var analyze=new Button {Content="Analyze selected ("+chosen.Length+")",Margin=new Thickness(0,0,0,8),IsEnabled=Ready && package.Trusted && package.Compatible && package.Ready && chosen.Length>0 && chosen.Length<=200 && !engine.StegoAnalysisActive && task==null && !engine.DraftReviewActive && !status.RunningScan};
            analyze.Click+=(s,e)=>{dialog.Close();Guard(()=>{string id=engine.RequestStegoAnalysis(chosen);stegoTask=id;Message("Hidden data analysis queued…");Queue(id,r=>{if(stegoTask==id)stegoTask=null;Message(r.Success ? r.Value : r.Error);if(r.Success)ShowText("Hidden data analysis",r.Value+"\n\nChoose Saved results to inspect each engine's coverage and findings.");});});};panel.Children.Add(analyze);
            var history=new Button {Content="Saved results",IsEnabled=Ready && chosen.Length==1,Margin=new Thickness(0,0,0,8)};
            history.Click+=(s,e)=>{dialog.Close();Guard(()=>Work(engine.RequestStegoHistory(chosen[0]),"Hidden data results",r=>ShowText("Hidden data results",r.Value)));};panel.Children.Add(history);
            var export=new Button {Content="Export latest result as JSON…",IsEnabled=Ready && chosen.Length==1};
            export.Click+=(s,e)=>{dialog.Close();Guard(()=>Work(engine.RequestStegoJson(chosen[0]),"Hidden data JSON",r=>{
                if(r.Value=="{}"){Message("No saved analysis for this item's current identity.");return;}
                var picker=new SaveFileDialog {Title="Export hidden data analysis",Filter="JSON result (*.json)|*.json",DefaultExt=".json",FileName="hidden-data-analysis.json"};
                if(picker.ShowDialog(Window)==true){File.WriteAllText(picker.FileName,r.Value,new System.Text.UTF8Encoding(false));Message("Analysis JSON exported.");}
            }));};panel.Children.Add(export);dialog.ShowDialog();
        }
        void StopStegoPackage(){stegoStop.Cancel();}
        void DisposeStegoPackage(){
            stegoStop.Cancel();if(stegoProbe!=null)try{stegoProbe.GetAwaiter().GetResult();}catch(OperationCanceledException){}
            stegoStop.Dispose();
        }
    }
}
