using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
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
            button.Visibility=present ? Visibility.Visible : Visibility.Collapsed;button.IsEnabled=present&&Ready&&!closing;
            button.ToolTip="Check unchecked photos for hidden data.";
        }
        Window stegoWindow;
        void ShowStegoPackage(){
            if(stegoWindow!=null){stegoWindow.Activate();return;}
            stegoWindow=CreateStegoWindow();if(stegoWindow==null)return;
            stegoWindow.Closed+=(s,e)=>stegoWindow=null;stegoWindow.Show();
        }
        internal Window CreateStegoWindow(Func<string> scanStart=null){
            if(stegoPackage==null || !Ready || engine==null)return null;
            var database=engine;var body=new DockPanel {Margin=new Thickness(20)};
            var dialog=Dialog("Hidden data scan",body,730,600);dialog.Name="StegoScanWindow";TrackDatabaseWindow(dialog);
            var heading=new StackPanel();Dock(body,heading,System.Windows.Controls.Dock.Top);
            heading.Children.Add(new TextBlock {Text="Check photos for hidden data",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
            heading.Children.Add(Label("Check unchecked photos. Suspect photos appear in Steganography with scan details in their notes."));
            var counts=Label("Finding unchecked photos…");counts.Name="StegoCountLabel";heading.Children.Add(counts);
            var actions=new WrapPanel {Margin=new Thickness(0,0,0,4)};heading.Children.Add(actions);
            var start=ActionButton("Start scan",()=>{});start.Name="StegoStartButton";start.IsEnabled=false;start.IsDefault=true;start.Style=(Style)Window.FindResource("PrimaryButton");actions.Children.Add(start);
            var stop=ActionButton("Stop scan",()=>{if(stegoTask!=null)database.CancelOperation(stegoTask);});stop.Name="StegoStopButton";actions.Children.Add(stop);
            var progress=new ProgressBar {Height=7,Margin=new Thickness(0,4,0,8),Visibility=Visibility.Collapsed};heading.Children.Add(progress);
            var feedback=Label("");feedback.Name="StegoStatusLabel";heading.Children.Add(feedback);
            var bottom=new StackPanel();Dock(body,bottom,System.Windows.Controls.Dock.Bottom);
            var paging=Bar();bottom.Children.Add(paging);
            var previous=ActionButton("Previous",()=>{});var next=ActionButton("Next",()=>{});var pageLabel=Label("Page 1");paging.Children.Add(previous);paging.Children.Add(pageLabel);paging.Children.Add(next);
            var resultActions=Bar();bottom.Children.Add(resultActions);
            var openBucket=ActionButton("Open Steganography",()=>{Combo("ViewBox").SelectedIndex=Array.IndexOf(BucketKeys,StegoFindings.Bucket);dialog.Close();});openBucket.Name="StegoOpenBucketButton";resultActions.Children.Add(openBucket);
            var details=ActionButton("View result",()=>{});details.Name="StegoResultButton";details.IsEnabled=false;resultActions.Children.Add(details);resultActions.Children.Add(ActionButton("Close",dialog.Close));
            var technical=new StackPanel();
            technical.Children.Add(Label(String.Join("\n",stegoPackage.Engines.Select(e=>e.Id+": "+(e.State=="available" ? e.Id=="aletheia" && !stegoPackage.FullCoverage ? "Available (limited checks)" : "Available" : "Unavailable")))));
            var export=ActionButton("Export result as JSON…",()=>{});export.IsEnabled=false;technical.Children.Add(export);
            var advanced=new Expander {Header="Technical details",Content=technical,Margin=new Thickness(0,4,0,6)};advanced.Name="StegoTechnicalDetails";bottom.Children.Add(advanced);
            bottom.Children.Add(Label("Some types of hidden data may not be detected."));
            var results=new DataGrid {Name="StegoResults",AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,EnableRowVirtualization=true,EnableColumnVirtualization=true};
            results.SetResourceReference(Control.BackgroundProperty,"PageBrush");results.SetResourceReference(Control.ForegroundProperty,"TextBrush");
            results.SetResourceReference(DataGrid.RowBackgroundProperty,"PageBrush");results.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"InsetBrush");results.SetResourceReference(Control.BorderBrushProperty,"BorderBrush");
            var headerStyle=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty,Window.FindResource("InsetBrush")));headerStyle.Setters.Add(new Setter(Control.ForegroundProperty,Window.FindResource("TextBrush")));headerStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,6,8,6)));headerStyle.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));results.ColumnHeaderStyle=headerStyle;
            results.Columns.Add(new DataGridTextColumn {Header="Photo",Binding=new Binding("Name"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
            results.Columns.Add(new DataGridTextColumn {Header="Result",Binding=new Binding("Outcome"),Width=180});
            results.Columns.Add(new DataGridTextColumn {Header="Last checked",Binding=new Binding("CheckedAt"),Width=145});
            var list=new DockPanel();list.Children.Add(results);body.Children.Add(list);
            int page=1;bool loaded=false,query=false,closed=false;StegoScanState state=null;string lastTask=null,outcome=null;long seenRevision=-1;
            Action refresh=()=>{};Action update=()=>{
                if(closed)return;bool valid=Ready && ReferenceEquals(engine,database);bool scanning=valid && database.StegoAnalysisActive;
                bool package=stegoPackage!=null && stegoPackage.Trusted && stegoPackage.Compatible && stegoPackage.Ready;
                bool busy=valid && (database.Status.RunningScan || database.DraftReviewActive || task!=null || database.ReportActive);
                start.IsEnabled=valid && package && loaded && state!=null && state.Pending>0 && !scanning && !busy;
                stop.Visibility=scanning ? Visibility.Visible : Visibility.Collapsed;stop.IsEnabled=scanning && stegoTask!=null;
                previous.IsEnabled=!query && page>1;next.IsEnabled=!query && state!=null && state.More;
                progress.Visibility=scanning ? Visibility.Visible : Visibility.Collapsed;
                if(scanning){lastTask=stegoTask;var p=stegoTask==null ? null : database.GetOperationProgress(stegoTask);feedback.Text=p==null ? "Getting ready…" : p.Stage;progress.IsIndeterminate=p==null || !p.Total.HasValue || p.Total.Value==0; if(!progress.IsIndeterminate)progress.Value=100.0*p.Completed/p.Total.Value;}
                else if(!package)feedback.Text="The scan package could not be opened. Use the matching package for this version.";
                else if(busy)feedback.Text="Finish the current scan or background task before starting.";
                else if(loaded && lastTask==null)feedback.Text=outcome??(state.Pending+state.Checked==0 ? "Scan a folder to add photos to this database." : state.Pending==0 ? "All available photos are checked." : "Ready to scan.");
                if(!scanning && lastTask!=null){var p=database.GetOperationProgress(lastTask);if(p!=null && p.State!="running" && p.State!="cancelling"){outcome=feedback.Text=p.State=="failed" ? "The scan could not finish. Try again to check the remaining photos." : p.Stage;lastTask=null;loaded=false;refresh();}}
            };
            refresh=()=>{
                if(closed || query || !Ready || !ReferenceEquals(engine,database))return;
                query=true;
                try{Queue(database.RequestStegoScanState(page),r=>{
                    query=false;if(closed || !ReferenceEquals(engine,database))return;
                    if(!r.Success){feedback.Text="Photo information could not be loaded. Close this window and try again.";return;}
                    state=r.StegoState;loaded=true;seenRevision=state.Revision;
                    counts.Text=state.Pending+state.Checked==0 ? "No photos found." : state.Pending.ToString("N0")+" photo"+(state.Pending==1 ? "" : "s")+" to check · "+state.Checked.ToString("N0")+" checked";
                    results.ItemsSource=state.Results;pageLabel.Text="Page "+page;details.IsEnabled=export.IsEnabled=false;update();
                });}catch{query=false;feedback.Text="The database is busy. Try again shortly.";}
            };
            start.Click+=(s,e)=>Guard(()=>{
                if(!start.IsEnabled)return;outcome=null;stegoTask=scanStart==null ? database.RequestStegoScan() : scanStart();lastTask=stegoTask;Message("Hidden data scan started.");
                string id=stegoTask;Queue(id,r=>{if(stegoTask==id)stegoTask=null;Message(r.Success ? r.Value : "Scan stopped before all photos were checked.");if(!closed && r.Success && Ready && ReferenceEquals(engine,database)){Combo("ViewBox").SelectedIndex=Array.IndexOf(BucketKeys,StegoFindings.Bucket);dialog.Close();return;}if(!closed){lastTask=null;outcome=feedback.Text=r.Success ? r.Value : database.GetOperationProgress(id).State=="cancelled" ? "Scan stopped. Finished checks are saved." : "The scan could not finish. Try again to check the remaining photos.";loaded=false;refresh();}});update();
            });
            previous.Click+=(s,e)=>{if(page>1){page--;refresh();}};next.Click+=(s,e)=>{if(state!=null && state.More){page++;refresh();}};
            results.SelectionChanged+=(s,e)=>{var selected=results.SelectedItem as StegoPhotoResult;details.IsEnabled=export.IsEnabled=selected!=null && selected.Outcome!="Not checked";};
            details.Click+=(s,e)=>{var selected=results.SelectedItem as StegoPhotoResult;if(selected!=null)Guard(()=>Work(database.RequestStegoHistory(selected.GroupId),"Photo result",r=>ShowText("Hidden data result",r.Value)));};
            export.Click+=(s,e)=>{var selected=results.SelectedItem as StegoPhotoResult;if(selected==null)return;Guard(()=>Work(database.RequestStegoJson(selected.GroupId),"Export result",r=>{
                if(r.Value=="{}"){Message("No saved result is available for this photo.");return;}
                var picker=new SaveFileDialog {Title="Export photo result",Filter="JSON result (*.json)|*.json",DefaultExt=".json",FileName="hidden-data-result.json"};
                if(picker.ShowDialog(dialog)==true){File.WriteAllText(picker.FileName,r.Value,new System.Text.UTF8Encoding(false));Message("Result exported.");}
            }));};
            var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
            timer.Tick+=(s,e)=>{update();if(!query && !database.StegoAnalysisActive && (!loaded || seenRevision!=database.Status.Revision))refresh();};
            dialog.Loaded+=(s,e)=>{refresh();update();timer.Start();};dialog.Closed+=(s,e)=>{closed=true;timer.Stop();};return dialog;
        }
        void StopStegoPackage(){stegoStop.Cancel();}
        void DisposeStegoPackage(){
            stegoStop.Cancel();if(stegoProbe!=null)try{stegoProbe.GetAwaiter().GetResult();}catch(OperationCanceledException){}
            stegoStop.Dispose();
        }
    }
}
