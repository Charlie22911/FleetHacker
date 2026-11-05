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
        const string FixturePassword="generated test password";
        internal static void CheckStartupPrompt(Window dialog) {
            var buttons=((WrapPanel)((StackPanel)dialog.Content).Children[2]).Children.OfType<Button>().ToArray();
            Check(buttons.Select(b=>b.Name).SequenceEqual(new[]{"StartupCatalogButton","StartupLaterButton"}),
                "The startup prompt must offer one catalog command and Later.");
            dialog.Loaded+=(s,e)=>dialog.Dispatcher.BeginInvoke(new Action(()=>buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        }
        internal static void CheckCatalogPath(string existing) {
            string unused=Path.Combine(Path.GetDirectoryName(existing),Guid.NewGuid().ToString("N")+".sqlite");
            Check(!ReviewerWindow.IsNewCatalogPath(existing) && ReviewerWindow.IsNewCatalogPath(unused),
                "The combined catalog picker must open an existing path and create only at a new path.");
        }

        internal static void CheckSidebarCommands(Window window) {
            var sidebar=(FrameworkElement)window.FindName("QueueSidebar");
            foreach(string name in new[]{"CatalogButton","MaintenanceButton","PreviewTasksButton","LogButton","HelpButton","AboutButton","ThemeButton",
                "ListModeButton","ThumbnailsModeButton","QuickModeButton","RefreshViewButton"})
                Check(sidebar.IsAncestorOf((Button)window.FindName(name)),"Expected sidebar command: "+name);
        }

        internal static void CheckQuickKeyMapping() {
            Check(ReviewerWindow.QuickReviewKey(Key.Up)=="COMPLIANT" && ReviewerWindow.QuickReviewKey(Key.Down)=="NON_COMPLIANT" &&
                ReviewerWindow.QuickReviewKey(Key.Left)==null && ReviewerWindow.QuickReviewKey(Key.Right)==null,
                "Only the vertical arrows should classify in Quick Review.");
        }

        internal static void CheckShell(Window window,string snapshot) {
            CheckSidebarCommands(window);
            CheckCommandSections(window);
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
            var item=(ListViewItem)list.ItemContainerGenerator.ContainerFromItem(first);
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=item});
            var inspection=window.OwnedWindows.OfType<Window>().SingleOrDefault(w=>w.Title.StartsWith("Inspect · "));
            Check(inspection!=null && inspection.IsVisible && list.IsVisible,"Double-click must open Inspect without changing the gallery view.");
            string firstTitle=inspection.Title;
            Check(InspectionArrow(inspection,Key.Right),"Inspect must handle the Right arrow.");
            inspection=window.OwnedWindows.OfType<Window>().Single(w=>w.Title.StartsWith("Inspect · "));
            Check(inspection.Title!=firstTitle && list.IsVisible,"Inspect must show the next item without changing the gallery view.");
            Check(InspectionArrow(inspection,Key.Left),"Inspect must handle the Left arrow.");
            inspection=window.OwnedWindows.OfType<Window>().Single(w=>w.Title.StartsWith("Inspect · "));
            Check(inspection.Title==firstTitle,"Inspect must return to the previous item.");
            inspection.Close();
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
            CheckQuickKeyMapping();
            Check(ReviewerWindow.QuickReviewDrag(-150,30,500,250)==Key.Left && ReviewerWindow.QuickReviewDrag(150,30,500,250)==Key.Right &&
                ReviewerWindow.QuickReviewDrag(15,-85,500,250)==Key.Up && ReviewerWindow.QuickReviewDrag(15,85,500,250)==Key.Down &&
                ReviewerWindow.QuickReviewDrag(150,140,500,250)==Key.None && ReviewerWindow.QuickReviewDrag(0,10,500,250)==Key.None,
                "Quick Review drags must recognize each direction and ignore short or diagonal movement.");
        }
        static void CheckCommandSections(Window window) {
            CheckVideoLifecycle();
            foreach(string name in new[]{"ScanSettingsExpander","FiltersExpander","ReviewNotesExpander","ReviewToolsExpander"}) {
                var section=window.FindName(name) as Expander;
                Check(section!=null && !section.IsExpanded,"Secondary commands must start collapsed: "+name);
                section.IsExpanded=true;window.UpdateLayout();
                Check(((FrameworkElement)section.Content).IsVisible,"Expanded commands must remain reachable: "+name);
                section.IsExpanded=false;
            }
            foreach(string name in new[]{"StartButton","ResumeButton","CancelButton","FilterButton","ResetButton","CompliantButton","NonCompliantButton","UnreviewedButton","SelectPageButton"})
                Check(((Button)window.FindName(name)).IsVisible,"Primary command is hidden: "+name);
            var notes=(TextBox)window.FindName("NotesBox");
            var noteSection=(Expander)window.FindName("ReviewNotesExpander");
            notes.Text="Generated draft";noteSection.IsExpanded=true;noteSection.IsExpanded=false;
            Check(notes.Text=="Generated draft","Collapsing review notes must preserve the draft.");notes.Text="";
            foreach(string name in new[]{"FormatsButton","OptionsButton","RetryPreviewsButton","ClearSelectionButton","VerifyButton","IdentityHistoryButton","RetainOriginalsButton","CaptureEvidenceButton","VaultButton","StoredOriginalButton","ExportOriginalButton"})
                Check(window.FindName(name) is Button,"A secondary command was lost: "+name);
            window.UpdateLayout();
            Check(((FrameworkElement)window.FindName("ReviewArea")).ActualHeight>=200,"Compact controls must leave useful review space at 1024x768.");
        }
        static void CheckVideoLifecycle() {
            var viewport=new Grid();viewport.Children.Add(new Image());
            Action<string,string> complete=null;
            bool cancelled=false;
            var player=new VideoPlaybackControls(viewport,callback=>{complete=callback;return ()=>cancelled=true;});
            var layout=(StackPanel)player.Child;
            var commands=(WrapPanel)layout.Children[2];
            var play=(Button)commands.Children[0];var stop=(Button)commands.Children[1];
            Check(complete==null && viewport.Children.Count==1,"Video must not autoplay on inspection.");
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(complete!=null && !play.IsEnabled && stop.IsEnabled,"Resolving a video must leave Stop available.");
            var stale=complete;stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));stale("C:\\unused-stale-video.mp4",null);
            Check(cancelled && viewport.Children.Count==1 && play.IsEnabled && !stop.IsEnabled,"A stopped source request must be cancelled and never start playback later.");
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));complete(null,"Generated source unavailable");
            Check(viewport.Children.Count==1 && play.IsEnabled && ((TextBlock)layout.Children[0]).Text=="Generated source unavailable","A failed source must preserve the poster and allow retry.");
            player.Prepare();player.Dispose();complete("C:\\unused-closed-video.mp4",null);
            Check(viewport.Children.Count==1 && !play.IsEnabled,"A closed inspector must reject late video results.");
        }
        static bool InspectionArrow(Window window,Key key) {
            var viewport=(Grid)((DockPanel)window.Content).Children.OfType<Grid>().Single();
            var arrow=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(viewport),Environment.TickCount,key) {
                RoutedEvent=Keyboard.PreviewKeyDownEvent
            };
            viewport.RaiseEvent(arrow);
            return arrow.Handled;
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
        static void QuickArrow(Window window,Key key) {
            var host=(FrameworkElement)window.FindName("QuickImageHost");
            window.Activate();host.Focus();
            Check(host.IsKeyboardFocusWithin,"The Quick Review image must accept keyboard focus.");
            var arrow=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(host),Environment.TickCount,key) {
                RoutedEvent=Keyboard.PreviewKeyDownEvent
            };
            host.RaiseEvent(arrow);
            Check(arrow.Handled,"Quick Review did not handle "+key+".");
        }
        internal static void CheckQuickNavigationAndMarkCompliant(Window window) {
            var pane=(FrameworkElement)window.FindName("QuickPane");
            var first=(CardItem)pane.DataContext;
            QuickArrow(window,Key.Left);
            Check(ReferenceEquals(pane.DataContext,first),"Left at the first image must keep the current item.");
            QuickArrow(window,Key.Right);
            var next=pane.DataContext as CardItem;
            Check(next!=null && !ReferenceEquals(first,next),"Right must display the next image.");
            QuickArrow(window,Key.Left);
            Check(ReferenceEquals(pane.DataContext,first) && first.CanSelect && next.CanSelect &&
                first.Record.Status=="TO_REVIEW" && next.Record.Status=="TO_REVIEW","Horizontal browsing must preserve both review decisions.");
            ((Button)window.FindName("QuickInspectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var inspection=window.OwnedWindows.OfType<Window>().Single(w=>w.Title.StartsWith("Inspect · "));
            Check(InspectionArrow(inspection,Key.Up),"Inspect must classify with the Up arrow.");
        }
        internal static void MarkQuickNonCompliant(Window window) {
            var pane=(FrameworkElement)window.FindName("QuickPane");
            var next=(CardItem)pane.DataContext;
            var inspection=window.OwnedWindows.OfType<Window>().Single(w=>w.Title.StartsWith("Inspect · "));
            Check(next!=null && inspection.Title=="Inspect · "+next.Name,"Inspect must advance after a successful classification.");
            inspection.Close();
            bool unlocked=false;
            var prompt=window.Dispatcher.BeginInvoke(new Action(()=>{
                var dialog=window.OwnedWindows.OfType<Window>().SingleOrDefault(w=>w.Title=="Unlock stored originals" && w.IsVisible);
                if(dialog==null)return;
                var panel=(StackPanel)dialog.Content;
                panel.Children.OfType<PasswordBox>().Single().Password=FixturePassword;
                unlocked=true;
                panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Single(b=>b.IsDefault)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            QuickArrow(window,Key.Down);
            if(!unlocked)prompt.Abort();
            Check(unlocked,"Down did not open the protected review prompt.");
        }
        internal static void CheckSavedQuickReviews(string path) {
            using(var database=new SqliteConnection(path,false)){
                foreach(string bucket in new[]{"COMPLIANT","NON_COMPLIANT"})
                    Check(Convert.ToInt64(database.Scalar("SELECT COUNT(*) FROM review_groups WHERE retired=0 AND status=?",bucket))==1,
                        "The review shortcuts did not save exactly one "+bucket+" decision.");
            }
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
                Result(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes(FixturePassword),true));
                Result(engine,engine.RequestOriginalRetention(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="EVIDENCE",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{id}}));
                using(var original=Result(engine,engine.RequestEvidenceInspect(id)).Evidence)
                    Check(original.Verified && original.Size==new FileInfo(image).Length,"Encrypted original was not verified.");
                string package=Path.Combine(directory,"fixture.mcrevidence");
                Result(engine,engine.RequestEvidencePackageExport(new[]{id},package,Guid.NewGuid().ToString("N")));
                using(var reader=EvidencePackageReader.Open(package,CancellationToken.None,null)) {
                    reader.Unlock(Encoding.UTF8.GetBytes(FixturePassword),CancellationToken.None,null);
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
                string quickRoot=Directory.CreateDirectory(Path.Combine(directory,"quick-navigation")).FullName;
                string quickFolder=Directory.CreateDirectory(Path.Combine(quickRoot,"example.user")).FullName;
                var encoder=new PngBitmapEncoder();
                var pixels=new byte[]{0,50,140,255,90,200,10,255};
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,1,96,96,PixelFormats.Bgra32,null,pixels,8)));
                using(var output=File.Create(Path.Combine(quickFolder,"next.png")))encoder.Save(output);
                string scan=engine.StartScan(new ScanOptions {RootPath=quickRoot,WorkerCount=1});
                Wait(()=>!engine.Status.RunningScan && engine.Status.ScanId==scan && engine.Status.ProcessedFiles==1);
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=4})).Page.Total==2,"The Quick Review navigation fixture is incomplete.");
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
