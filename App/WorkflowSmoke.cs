using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MediaReviewer {
    static class WorkflowSmoke {
        static T Field<T>(object owner,string name){return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Wait(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>25)throw new Exception("Workflow condition timed out");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
        static void Image(string path,byte red){int width=1200,height=800;var pixels=new byte[width*height*3];for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=(y*width+x)*3;pixels[i]=(byte)(x%251);pixels[i+1]=(byte)(y%251);pixels[i+2]=red;}var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Rgb24,null,pixels,width*3);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
        internal static int Run(string directory,string mode){
            Directory.CreateDirectory(directory);string root=Directory.CreateDirectory(Path.Combine(directory,"images")).FullName;
            int total=mode=="pagination" ? 22 : 6;
            for(int i=0;i<total;i++)Image(Path.Combine(root,i+".png"),(byte)(i*40));
            string database=Path.Combine(directory,"workflow.sqlite");
            using(var engine=ReviewerEngine.Open(database,true)){Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1});Wait(()=>engine.Status.ScanId==scan&&!engine.Status.RunningScan);}
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                var window=controller.Window;window.Width=1400;window.Height=900;controller.Open(database,false);window.Show();
                var cards=Field<ObservableCollection<CardItem>>(controller,"cards");Wait(()=>cards.Count==total&&cards[0].Image!=null);
                Wait(()=>((Button)window.FindName("SteganographyButton")).Visibility==Visibility.Visible);
                Check(!Field<StegoPackageInfo>(controller,"stegoPackage").Ready,"A compatible package without engines must remain explicitly unavailable");
                cards[0].IsSelected=true;
                if(mode=="selection" || mode=="gallery"){
                    if(mode=="gallery")((Button)window.FindName("ThumbnailsModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var first=cards[0];var next=cards[1];((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Wait(()=>!cards.Contains(first)&&((TextBlock)window.FindName("MessageText")).Text=="Review saved.");
                    Check(next.IsSelected&&((Button)window.FindName("CompliantButton")).IsEnabled,"The previewed successor must be selected and immediately reviewable after a decision");
                    ((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>!cards.Contains(next));
                    Check(cards.Count==4&&cards[0].IsSelected,"Repeated decisions must advance without clicking another card");
                    var pending=cards[0];var manuallySelected=cards[3];
                    ((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    pending.IsSelected=false;manuallySelected.IsSelected=true;
                    Wait(()=>!cards.Contains(pending));Wait(()=>Field<string>(controller,"pageRequest")==null);
                    Check(manuallySelected.IsSelected,"A newer manual selection must survive a pending review completion");
                    ((Button)window.FindName("ClearSelectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!cards.Any(c=>c.IsSelected)&&!((Button)window.FindName("CompliantButton")).IsEnabled,"Explicit Clear must preserve an empty selection");
                    var batch=cards.Take(2).ToArray();foreach(var card in batch)card.IsSelected=true;
                    ((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>batch.All(c=>!cards.Contains(c)));
                    Check(cards.Count==1&&!cards[0].IsSelected,"A batch decision must not implicitly select another item");
                    cards[0].IsSelected=true;((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>cards.Count==0);
                    Check(!((Button)window.FindName("CompliantButton")).IsEnabled,"Empty queues must disable decisions");
                    ((ComboBox)window.FindName("ViewBox")).SelectedIndex=1;Wait(()=>cards.Count==6);
                    cards[0].IsSelected=true;var sameQueueNext=cards[1];
                    ((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>sameQueueNext.IsSelected);
                    Check(cards.Count==6,"Reviewing within the same bucket must advance without removing the saved item");
                }else if(mode=="pagination"){
                    ((ComboBox)window.FindName("PageSizeBox")).SelectedItem=20;Wait(()=>cards.Count==20);
                    foreach(var card in cards)card.IsSelected=false;var last=cards[19];var before=cards.Select(c=>c.Record.GroupId).ToArray();last.IsSelected=true;
                    ((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Wait(()=>cards.Count==20&&!cards.Contains(last)&&cards.Any(c=>c.IsSelected));
                    Check(cards[19].IsSelected&&!before.Contains(cards[19].Record.GroupId),"Reviewing a page's last item must select the next item refilled from the following page");
                    ((TextBox)window.FindName("PageBox")).Text="2";((Button)window.FindName("GoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>cards.Count==1);
                    cards[0].IsSelected=true;((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Wait(()=>cards.Count==20&&cards.Any(c=>c.IsSelected));
                    Check(((TextBox)window.FindName("PageBox")).Text=="1","A shrinking final page must return to a valid page");
                    ((Button)window.FindName("CompliantButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((TextBox)window.FindName("SearchBox")).Text="no-generated-image-matches-this";((Button)window.FindName("FilterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Wait(()=>cards.Count==0&&Field<string>(controller,"pageRequest")==null);
                    Check(!((Button)window.FindName("CompliantButton")).IsEnabled,"Changing the filter during a save must cancel selection continuation");
                }else if(mode=="preview"){
                    Field<WorkspacePreferences>(controller,"workspace").PreviewWidth=600;
                    controller.GetType().GetMethod("UpdateControls",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);window.UpdateLayout();
                    var detail=window.FindName("PaneDetailImage") as System.Windows.Controls.Image;
                    Check(detail!=null,"The right pane must have a separate source-resolution image");Wait(()=>detail.Source!=null&&((BitmapSource)detail.Source).PixelWidth>256);
                    Check(((BitmapSource)detail.Source).PixelWidth>256,"The right pane must decode original detail beyond its thumbnail");
                    var previous=detail.Source;cards[0].IsSelected=false;cards[1].IsSelected=true;cards[1].IsSelected=false;cards[2].IsSelected=true;
                    Check(detail.Source==null,"Changing the preview target must immediately clear the previous source image");
                    Wait(()=>{var current=Field<MediaRow>(controller,"paneSource");return detail.Source!=null&&!ReferenceEquals(previous,detail.Source)&&current!=null&&current.GroupId==cards[2].Record.GroupId;});
                    Check(((BitmapSource)detail.Source).PixelWidth>256,"Rapid selection must retain source detail");
                    var thumbnail=cards[2].Image;cards[2].SetImage(null,"Generated saved-thumbnail failure");window.UpdateLayout();
                    var placeholder=((System.Windows.Controls.Grid)detail.Parent).Children.OfType<TextBlock>().First();
                    Check(placeholder.Visibility==Visibility.Collapsed,"A successful source preview must hide saved-thumbnail error text");
                    cards[2].SetImage(thumbnail,null);
                    Smoke.SaveGallery(window,Path.Combine(directory,"source-preview.png"));
                    var selected=cards[2].Record;string original=Catalog.PreviewSourcePath(selected.RootPath,selected.RelativePath);
                    File.Move(original,original+".offline");
                    try{
                        controller.GetType().GetMethod("ResetPanePreview",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);
                        Wait(()=>((TextBlock)window.FindName("PanePreviewStatus")).Text=="Saved preview — source unavailable");
                        Check(detail.Source==null&&cards[2].Image!=null,"Offline fallback must retain only the saved thumbnail");
                    }finally{File.Move(original+".offline",original);}
                }else if(mode=="archive")CheckArchiveIndependence(root);
                else throw new ArgumentException("Unknown workflow check");
                window.Close();Wait(()=>Field<bool>(controller,"allowClose"));
            }
            app.Shutdown();Console.WriteLine("Workflow "+mode+" passed.");return 0;
        }
        static void CheckArchiveIndependence(string root){
            string original=Path.Combine(root,"0.png"),archive=Path.Combine(root,"sample.zip");
            using(var file=File.Create(archive))using(var zip=new System.IO.Compression.ZipArchive(file,System.IO.Compression.ZipArchiveMode.Create))
            using(var entry=zip.CreateEntry("image.png").Open())using(var input=File.OpenRead(original))input.CopyTo(entry);
            Preview.CreateExpected(original,-1,0,null,512,InspectionPlan.EncodedLimit);
            var host=typeof(Preview).GetField("inspectionHost",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var gate=Field<object>(host,"gate");var loader=new MediaLoader(true);Monitor.Enter(gate);
            try{
                loader.QueueInspect(new MediaRow {GroupId=99,ContentId=99,RootPath=root,RelativePath="sample.zip"+Archives.Separator+"image.png",Size=-1},1,512);
                MediaLoadResult result=null;bool received=false;var clock=Stopwatch.StartNew();
                while(clock.Elapsed.TotalSeconds<3 && !(received=loader.TryTakeResult(out result)))Thread.Sleep(10);
                Check(received&&result.Image!=null,"Archived pane previews must complete without waiting for the shared Inspect decoder");
            }finally{Monitor.Exit(gate);loader.Dispose();}
        }
    }
}
