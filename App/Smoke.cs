using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaReviewer {
    static class Smoke {
        internal static void CheckStartupPrompt(Window dialog) {
            var buttons=((WrapPanel)((StackPanel)dialog.Content).Children[2]).Children.OfType<Button>().ToArray();
            Check(buttons.Select(b=>b.Name).SequenceEqual(new[]{"StartupCreateButton","StartupOpenButton","StartupLaterButton"}),
                "The startup catalog chooser must offer Create, Open and Later.");
            dialog.Loaded+=(s,e)=>dialog.Dispatcher.BeginInvoke(new Action(()=>buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        }

        internal static void CheckSidebarCommands(Window window) {
            var sidebar=(FrameworkElement)window.FindName("QueueSidebar");
            foreach(string name in new[]{"CreateButton","OpenButton","MaintenanceButton","PreviewTasksButton","LogButton","HelpButton","AboutButton","ThemeButton",
                "ListModeButton","ThumbnailsModeButton","QuickModeButton","RefreshViewButton"})
                Check(sidebar.IsAncestorOf((Button)window.FindName(name)),"Expected sidebar command: "+name);
        }

        internal static void CheckShell(Window window,string snapshot) {
            CheckSidebarCommands(window);
            var list=window.FindName("ReviewList") as ListView;
            var sidebar=window.FindName("QueueSidebar") as FrameworkElement;
            var compact=window.FindName("CompactQueues") as FrameworkElement;
            var scan=window.FindName("ScanControls") as FrameworkElement;
            var top=window.FindName("TopControlsButton") as Button;
            Check(scan!=null && top!=null && window.Icon!=null,"The workspace needs collapsible top controls and an application icon.");
            var sidebarButton=(Button)window.FindName("QueueSidebarButton");
            var previewButton=(Button)window.FindName("PreviewPaneButton");
            Check(sidebarButton.IsVisible && previewButton.IsVisible && top.IsVisible,"All three panel controls must be visible at 1024 pixels.");
            Check(sidebar.IsVisible && !compact.IsVisible && (string)sidebarButton.Content=="Left","The sidebar starts open with a left-facing collapse arrow.");
            sidebarButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check(!sidebar.IsVisible && compact.IsVisible && sidebarButton.IsVisible && (string)sidebarButton.Content=="Right",
                "Hiding the sidebar must leave its expand arrow and restore the queue dropdown.");
            var queues=(ComboBox)window.FindName("ViewBox");queues.ApplyTemplate();queues.IsDropDownOpen=true;
            window.Dispatcher.Invoke(new Action(()=>{}),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var popup=queues.Template.FindName("PART_Popup",queues) as Popup;
            Check(popup!=null && popup.IsOpen && queues.Items.Count==4,"The themed compact dropdown must show the review queues.");
            queues.IsDropDownOpen=false;
            top.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!scan.IsVisible && top.IsVisible && (string)top.Content=="Down" && sidebarButton.IsVisible && previewButton.IsVisible,
                "Collapsing the top controls must keep all three panel controls visible.");
            top.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(scan.IsVisible && (string)top.Content=="Up","The top controls must expand again.");
            sidebarButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check(sidebar.IsVisible && !compact.IsVisible,"The left sidebar must reopen at 1024 pixels.");
            var preview=(FrameworkElement)window.FindName("PreviewPane");
            var divider=(GridSplitter)window.FindName("PreviewDivider");
            Check(divider!=null && divider.IsVisible && divider.Cursor==Cursors.SizeWE,"The right preview must have a visible resize divider with the double-arrow cursor.");
            double initialWidth=preview.ActualWidth;
            divider.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
            divider.RaiseEvent(new DragDeltaEventArgs(-95,0){RoutedEvent=Thumb.DragDeltaEvent});
            divider.RaiseEvent(new DragCompletedEventArgs(-95,0,false){RoutedEvent=Thumb.DragCompletedEvent});
            window.UpdateLayout();
            double expandedWidth=preview.ActualWidth;
            Check(expandedWidth>=initialWidth+80,"Dragging the divider left must widen the preview.");
            double listWithPreview=list.ActualWidth;
            previewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check(!preview.IsVisible && !divider.IsVisible && previewButton.IsVisible && (string)previewButton.Content=="Left" && list.ActualWidth>=listWithPreview+expandedWidth,
                "Collapsing the right preview must give its width back to the review list.");
            previewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check(preview.IsVisible && (string)previewButton.Content=="Right" && Math.Abs(preview.ActualWidth-expandedWidth)<2,"The preview pane must reopen at the chosen width.");
            SaveGallery(window,snapshot);
            Check(list!=null && list.IsVisible && list.Items.Count>0,"The desktop must open in the paged review list.");
            var first=(CardItem)list.Items[0];list.SelectedItem=first;
            Check(first.IsSelected && ((Button)window.FindName("CompliantButton")).IsEnabled,"The list selection must drive the existing review controls.");
            var toggle=window.FindName("ThumbnailsModeButton") as Button;
            Check(toggle!=null,"The thumbnails view selector is missing.");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((FrameworkElement)window.FindName("GalleryScroll")).Visibility==Visibility.Visible && !list.IsVisible,"Switching to thumbnails must use the same loaded page.");
            Check(first.IsSelected,"Switching the view discarded the selected review item.");
            var theme=(Button)window.FindName("ThemeButton");
            var before=window.FindResource("PageBrush");
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(!SystemParameters.HighContrast)Check(!Object.Equals(before,window.FindResource("PageBrush")),"Theme switch did not change the page brush.");
            var quick=(Button)window.FindName("QuickModeButton");quick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((FrameworkElement)window.FindName("QuickPane")).IsVisible && list.Items.Count>0,"Quick Review must show the already-loaded page.");
            Check(ReviewerWindow.QuickReviewKey(Key.Left)=="NON_COMPLIANT" && ReviewerWindow.QuickReviewKey(Key.Right)=="COMPLIANT" &&
                ReviewerWindow.QuickReviewKey(Key.Up)==null && ReviewerWindow.QuickReviewDrag(-150,30,500)=="NON_COMPLIANT" &&
                ReviewerWindow.QuickReviewDrag(150,30,500)=="COMPLIANT" && ReviewerWindow.QuickReviewDrag(150,140,500)==null &&
                ReviewerWindow.QuickReviewDrag(10,0,500)==null,"Quick Review gestures must distinguish direction from small or vertical movement.");
        }
        internal static void CheckWideShell(Window window) {
            var sidebar=(FrameworkElement)window.FindName("QueueSidebar");var compact=(FrameworkElement)window.FindName("CompactQueues");
            var button=(Button)window.FindName("QueueSidebarButton");
            Check(sidebar.IsVisible && !compact.IsVisible,"The left sidebar must stay open when the window widens.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!sidebar.IsVisible && compact.IsVisible,"Collapsed sidebar queues must remain reachable in the dropdown.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(sidebar.IsVisible && !compact.IsVisible,"The left sidebar must expand again.");
        }
        internal static void SaveQuickReviewWithArrow(Window window) {
            var host=(FrameworkElement)window.FindName("QuickImageHost");
            window.Activate();host.Focus();
            Check(host.IsKeyboardFocusWithin,"The Quick Review image must accept keyboard focus.");
            var right=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(host),Environment.TickCount,Key.Right) {
                RoutedEvent=Keyboard.PreviewKeyDownEvent
            };
            host.RaiseEvent(right);
            Check(right.Handled,"Quick Review did not handle the right arrow.");
        }
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void Wait(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed>TimeSpan.FromSeconds(40))throw new TimeoutException("Timed out waiting for the reviewer engine.");Thread.Sleep(25);}}
        static UiResult Result(ReviewerEngine engine,string request) {
            UiResult result=null;
            Wait(()=>{UiResult reply;return engine.TryTakeResult(out reply) && reply.RequestId==request && (result=reply)!=null;});
            Check(result.Success,result.Error);return result;
        }
        internal static int Run(string directory) {
            Check(Native.SqliteVersion=="3.54.1","Linked SQLite was not loaded.");
            string root=Directory.CreateDirectory(Path.Combine(directory,"home-share")).FullName;
            string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
            string image=Path.Combine(folder,"test.png");
            File.WriteAllBytes(image,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/3Z8AAAAASUVORK5CYII="));
            var preview=Preview.Create(image);Check(preview.Width==1 && preview.Height==1,"Image preview did not decode.");
            string db=Path.Combine(directory,"review.sqlite");
            long id;string rootId;
            using(var engine=ReviewerEngine.Open(db,true)) {
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=2});
                Wait(()=>!engine.Status.RunningScan && engine.Status.ScanId==scan && engine.Status.ProcessedFiles==1);
                var first=Result(engine,engine.RequestPage(new PageRequest {Generation=1})).Page;
                Check(first.Total==1 && first.Rows.Count==1,"Scanned item is missing.");
                var row=first.Rows.Single();id=row.GroupId;rootId=row.RootId;
                Check(row.PreviewBytes!=null && row.PreviewWidth==1,"Thumbnail was not committed.");
                Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="COMPLIANT",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{id}}));
                var current=Result(engine,engine.RequestPage(new PageRequest {Generation=2,Bucket="COMPLIANT"})).Page;
                Check(current.Rows.Single().GroupId==id,"Review did not persist the group identity.");
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=3,Bucket="COMPLIANT",Search="fixture",Sort="reviewer",SortDescending=true})).Page.Rows.Single().GroupId==id,"The paged reviewer search did not find a saved label.");
                Result(engine,engine.RequestSavePreferences(new ReviewPreferences {DefaultSourceRoot=root},Guid.NewGuid().ToString("N")));
                Result(engine,engine.RequestIntegrity());
            }
            using(var database=new SqliteConnection(db,false))database.Execute("UPDATE settings SET value=? WHERE key='ui.v1.age'","201");
            using(var engine=ReviewerEngine.Open(db,false)) {
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                var saved=Result(engine,engine.RequestPage(new PageRequest {Generation=1,Bucket="COMPLIANT"})).Page.Rows.Single();
                Check(saved.GroupId==id && saved.Notes=="smoke test","Reopen lost the saved review.");
                var preferences=Result(engine,engine.RequestPreferences()).Preferences;
                Check(preferences.DefaultSourceRoot==root,"Reopen lost the default scan path.");
                Check(preferences.ModifiedAgeYears==0 && preferences.Warning.Length!=0,"An old unsupported scan age must fall back to the safe default.");
                string retry=engine.RequestPreviewRetry(new[]{id});Result(engine,retry);
                var details=Result(engine,engine.RequestPreviewRetryStatus(retry)).PreviewRetry;
                Check(details.Saved==1 && details.Items.Single().GroupId==id,"Preview task history lost the completed item.");
                string replacement=Directory.CreateDirectory(Path.Combine(directory,"replacement")).FullName;
                string copied=Path.Combine(Directory.CreateDirectory(Path.Combine(replacement,"example.user")).FullName,"test.png");
                File.Copy(image,copied);
                string connection=engine.RequestRootRemap(rootId,replacement,false);Result(engine,connection);
                var check=Result(engine,engine.RequestSourceConnectionStatus(connection)).SourceConnection;
                Check(check.Full==1 && check.Failed==0,"Per-root check did not verify the copied source.");
                Result(engine,engine.RequestApplySourceConnection(connection,false));
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=2,Bucket="COMPLIANT"})).Page.Rows.Single().SourcePath==copied,"Applied source did not become the active occurrence path.");
                const string password="generated test password";
                Result(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes(password),true));
                Result(engine,engine.RequestOriginalRetention(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="EVIDENCE",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{id}}));
                using(var original=Result(engine,engine.RequestEvidenceInspect(id)).Evidence)
                    Check(original.Verified && original.Size==new FileInfo(image).Length,"Encrypted original was not verified.");
                string package=Path.Combine(directory,"fixture.mcrevidence");
                Result(engine,engine.RequestEvidencePackageExport(new[]{id},package,Guid.NewGuid().ToString("N")));
                using(var reader=EvidencePackageReader.Open(package,CancellationToken.None,null)) {
                    reader.Unlock(Encoding.UTF8.GetBytes(password),CancellationToken.None,null);
                    using(var original=reader.Inspect(0,CancellationToken.None,null))Check(original.Verified,"Protected package original was not verified.");
                }
                string report=Path.Combine(directory,"report.csv");
                Result(engine,engine.RequestReport(new ReportOptions {Destination=report,IncludeEvidence=true}));
                Check(File.Exists(report) && File.ReadAllText(report).Contains("test.png"),"Evidence was missing from the explicitly requested report.");
                Result(engine,engine.RequestEvidenceReclassify(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="NON_COMPLIANT",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{id}}));
                var flagged=Result(engine,engine.RequestPage(new PageRequest {Generation=3,Bucket="NON_COMPLIANT"})).Page.Rows.Single();
                var sheet=Result(engine,engine.RequestCaseSheet(flagged)).Value;
                Check(sheet.Contains("Full SHA-256:") && sheet.Contains("Windows file owner:") && sheet.Contains("fixture"),"The case sheet did not include the recorded review and source metadata.");
                Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="TO_REVIEW",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{id}}));
            }
            CheckRenamed(directory,image);
            Console.WriteLine("Embedded SQLite, scan, renamed media, review, preferences, preview history, source reconnection, case sheet, report, integrity and encrypted Evidence passed.");return 0;
        }
        static void CheckRenamed(string directory,string image) {
            string root=Directory.CreateDirectory(Path.Combine(directory,"renamed-root")).FullName;
            string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
            string renamed=Path.Combine(folder,"renamed.dat");File.Copy(image,renamed);
            File.WriteAllText(Path.Combine(folder,"document.dat"),"<?xml version=\"1.0\"?><document>ordinary content</document>");
            using(var engine=ReviewerEngine.Open(Path.Combine(directory,"renamed.sqlite"),true)){
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                var options=new ScanOptions {RootPath=root,Extensions=new[]{"png"},WorkerCount=1};
                engine.StartScan(options);Wait(()=>!engine.Status.RunningScan);
                Check(Result(engine,engine.RequestPage(new PageRequest{Generation=1})).Page.Total==0,"Renamed media must be opt-in.");
                options.SniffRenamed=true;options.FingerprintPolicy=FingerprintPolicy.Full();
                engine.StartScan(options);Wait(()=>!engine.Status.RunningScan);
                var row=Result(engine,engine.RequestPage(new PageRequest{Generation=2,Type="png"})).Page.Rows.Single();
                Check(row.Name=="renamed.dat" && row.SourcePath==renamed && row.Extension=="png" && row.Algorithm=="sha256" && row.PreviewBytes!=null,"Renamed media did not retain its true filename, media family, preview and full hash.");
            }
        }
        internal static int PreviewFiles(IEnumerable<string> paths) {
            foreach(string path in paths) {
                var preview=Preview.Create(path);
                Check(preview.Bytes.Length>0 && preview.Width>0 && preview.Height>0,"Preview did not decode: "+path);
                Console.WriteLine(Path.GetFileName(path)+" · "+preview.Width+" × "+preview.Height+" · "+preview.Decoder);
            }
            return 0;
        }
        internal static int Import(string html,string output) {
            using(var source=ReviewerEngine.Open(html,false)) {
                Wait(()=>source.Ready || source.Faulted);
                Check(source.DetectedHtml && !source.Ready,"Open did not recognize the final HTML format.");
            }
            using(var imported=ReviewerEngine.ImportHtml(html,output)) {
                Wait(()=>imported.Ready || imported.Faulted);Check(imported.Ready,imported.Status.Error);
                Result(imported,imported.RequestIntegrity());
                var page=Result(imported,imported.RequestPage(new PageRequest {Generation=1})).Page;
                Check(page.Totals.Sum()>0,"The generated fixture did not produce review records.");
            }
            Console.WriteLine("Final HTML database detected and imported into a separate, valid catalog.");return 0;
        }
        internal static void SaveGallery(Window window,string output) {
            var view=(FrameworkElement)window.Content;
            var image=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32);
            image.Render(view);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using(var file=new FileStream(output,FileMode.CreateNew))encoder.Save(file);
        }
    }
}
