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
using System.Windows.Threading;

namespace MediaReviewer {
    static class Smoke {
        const string FixturePassword="generated test password";
        internal static void CheckStartupPrompt(Window dialog) {
            var buttons=((WrapPanel)((StackPanel)dialog.Content).Children[2]).Children.OfType<Button>().ToArray();
            Check(buttons.Select(b=>b.Name).SequenceEqual(new[]{"StartupCatalogButton","StartupLaterButton"}),
                "The startup prompt must offer one database command and Later.");
            dialog.Loaded+=(s,e)=>dialog.Dispatcher.BeginInvoke(new Action(()=>buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        }
        internal static void CheckCatalogPath(string existing) {
            string unused=Path.Combine(Path.GetDirectoryName(existing),Guid.NewGuid().ToString("N")+".sqlite");
            Check(!ReviewerWindow.IsNewCatalogPath(existing) && ReviewerWindow.IsNewCatalogPath(unused),
                "The combined database picker must open an existing path and create only at a new path.");
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
            Check(((Button)window.FindName("SteganographyButton")).Visibility==Visibility.Collapsed,"Analysis controls must remain hidden when the optional package is absent.");
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
            Check(popup!=null && popup.IsOpen && queues.Items.Count==6,"The themed compact dropdown must show all six review queues, including Steganography.");
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
            CheckLocationsAfterRescan(window,first.Record,Path.Combine(Path.GetDirectoryName(snapshot),"replacement"));
            var item=(ListViewItem)list.ItemContainerGenerator.ContainerFromItem(first);
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=item});
            var inspection=window.OwnedWindows.OfType<Window>().SingleOrDefault(w=>w.Title.StartsWith("Inspect · "));
            Check(inspection!=null && inspection.IsVisible && list.IsVisible,"Double-click must open Inspect without changing the gallery view.");
            string firstTitle=inspection.Title;
            var originalStyle=inspection.WindowStyle;
            var originalState=inspection.WindowState;
            Check(InspectionArrow(inspection,Key.F11) && inspection.WindowStyle==WindowStyle.None && inspection.WindowState==WindowState.Maximized,
                "Inspect must enter fullscreen with F11.");
            var originalInspection=inspection;
            Check(InspectionArrow(inspection,Key.Right),"Inspect must handle the Right arrow.");
            inspection=window.OwnedWindows.OfType<Window>().Single(w=>w.Title.StartsWith("Inspect · "));
            Check(inspection.Title!=firstTitle && list.IsVisible,"Inspect must show the next item without changing the gallery view.");
            Check(ReferenceEquals(inspection,originalInspection) && inspection.WindowStyle==WindowStyle.None,"Fullscreen must stay open on the next item.");
            Check(InspectionArrow(inspection,Key.Left),"Inspect must handle the Left arrow.");
            inspection=window.OwnedWindows.OfType<Window>().Single(w=>w.Title.StartsWith("Inspect · "));
            Check(inspection.Title==firstTitle,"Inspect must return to the previous item.");
            Check(InspectionArrow(inspection,Key.Escape) && inspection.WindowStyle==originalStyle && inspection.WindowState==originalState,
                "Escape must restore the Inspect window.");
            inspection.Close();
            var toggle=window.FindName("ThumbnailsModeButton") as Button;
            Check(toggle!=null,"The thumbnails view selector is missing.");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((FrameworkElement)window.FindName("GalleryScroll")).Visibility==Visibility.Visible && !list.IsVisible,"Switching to thumbnails must use the same loaded page.");
            Check(first.IsSelected,"Switching the view discarded the selected review item.");
            window.UpdateLayout();
            var gallery=(ItemsControl)window.FindName("CardList");
            var tile=(FrameworkElement)gallery.ItemContainerGenerator.ContainerFromItem(first);
            foreach(string name in new[]{"CardDetailsButton","CardInspectButton"}){
                var command=(Button)gallery.ItemTemplate.FindName(name,tile);
                command.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,command));
                string title=name=="CardDetailsButton" ? "Item details and notes" : "Inspect · "+first.Name;
                var opened=window.OwnedWindows.OfType<Window>().SingleOrDefault(w=>w.Title==title && w.IsVisible);
                Check(opened!=null,"Clicking the rendered thumbnail button did not open "+title+".");
                opened.Close();
            }
            var theme=(Button)window.FindName("ThemeButton");
            var before=window.FindResource("PageBrush");
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(!SystemParameters.HighContrast)Check(!Object.Equals(before,window.FindResource("PageBrush")),"Theme switch did not change the page brush.");
            var quick=(Button)window.FindName("QuickModeButton");quick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((FrameworkElement)window.FindName("QuickPane")).IsVisible && list.Items.Count>0,"Quick Review must show the already-loaded page.");
            var quickFull=(Button)window.FindName("QuickFullScreenButton");
            quickFull.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.WindowStyle==WindowStyle.None && !sidebar.IsVisible && quickFull.IsVisible,
                "Quick Review fullscreen must hide workspace chrome and keep its exit button available.");
            quickFull.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.WindowStyle!=WindowStyle.None && sidebar.IsVisible,"Quick Review must restore the previous workspace.");
            CheckQuickKeyMapping();
            Check(ReviewerWindow.QuickReviewDrag(-150,30,500,250)==Key.Left && ReviewerWindow.QuickReviewDrag(150,30,500,250)==Key.Right &&
                ReviewerWindow.QuickReviewDrag(15,-85,500,250)==Key.Up && ReviewerWindow.QuickReviewDrag(15,85,500,250)==Key.Down &&
                ReviewerWindow.QuickReviewDrag(150,140,500,250)==Key.None && ReviewerWindow.QuickReviewDrag(0,10,500,250)==Key.None,
                "Quick Review drags must recognize each direction and ignore short or diagonal movement.");
        }
        static void CheckLocationsAfterRescan(Window window,MediaRow row,string fixtureRoot) {
            string original=Path.Combine(fixtureRoot,"example.user","test.png");
            Check(String.Equals(original,row.SourcePath,StringComparison.OrdinalIgnoreCase),"Expected the generated rescan fixture.");
            var source=(TextBox)window.FindName("SourceBox");
            string previousSource=source.Text;
            ((Button)window.FindName("PaneDetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var details=window.OwnedWindows.OfType<Window>().Single(w=>w.Title=="Item details and notes");
            TextBox notes=null;string originalNotes=null;
            try {
                var body=(DockPanel)details.Content;
                var locations=body.Children.OfType<DockPanel>().Single().Children.OfType<ListView>().Single();
                var top=(StackPanel)body.Children.OfType<ScrollViewer>().Single().Content;
                var noteSection=top.Children.OfType<Expander>().Single(section=>Convert.ToString(section.Header).StartsWith("Notes"));
                notes=((StackPanel)noteSection.Content).Children.OfType<TextBox>().Single();
                WaitForWindow(()=>locations.Items.Count==1);originalNotes=notes.Text;
                notes.Text="Unsaved notes during rescan";
                string copy=Path.Combine(fixtureRoot,"example.user","rescan-copy.png");
                File.Copy(original,copy);
                source.Text=fixtureRoot;
                ((Button)window.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForWindow(()=>locations.Items.Count==2);
                Check(locations.Items.OfType<LocationRow>().Any(location=>location.FullPath==copy && !location.OtherCard),
                    "The open details window did not show the new same-root copy.");
                Check(notes.Text=="Unsaved notes during rescan","Refreshing locations must preserve an unsaved notes draft.");
            } finally {if(notes!=null)notes.Text=originalNotes??"";details.Close();source.Text=previousSource;}
        }
        static void WaitForWindow(Func<bool> condition,string failure="Timed out waiting for Item details to refresh recorded locations.") {
            var frame=new DispatcherFrame();
            var elapsed=Stopwatch.StartNew();
            var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(50)};
            timer.Tick+=(s,e)=>{if(condition() || elapsed.Elapsed>TimeSpan.FromSeconds(12))frame.Continue=false;};
            timer.Start();
            try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}
            Check(condition(),failure);
        }
        static void CheckCommandSections(Window window) {
            CheckVideoLifecycle();
            foreach(string name in new[]{"FiltersExpander","ReviewNotesExpander","ReviewToolsExpander"}) {
                var section=window.FindName(name) as Expander;
                Check(section!=null && !section.IsExpanded,"Secondary commands must start collapsed: "+name);
                section.IsExpanded=true;window.UpdateLayout();
                Check(((FrameworkElement)section.Content).IsVisible,"Expanded commands must remain reachable: "+name);
                section.IsExpanded=false;
            }
            Check(window.FindName("SettingsCogButton") is Button && ((Button)window.FindName("SettingsCogButton")).IsVisible && (string)((Button)window.FindName("SettingsCogButton")).Content=="⚙","Settings cog button must be a visible icon-only button in the top right.");
            foreach(string name in new[]{"StartButton","ResumeButton","CancelButton","FilterButton","ResetButton","CompliantButton","NonCompliantButton","UnreviewedButton","SelectPageButton"})
                Check(((Button)window.FindName(name)).IsVisible,"Primary command is hidden: "+name);
            var notes=(TextBox)window.FindName("NotesBox");
            var noteSection=(Expander)window.FindName("ReviewNotesExpander");
            notes.Text="Generated draft";noteSection.IsExpanded=true;noteSection.IsExpanded=false;
            Check(notes.Text=="Generated draft","Collapsing review notes must preserve the draft.");notes.Text="";
            ((Button)window.FindName("NoteTagPersonalButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(notes.Text=="Personal media","Quick note tag button did not populate NotesBox.");
            ((Button)window.FindName("ClearNotesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(notes.Text=="","Clear notes button did not clear NotesBox.");
            foreach(string name in new[]{"FormatsButton","OptionsButton","RetryPreviewsButton","ClearSelectionButton","UndoReviewButton","VerifyButton","IdentityHistoryButton","RetainOriginalsButton","CaptureEvidenceButton","VaultButton","StoredOriginalButton","ExportOriginalButton","QuickUndoButton","QuickZoomInButton","QuickRotateRightButton"})
                Check(window.FindName(name) is Button,"A secondary command was lost: "+name);
            Check(window.FindName("SmartFilterBox") is ComboBox && window.FindName("ActivityProgress") is ProgressBar,"SmartFilterBox or ActivityProgress is missing.");
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
            var arrow=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,key) {
                RoutedEvent=Keyboard.PreviewKeyDownEvent
            };
            window.RaiseEvent(arrow);
            return arrow.Handled;
        }
        sealed class FixtureDraftClassifier:IDraftClassifier {
            public string Device {get{return "";}}
            readonly double score;
            internal int Calls;
            internal FixtureDraftClassifier(double value){score=value;}
            public double? Score(byte[] preview,CancellationToken token){Calls++;return score;}
            public void Dispose(){}
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
        internal static void CheckQuickNavigationAndMarkCompliant(Window window,Action<PageData> applyPage) {
            var pane=(FrameworkElement)window.FindName("QuickPane");
            var first=(CardItem)pane.DataContext;
            QuickArrow(window,Key.Left);
            Check(ReferenceEquals(pane.DataContext,first),"Left at the first image must keep the current item.");
            QuickArrow(window,Key.Right);
            var next=pane.DataContext as CardItem;
            Check(next!=null && !ReferenceEquals(first,next),"Right must display the next image.");
            var zoom=(Button)window.FindName("QuickZoomInButton");
            var zoomReadout=(Button)window.FindName("QuickZoomResetButton");
            zoom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            object magnification=zoomReadout.Content;
            var thumbnail=next.Image;
            applyPage(new PageData {Page=1,PageSize=60,Total=2,Rows=new List<MediaRow>{next.Record,first.Record}});
            QuickArrow(window,Key.Left);
            Check(ReferenceEquals(pane.DataContext,next) && Equals(zoomReadout.Content,magnification) && ReferenceEquals(next.Image,thumbnail),
                "A passive page reorder replaced the active Quick Review item or its preview.");
            applyPage(new PageData {Page=1,PageSize=60,Total=2,Rows=new List<MediaRow>{first.Record,next.Record}});
            applyPage(new PageData {Page=1,PageSize=60,Total=1,Rows=new List<MediaRow>{next.Record}});
            Check(Equals(zoomReadout.Content,magnification),"Removing a preceding card reset the active Quick Review zoom.");
            applyPage(new PageData {Page=1,PageSize=60,Total=1,Rows=new List<MediaRow>{first.Record}});
            Check(Equals(zoomReadout.Content,"Fit"),"Quick Review did not reset the preview when the current item left the page.");
            applyPage(new PageData {Page=1,PageSize=60,Total=2,Rows=new List<MediaRow>{first.Record,next.Record}});
            QuickArrow(window,Key.Right);
            QuickArrow(window,Key.Left);
            var current=pane.DataContext as CardItem;
            Check(current!=null && current.Record.GroupId==first.Record.GroupId && current.CanSelect && next.CanSelect &&
                current.Record.Status=="TO_REVIEW" && next.Record.Status=="TO_REVIEW","Horizontal browsing must preserve both review decisions.");
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
        static UiResult WaitForResult(ReviewerEngine engine,string request) {
            UiResult result=null;
            Wait(()=>{UiResult reply;return engine.TryTakeResult(out reply) && reply.RequestId==request && (result=reply)!=null;});
            return result;
        }
        static UiResult Result(ReviewerEngine engine,string request) {
            var result=WaitForResult(engine,request);
            Check(result.Success,result.Error);return result;
        }
        sealed class DetailFixtureClassifier:IDraftClassifier {
            readonly int expected;internal int Calls;
            internal DetailFixtureClassifier(int side){expected=side;}
            public string Device {get{return "Detail fixture";}}
            public double? Score(byte[] bytes,CancellationToken token){
                token.ThrowIfCancellationRequested();int width,height;Preview.Dimensions(bytes,bytes.Length,out width,out height);
                Check(width==expected && height==expected*3/4,"Gemma must receive the requested image detail from the original, not an enlarged saved thumbnail.");Calls++;return .97;
            }
            public void Dispose(){}
        }
        static void CheckDraftDetail(string directory){
            string root=Path.Combine(directory,"draft-detail");Directory.CreateDirectory(root);string file=Path.Combine(root,"detail.png");
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())draw.DrawRectangle(Brushes.CornflowerBlue,null,new Rect(0,0,2048,1536));
            var bitmap=new RenderTargetBitmap(2048,1536,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(file))png.Save(stream);
            byte[] original=File.ReadAllBytes(file);DateTime modified=File.GetLastWriteTimeUtc(file);
            using(var engine=ReviewerEngine.Open(Path.Combine(directory,"draft-detail.sqlite"),true)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
                engine.StartScan(new ScanOptions {RootPath=root,Extensions=new[]{"png"},WorkerCount=1});Wait(()=>!engine.Status.RunningScan && engine.Status.ProcessedFiles==1);
                var request=new PageRequest {Bucket=DraftReview.Bucket,GemmaAvailable=true};
                foreach(int side in new[]{256,512,1024}){
                    Result(engine,engine.RequestDraftThreshold(55,true,true,"",side));
                    Check(Result(engine,engine.RequestPage(request)).Page.Total==0,"A different scan size must not reuse scores from another mode.");
                    var classifier=new DetailFixtureClassifier(side);
                    if(side>256){
                        File.Move(file,file+".offline");
                        try{var offline=Result(engine,engine.RequestDraftReview(()=>classifier,true));Check(classifier.Calls==0 && offline.Value.Contains("1 still items"),"Larger scans must report unavailable originals without scoring saved thumbnails.");Wait(()=>!engine.DraftReviewActive);}
                        finally{File.Move(file+".offline",file);}
                    }
                    Result(engine,engine.RequestDraftReview(()=>classifier,true));Wait(()=>!engine.DraftReviewActive);
                    var page=Result(engine,engine.RequestPage(request)).Page;
                    Check(classifier.Calls==1 && page.Total==1 && page.GemmaSide==side,"Each scan size must save and display its own result.");
                    Result(engine,engine.RequestDraftReview(()=>classifier,true));Wait(()=>!engine.DraftReviewActive);
                    Check(classifier.Calls==1,"An unchanged repeat scan must resume without rescoring completed items.");
                }
                Result(engine,engine.RequestDraftThreshold(55,true,true,"",256));
                Check(Result(engine,engine.RequestPage(request)).Page.Total==1,"Returning to Quick must preserve its previous result.");
                Result(engine,engine.RequestDraftThreshold(55,true,true,"",1024));
                engine.WaitWrite(db=>db.Execute("DELETE FROM ai_draft_scores WHERE model=?",DraftReview.ScoreModel("",1024)));
                File.SetLastWriteTimeUtc(file,modified.AddMinutes(1));
                try{var changed=new DetailFixtureClassifier(1024);Result(engine,engine.RequestDraftReview(()=>changed,true));Wait(()=>!engine.DraftReviewActive);Check(changed.Calls==0 && Result(engine,engine.RequestPage(request)).Page.Total==0,"A changed original must remain unscored until rescanned.");}
                finally{File.SetLastWriteTimeUtc(file,modified);}
                string other=Path.Combine(directory,"draft-detail-copy");Directory.CreateDirectory(other);File.Copy(file,Path.Combine(other,"copy.png"));
                string scan=engine.StartScan(new ScanOptions {RootPath=other,Extensions=new[]{"png"},WorkerCount=1});Wait(()=>!engine.Status.RunningScan && engine.Status.ScanId==scan && engine.Status.ProcessedFiles==1);
                File.Move(file,file+".offline");
                try{var duplicate=new DetailFixtureClassifier(1024);Result(engine,engine.RequestDraftReview(()=>duplicate,true));Wait(()=>!engine.DraftReviewActive);Check(duplicate.Calls==1 && Result(engine,engine.RequestPage(request)).Page.Total==2,"Larger scans must try an unchanged exact duplicate in another root when the first original is offline.");}
                finally{File.Move(file+".offline",file);}
                if(DraftModelWorker.GemmaAvailable()){
                    engine.WaitWrite(db=>db.Execute("DELETE FROM ai_draft_scores WHERE model=?",DraftReview.ScoreModel("",1024)));
                    Result(engine,engine.RequestDraftReview(true,true));Wait(()=>!engine.DraftReviewActive);
                    Check(engine.WaitWrite(db=>db.Scalar("SELECT score FROM ai_draft_scores WHERE model=?",DraftReview.ScoreModel("",1024))) is double,"The bounded Gemma worker must score an actual 1024-pixel input.");
                    Console.WriteLine("Gemma Thorough scored a 1024-pixel original copy in the private worker.");
                }
            }
            Check(original.SequenceEqual(File.ReadAllBytes(file)),"AI detail scans must leave original bytes unchanged.");
        }
        internal static int Run(string directory,string playbackFixtures=null) {
            Directory.CreateDirectory(directory);CheckDraftDetail(directory);
            Check(Native.SqliteVersion=="3.54.1","Linked SQLite was not loaded.");
            string root=Directory.CreateDirectory(Path.Combine(directory,"home-share")).FullName;
            string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
            string image=Path.Combine(folder,"test.png");
            File.WriteAllBytes(image,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/3Z8AAAAASUVORK5CYII="));
            var preview=Preview.Create(image);Check(preview.Width==1 && preview.Height==1,"Image preview did not decode.");
            CheckScratchRecovery(directory,image);
            string db=Path.Combine(directory,"review.sqlite");
            long id;string rootId,firstScan;
            using(var engine=ReviewerEngine.Open(db,true)) {
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=2});
                firstScan=scan;
                Wait(()=>!engine.Status.RunningScan && engine.Status.ScanId==scan && engine.Status.ProcessedFiles==1);
                var first=Result(engine,engine.RequestPage(new PageRequest {Generation=1})).Page;
                Check(first.Total==1 && first.Rows.Count==1,"Scanned item is missing.");
                Check(first.AiThreshold==7 && first.MarqoThreshold==7 && first.GemmaThreshold==55 && !first.GemmaCpuOnly,
                    "A database without saved AI thresholds must use the measured defaults for each model.");
                var row=first.Rows.Single();id=row.GroupId;rootId=row.RootId;
                Check(row.PreviewBytes!=null && row.PreviewWidth==1,"Thumbnail was not committed.");
                var scanDefaults=new ScanOptions();var catalogDefaults=new ReviewPreferences();
                Check(scanDefaults.IncludeRootFiles && scanDefaults.SniffRenamed && catalogDefaults.IncludeRootFiles && catalogDefaults.SniffRenamed,
                    "New scans and databases must include root-level files and detect renamed media by default.");
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=1,Bucket="AI_SUGGESTIONS"})).Page.Total==0,
                    "An unevaluated item must not appear in the AI suggestions queue.");
                var draft=new FixtureDraftClassifier(.86);
                File.Move(image,image+".offline");
                try{
                    string executable;using(var hostProcess=Process.GetCurrentProcess())executable=hostProcess.MainModule.FileName;
                    if(File.Exists(DraftModelWorker.ModelPath(executable))){
                        Result(engine,engine.RequestDraftReview());
                        Check(engine.WaitWrite(writer=>writer.Scalar("SELECT score FROM ai_draft_scores WHERE content_id=?",row.ContentId)) is double,
                            "The native model did not save a score with the source unavailable.");
                        engine.WaitWrite(writer=>writer.Execute("DELETE FROM ai_draft_scores WHERE content_id=?",row.ContentId));
                        Wait(()=>!engine.DraftReviewActive);
                    }
                    if(DraftModelWorker.GemmaAvailable()){
                        engine.ConfigureGemma(true,true,"");
                        Wait(()=>engine.GemmaStatus.StartsWith("Gemma is ready",StringComparison.Ordinal) ||
                            engine.GemmaStatus.StartsWith("Gemma could not load",StringComparison.Ordinal));
                        Check(engine.GemmaStatus=="Gemma is ready · CPU",engine.GemmaStatus);
                        var result=Result(engine,engine.RequestDraftReview(true,true));
                        Check(result.Value.Contains("1 new scores saved") && engine.GemmaStatus=="Gemma is ready · CPU",
                            "The preloaded Gemma worker must score an offline preview and stay ready after the draft.");
                        engine.WaitWrite(writer=>writer.Execute("DELETE FROM ai_draft_scores WHERE content_id=? AND model=?",row.ContentId,GemmaCriteria.Model("")));
                        Wait(()=>!engine.DraftReviewActive);
                        engine.ConfigureGemma(false,true,"");
                        Console.WriteLine("Gemma loaded before the draft and stayed ready after scoring an offline preview.");
                    }
                    Result(engine,engine.RequestDraftReview(()=>draft));
                }
                finally{File.Move(image+".offline",image);}
                var suggestions=Result(engine,engine.RequestPage(new PageRequest {Generation=2,Bucket="AI_SUGGESTIONS"})).Page;
                Check(draft.Calls==1 && suggestions.Total==1 && suggestions.AiTotal==1 && suggestions.Rows.Single().GroupId==id && suggestions.Rows.Single().Status=="TO_REVIEW" && suggestions.Rows.Single().AiScore==.86,
                    "The offline draft should retain the human status and present a suggestion for the scored preview.");
                Result(engine,engine.RequestDraftThreshold(90));
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=3,Bucket="AI_SUGGESTIONS"})).Page.Total==0,
                    "Raising the threshold must filter out the saved score.");
                Result(engine,engine.RequestDraftThreshold(75));
                Wait(()=>!engine.DraftReviewActive);
                Result(engine,engine.RequestDraftReview(()=>draft));
                Check(draft.Calls==1 && Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket="AI_SUGGESTIONS"})).Page.Total==1,
                    "Lowering the threshold must reuse the saved score.");
                Wait(()=>!engine.DraftReviewActive);
                Result(engine,engine.RequestDraftThreshold(80,true,true));
                engine.WaitWrite(writer=>{writer.Execute("INSERT INTO ai_draft_scores(content_id,preview_revision,model,score) VALUES(?,?,?,?) ON CONFLICT(content_id,model) DO UPDATE SET score=excluded.score",row.ContentId,row.PreviewRevision,DraftReview.Gemma,.97);return null;});
                var oldDefault=Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket="AI_SUGGESTIONS",GemmaAvailable=true})).Page;
                Check(oldDefault.UseGemma && oldDefault.Total==0,"Scores from the old default rubric must not be reused with the revised criteria.");
                Result(engine,engine.RequestDraftReview(()=>new FixtureDraftClassifier(.55),true));
                var gemmaPage=Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket="AI_SUGGESTIONS",GemmaAvailable=true})).Page;
                var fallbackPage=Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket="AI_SUGGESTIONS"})).Page;
                Check(gemmaPage.UseGemma && gemmaPage.Total==0 && gemmaPage.GemmaCpuOnly && !fallbackPage.UseGemma && fallbackPage.Total==1 && fallbackPage.AiThreshold==75,
                    "The model selection, cached scores, and thresholds must remain independent with a Marqo fallback.");
                Result(engine,engine.RequestDraftThreshold(55,true,true));
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket="AI_SUGGESTIONS",GemmaAvailable=true})).Page.Rows.Single().AiScore==.55,
                    "The Gemma threshold must include a matching saved policy tier.");
                string custom=GemmaCriteria.Default.Replace("maps, diagrams","train diagrams");
                Result(engine,engine.RequestDraftThreshold(55,true,true,custom));
                var customRequest=new PageRequest {Generation=4,Bucket=DraftReview.Bucket,GemmaAvailable=true};
                var customPage=Result(engine,engine.RequestPage(customRequest)).Page;
                Check(customPage.Total==0 && customPage.AiTotal==0 && customPage.GemmaCriteria==custom,
                    "Changing Gemma criteria must persist the text and hide scores for the prior rubric.");
                Wait(()=>!engine.DraftReviewActive);
                Result(engine,engine.RequestDraftReview(()=>new FixtureDraftClassifier(.90),true));
                Check(Result(engine,engine.RequestPage(customRequest)).Page.Rows.Single().AiScore==.90,
                    "Scores must be shown only for the current Gemma criteria.");
                Wait(()=>!engine.DraftReviewActive);
                Result(engine,engine.RequestDraftThreshold(55,true,true,GemmaCriteria.Default));
                Check(Result(engine,engine.RequestPage(customRequest)).Page.Rows.Single().AiScore==.55,
                    "Resetting the Gemma criteria must restore the cached default scores.");
                Check(Result(engine,engine.RequestPage(customRequest)).Page.GemmaCustomCriteria==custom,"Switching to a built-in preset must retain the saved custom rules.");
                Result(engine,engine.RequestDraftThreshold(55,true,true,custom));
                Check(Result(engine,engine.RequestPage(customRequest)).Page.Rows.Single().AiScore==.90,"Returning to unchanged Custom rules must reuse their saved scores.");
                Result(engine,engine.RequestDraftThreshold(90,true,true,""));
                using(var savedFilter=new SqliteConnection(db,true))Check((string)savedFilter.Scalar("SELECT value FROM settings WHERE key='ai.draft.gemma.custom.threshold'")=="55","Remembered advanced Custom rules must retain their own filter when a built-in preset is saved.");
                string plain=GemmaCriteria.FromRules("Firearms","Toys and water guns");
                Result(engine,engine.RequestDraftThreshold(97,true,true,plain));
                Check(Result(engine,engine.RequestPage(customRequest)).Page.Total==0,"Custom instructions must not reuse the default rubric's scores.");
                Result(engine,engine.RequestDraftReview(()=>new FixtureDraftClassifier(.97),true));Wait(()=>!engine.DraftReviewActive);
                var plainPage=Result(engine,engine.RequestPage(customRequest)).Page;
                Check(plainPage.Total==1 && plainPage.GemmaCriteria==plain,"Plain custom rules must persist and use their own scored suggestions.");
                Result(engine,engine.RequestDraftThreshold(97,true,true,GemmaCriteria.FromRules("Firearms","All drawings")));
                Check(Result(engine,engine.RequestPage(customRequest)).Page.Total==0,"Changing only exclusions must require new custom scores.");
                Result(engine,engine.RequestDraftThreshold(55,true,true,""));
                foreach(int percent in new[]{55,90,97}){
                    Result(engine,engine.RequestDraftThreshold(percent,true,true,""));
                    foreach(double score in new[]{.08,.55,.82,.90,.97}){
                        engine.WaitWrite(writer=>writer.Execute("UPDATE ai_draft_scores SET score=? WHERE content_id=? AND model=?",score,row.ContentId,GemmaCriteria.Model("")));
                        bool included=percent==55 ? score!=.08 : percent==90 ? score==.90 || score==.97 : score==.97;
                        Check((Result(engine,engine.RequestPage(customRequest)).Page.Total==1)==included,"Preset filtering must include only its supported Gemma categories.");
                    }
                }
                engine.WaitWrite(writer=>writer.Execute("UPDATE ai_draft_scores SET score=.55 WHERE content_id=? AND model=?",row.ContentId,GemmaCriteria.Model("")));
                Result(engine,engine.RequestDraftThreshold(55,true,true,""));
                Result(engine,engine.RequestDraftThreshold(75));
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket=DraftReview.Bucket})).Page.Rows.Single().AiScore==.86,
                    "Changing the Gemma criteria must leave Marqo scores untouched.");
                var reviewed=Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="COMPLIANT",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{id}}));
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=5,Bucket="AI_SUGGESTIONS"})).Page.Total==0,
                    "A human decision must clear an AI suggestion.");
                var undo=new ReviewUndoItem {
                    GroupId=id,ExpectedRevision=reviewed.ReviewRevisions[id],Status="TO_REVIEW",Reviewer="",Notes=""
                };
                string undoId=Guid.NewGuid().ToString("N");
                Result(engine,engine.RequestUndoReview(new[]{undo},undoId));
                Check(Result(engine,engine.RequestUndoReview(new[]{undo},undoId)).Reconciled,"A committed Undo was not recognized on replay.");
                var restored=Result(engine,engine.RequestInspector(id,0)).Inspector;
                Check(restored.Status=="TO_REVIEW" && restored.Notes=="","Undo did not restore the prior decision and notes.");
                engine.WaitWrite(writer=>writer.Execute("UPDATE previews SET revision=revision+1 WHERE content_id=?",row.ContentId));
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=6,Bucket="AI_SUGGESTIONS"})).Page.Total==0,
                    "A replaced preview must invalidate its AI suggestion until it is scored again.");
                reviewed=Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="COMPLIANT",Reviewer="fixture",Notes="old notes",GroupIds=new[]{id}}));
                undo.ExpectedRevision=reviewed.ReviewRevisions[id];
                Result(engine,engine.RequestNotes(new NotesRequest {
                    GroupId=id,ContentId=row.ContentId,ExpectedRevision=undo.ExpectedRevision,
                    OperationId=Guid.NewGuid().ToString("N"),Reviewer="fixture",Notes="smoke test"
                }));
                Check(!WaitForResult(engine,engine.RequestUndoReview(new[]{undo},Guid.NewGuid().ToString("N"))).Success,
                    "Undo should reject an intervening notes edit.");
                var current=Result(engine,engine.RequestPage(new PageRequest {Generation=2,Bucket="COMPLIANT"})).Page;
                Check(current.Rows.Single().GroupId==id,"Review did not persist the group identity.");
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=3,Bucket="COMPLIANT",Search="fixture",Sort="reviewer",SortDescending=true})).Page.Rows.Single().GroupId==id,"The paged reviewer search did not find a saved label.");
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket="COMPLIANT",Search="smoke test"})).Page.Rows.Single().GroupId==id,"The paged search did not find saved decision notes.");
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=5,Bucket="COMPLIANT",Filter="notes"})).Page.Rows.Single().GroupId==id,"The notes smart filter did not match the annotated item.");
                Result(engine,engine.RequestDatabaseCleanup());
                Result(engine,engine.RequestSavePreferences(new ReviewPreferences {DefaultSourceRoot=root},Guid.NewGuid().ToString("N")));
                Result(engine,engine.RequestIntegrity());
            }
            using(var database=new SqliteConnection(db,false)){
                database.Execute("UPDATE settings SET value=? WHERE key='ui.v1.age'","201");
                SeedInterruptedVerification(database,id);
            }
            using(var engine=ReviewerEngine.Open(db,false)) {
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                using(var reader=new SqliteConnection(db,true)){
                    Wait(()=>reader.Scalar("SELECT id FROM identity_runs WHERE id='interrupted-test'")==null);
                    Check(reader.Scalar("SELECT id FROM identity_runs WHERE id='published-test'")!=null &&
                        reader.Scalar("PRAGMA foreign_key_check")==null &&
                        reader.Scalar("SELECT run_id FROM identity_checks WHERE run_id='interrupted-test'")==null &&
                        reader.Scalar("SELECT run_id FROM identity_plan WHERE run_id='interrupted-test'")==null &&
                        reader.Scalar("SELECT run_id FROM resolved_locations WHERE run_id='interrupted-test'")==null,
                        "Startup recovery did not clean up an interrupted verification safely.");
                }
                var reopenedPage=Result(engine,engine.RequestPage(new PageRequest {Generation=1,Bucket="COMPLIANT"})).Page;
                var saved=reopenedPage.Rows.Single();
                Check(reopenedPage.AiThreshold==75 && reopenedPage.GemmaCpuOnly,"Reopen lost the database's saved AI settings.");
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
                engine.WaitWrite(writer=>writer.Execute("UPDATE scans SET state='cancelled',completed_at=NULL WHERE id=?",firstScan));
                engine.ResumeScan(firstScan,1);Wait(()=>!engine.Status.RunningScan);
                Check(String.IsNullOrEmpty(engine.Status.Error) && engine.Status.CompletedUsers==2,
                    "The unchanged root must resume the saved manifest.");
                engine.WaitWrite(writer=>{
                    writer.Execute("UPDATE scans SET state='cancelled',completed_at=NULL WHERE id=?",firstScan);
                    writer.Execute("INSERT INTO jobs(scan_id,user_ordinal,kind,path,path_key,state) VALUES(?,0,'archive','pending.zip','PENDING.ZIP','pending')",firstScan);
                    return null;
                });
                Result(engine,engine.RequestApplySourceConnection(connection,false));
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=2,Bucket="COMPLIANT"})).Page.Rows.Single().SourcePath==copied,"Applied source did not become the active occurrence path.");
                engine.ResumeScan(firstScan,1);Wait(()=>!engine.Status.RunningScan);
                Check(engine.Status.Error.Contains("start a fresh scan"),"Resume accepted a copied source for the old manifest.");
                using(var reader=new SqliteConnection(db,true))
                    Check((string)reader.Scalar("SELECT state FROM scans WHERE id=?",firstScan)=="cancelled" &&
                        (string)reader.Scalar("SELECT state FROM jobs WHERE scan_id=? AND path='pending.zip'",firstScan)=="pending",
                        "Refusing resume must preserve the saved manifest and queued work.");
                Result(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes(FixturePassword),true));
                Result(engine,engine.RequestOriginalRetention(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="EVIDENCE",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{id}}));
                Result(engine,engine.RequestVaultLock());
                var evidenceCard=Result(engine,engine.RequestPage(new PageRequest {Generation=3,Bucket="EVIDENCE"})).Page.Rows.Single();
                var ordinaryDetails=WaitForResult(engine,engine.RequestInspector(id,0));
                var ordinaryLocations=WaitForResult(engine,engine.RequestLocations(id,""));
                var ordinaryWrite=WaitForResult(engine,engine.RequestNotes(new NotesRequest {
                    GroupId=id,ContentId=evidenceCard.ContentId,ExpectedRevision=evidenceCard.ReviewRevision,
                    OperationId=Guid.NewGuid().ToString("N"),Reviewer="unexpected",Notes="must not overwrite Evidence"
                }));
                Check(!ordinaryDetails.Success && !ordinaryLocations.Success && !ordinaryWrite.Success,
                    "Ordinary Details, locations and notes must be unavailable for locked Evidence.");
                Result(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes(FixturePassword),false));
                using(var original=Result(engine,engine.RequestEvidenceInspect(id)).Evidence)
                    Check(original.Verified && original.Size==new FileInfo(image).Length &&
                        EvidencePackageSession.Describe(original).Contains("smoke test"),"Authenticated Evidence did not preserve the original and its notes.");
                string package=Path.Combine(directory,"fixture.mcrevidence");
                Result(engine,engine.RequestEvidencePackageExport(new[]{id},package,Guid.NewGuid().ToString("N")));
                using(var reader=EvidencePackageReader.Open(package,CancellationToken.None,null)) {
                    reader.Unlock(Encoding.UTF8.GetBytes(FixturePassword),CancellationToken.None,null);
                    using(var original=reader.Inspect(0,CancellationToken.None,null))Check(original.Verified,"Protected package original was not verified.");
                }
                string report=Path.Combine(directory,"report.csv");
                Result(engine,engine.RequestReport(new ReportOptions {Destination=report,IncludeEvidence=true}));
                string reportText=File.ReadAllText(report);
                Check(reportText.Contains("test.png") && reportText.Contains("smoke test"),"Evidence was missing from the explicitly requested report.");
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
                var batch=Result(engine,engine.RequestPage(new PageRequest {Generation=4})).Page.Rows.OrderBy(r=>r.GroupId).ToArray();
                Check(batch.Length==2,"The Quick Review navigation fixture is incomplete.");
                var committed=Result(engine,engine.RequestReview(new ReviewRequest {
                    OperationId=Guid.NewGuid().ToString("N"),Status="COMPLIANT",Reviewer="batch",Notes="batch decision",
                    GroupIds=batch.Select(r=>r.GroupId).ToArray()
                }));
                var restoreBatch=batch.Select(r=>new ReviewUndoItem {
                    GroupId=r.GroupId,ExpectedRevision=committed.ReviewRevisions[r.GroupId],
                    Status=r.Status,Reviewer=r.Reviewer,Notes=r.Notes
                }).ToArray();
                var last=batch[1];
                Result(engine,engine.RequestNotes(new NotesRequest {
                    GroupId=last.GroupId,ContentId=last.ContentId,ExpectedRevision=restoreBatch[1].ExpectedRevision,
                    OperationId=Guid.NewGuid().ToString("N"),Reviewer="batch",Notes="newer notes"
                }));
                Check(!WaitForResult(engine,engine.RequestUndoReview(restoreBatch,Guid.NewGuid().ToString("N"))).Success,
                    "A stale item must reject an entire Undo batch.");
                var afterConflict=Result(engine,engine.RequestPage(new PageRequest {Generation=5,Bucket="COMPLIANT"})).Page.Rows;
                Check(afterConflict.Count==2 && afterConflict.Single(r=>r.GroupId==batch[0].GroupId).Notes=="batch decision" &&
                    afterConflict.Single(r=>r.GroupId==last.GroupId).Notes=="newer notes",
                    "A rejected Undo changed another item or overwrote newer notes.");
                CheckPageRefresh(engine,batch,quickRoot,quickFolder);
            }
            CheckRenamed(directory,image);
            CheckCrossRootCopies(directory,image);
            CheckMixedFingerprints(directory,image);
            CheckRecordedCopyFallback(directory,image);
            if(playbackFixtures!=null){
                CheckPlaybackFormats(directory,playbackFixtures);
                CheckMotionDraft(directory,playbackFixtures);
                if(File.Exists(Path.Combine(playbackFixtures,"wide.webp")))CheckLinkedDimensions(playbackFixtures);
            }
            Console.WriteLine("Embedded SQLite, scan, duplicate locations, renamed media, review, preferences, preview history, source reconnection, case sheet, report, integrity and encrypted Evidence passed.");return 0;
        }
        static void SeedInterruptedVerification(SqliteConnection db,long group) {
            db.Script("PRAGMA foreign_keys=ON");
            Verification.Transaction(db,()=>{
                foreach(string state in new[]{"prepared","published"})
                    db.Execute("INSERT INTO identity_runs(id,group_id,source_content,root_id,state,created_at) SELECT ?,id,content_id,root_id,?,? FROM review_groups WHERE id=?",
                        state=="prepared" ? "interrupted-test" : "published-test",state,Catalog.Now(),group);
                db.Execute("INSERT INTO identity_plan SELECT 'interrupted-test',c.digest,g.content_id,g.id,0,0 FROM review_groups g JOIN contents c ON c.id=g.content_id WHERE g.id=?",group);
                db.Execute(@"INSERT INTO identity_checks SELECT 'interrupted-test',o.id,o.occurrence_order,o.path,o.size,o.modified_ticks,
 o.file_identity,o.modified_ticks,o.file_identity,c.digest FROM group_occurrences o JOIN contents c ON c.id=o.content_id WHERE o.group_id=?",group);
                db.Execute("INSERT INTO resolved_locations SELECT run_id,?,digest,occurrence_id FROM identity_checks WHERE run_id='interrupted-test'",group);
            });
        }
        static void CheckRecordedCopyFallback(string directory,string image) {
            string root=Path.Combine(directory,"retention-sources"),db=Path.Combine(directory,"retention-sources.sqlite");
            string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
            string first=Path.Combine(folder,"original.png"),second=Path.Combine(folder,"duplicate.png");
            File.Copy(image,first);File.Copy(image,second);
            using(var engine=ReviewerEngine.Open(db,true)){
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1,FingerprintPolicy=FingerprintPolicy.Full()});
                Wait(()=>!engine.Status.RunningScan);Check(String.IsNullOrEmpty(engine.Status.Error),engine.Status.Error);
                var reviewed=Result(engine,engine.RequestPage(new PageRequest())).Page.Rows.Single();
                Check(reviewed.LocationCount==2,"The fallback fixture must have two copies on one card.");
                long group=reviewed.GroupId;
                Result(engine,engine.RequestReview(new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="COMPLIANT",GroupIds=new[]{group}}));
                string preferred=reviewed.SourcePath,alternate=preferred==first ? second : first;
                string parked=Path.Combine(directory,"parked-copy.png");
                File.Move(preferred,parked);
                string retry=engine.RequestPreviewRetry(new[]{group});Result(engine,retry);
                Check(Result(engine,engine.RequestPreviewRetryStatus(retry)).PreviewRetry.Saved==1,"Preview retry did not use the alternate copy.");
                Result(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes(FixturePassword),true));
                var capture=new ReviewRequest {OperationId=Guid.NewGuid().ToString("N"),Status="EVIDENCE",Reviewer="fixture",Notes="smoke test",GroupIds=new[]{group}};
                string held=Path.Combine(directory,"held-copy.png");
                File.Move(alternate,held);
                Check(!WaitForResult(engine,engine.RequestOriginalRetention(capture)).Success,"An unavailable original must not be published.");
                using(var reader=new SqliteConnection(db,true))
                    Check(reader.Scalar("SELECT id FROM original_captures WHERE request_id=?",capture.OperationId)==null &&
                        (string)reader.Scalar("SELECT status FROM review_groups WHERE id=?",group)=="COMPLIANT",
                        "Failed capture left staging rows or changed the saved review.");
                File.Move(held,alternate);
                capture.OperationId=Guid.NewGuid().ToString("N");
                Result(engine,engine.RequestOriginalRetention(capture));
                using(var original=Result(engine,engine.RequestEvidenceInspect(group)).Evidence)
                    Check(original.Verified && EvidencePackageSession.Describe(original).Contains(Path.GetFileName(alternate)),
                        "Retention did not authenticate the remaining copy.");
            }
        }
        static void CheckPageRefresh(ReviewerEngine engine,MediaRow[] batch,string quickRoot,string quickFolder) {
            var ids=batch.Select(r=>r.GroupId).ToArray();
            Result(engine,engine.RequestReview(new ReviewRequest {
                OperationId=Guid.NewGuid().ToString("N"),Status="TO_REVIEW",Notes="matches-refresh",GroupIds=ids
            }));
            var request=new PageRequest {Generation=6,Search="matches-refresh",Sort="name"};
            var initial=Result(engine,engine.RequestPage(request)).Page;
            Check(initial.Total==2 && initial.Rows.All(row=>row.PreviewBytes!=null),"The initial refresh fixture did not have both thumbnails.");
            request.CachedPreviews=initial.Rows.ToDictionary(r=>r.GroupId,r=>new PreviewVersion(r));
            var next=initial.Rows.Single(r=>r.GroupId==batch[1].GroupId);
            File.Copy(Path.Combine(quickFolder,"next.png"),Path.Combine(quickFolder,"copy.png"));
            string scan=engine.StartScan(new ScanOptions {RootPath=quickRoot,WorkerCount=1});
            Wait(()=>!engine.Status.RunningScan && engine.Status.ScanId==scan && engine.Status.ProcessedFiles==2);
            Result(engine,engine.RequestReview(new ReviewRequest {
                OperationId=Guid.NewGuid().ToString("N"),Status="COMPLIANT",GroupIds=new[]{batch[0].GroupId}
            }));
            var refill=Result(engine,engine.RequestPage(request)).Page;
            Check(refill.Total==1 && refill.Totals[0]==1 && refill.Totals[1]==1 &&
                refill.Revision>initial.Revision && refill.Rows.Single().GroupId==next.GroupId,
                "Refill did not reconcile the current page and its totals after a review.");
            var retained=refill.Rows.Single();
            Check(retained.LocationCount==2 && retained.PreviewBytes==null && retained.PreviewRevision==next.PreviewRevision,
                "Refill must update the copy count and reuse an unchanged preview.");
            var card=new CardItem(next);
            byte[] cached=next.PreviewBytes;
            card.Update(retained);
            Check(ReferenceEquals(card.Record.PreviewBytes,cached),"Refill discarded an encoded thumbnail waiting for decode.");
            Result(engine,engine.RequestNotes(new NotesRequest {
                GroupId=next.GroupId,ContentId=next.ContentId,ExpectedRevision=retained.ReviewRevision,
                OperationId=Guid.NewGuid().ToString("N"),Notes="no longer in filter"
            }));
            var removed=Result(engine,engine.RequestPage(request)).Page;
            Check(removed.Total==0 && removed.Rows.Count==0 && removed.Revision>refill.Revision,
                "Refill returned a retained card that no longer matched the search.");
            Result(engine,engine.RequestReview(new ReviewRequest {
                OperationId=Guid.NewGuid().ToString("N"),Status="TO_REVIEW",GroupIds=ids
            }));
        }
        static void CheckScratchRecovery(string directory,string image) {
            string archive=Path.Combine(directory,"media-preview.zip");
            using(var output=File.Create(archive))
            using(var zip=new System.IO.Compression.ZipArchive(output,System.IO.Compression.ZipArchiveMode.Create))
            using(var entry=zip.CreateEntry("fixture.png").Open())
            using(var original=File.OpenRead(image))original.CopyTo(entry);
            var thumbnail=Preview.Create(archive+Archives.Separator+"fixture.png");
            Check(thumbnail.Width==1 && thumbnail.Height==1,"The ZIP member did not decode through the private scratch directory.");
            string temp=Path.GetTempPath();
            Func<string> create=()=>Directory.CreateDirectory(Path.Combine(temp,"MediaComplianceReviewer-"+Guid.NewGuid().ToString("N"))).FullName;
            string interrupted=create(),live=create(),unrelated=create();
            string name="archive-preview-"+Guid.NewGuid().ToString("N")+".png";
            foreach(string folder in new[]{interrupted,live,unrelated})File.Copy(image,Path.Combine(folder,name));
            foreach(string folder in new[]{interrupted,live})
                File.WriteAllBytes(Path.Combine(folder,"media-owner-v1"),Encoding.ASCII.GetBytes("MCR-MEDIA-1\n"+Path.GetFileName(folder)+"\n"));
            using(var other=new FileStream(Path.Combine(live,"media-owner-v1"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
                using(var locked=File.OpenRead(Path.Combine(interrupted,name))){
                    MediaScratch.Recover(temp);
                    Check(File.Exists(Path.Combine(interrupted,"media-owner-v1")),"Recovery lost ownership of a locked temporary file.");
                }
                MediaScratch.Recover(temp);
                Check(!Directory.Exists(interrupted) && File.Exists(Path.Combine(live,name)) && File.Exists(Path.Combine(unrelated,name)),
                    "Recovery must reclaim abandoned files and preserve live or unmarked directories.");
            }
            MediaScratch.Recover(temp);
            Check(!Directory.Exists(live),"Recovery did not reclaim a directory after the helper released its lease.");
            File.Delete(Path.Combine(unrelated,name));Directory.Delete(unrelated,false);
            Console.WriteLine("ZIP preview and interrupted scratch recovery passed.");
        }
        static void CheckLinkedDimensions(string fixtures) {
            foreach(string name in new[]{"wide","tall","square"}){
                string path=Path.Combine(fixtures,name+".webp");
                var fit=Preview.CreateExpected(path,-1,0,null,800,InspectionPlan.EncodedLimit);
                Check(Math.Max(fit.Width,fit.Height)==800,"Linked preview did not fit the requested viewport.");
                var zoom=Preview.CreateExpected(path,-1,0,null,8192,InspectionPlan.EncodedLimit);
                int width=name=="wide" ? 6000 : name=="tall" ? 1000 : 4000;
                int height=name=="wide" ? 1000 : name=="tall" ? 6000 : 4000;
                Check(zoom.SourceWidth==width && zoom.SourceHeight==height && (long)zoom.Width*zoom.Height<=InspectionPlan.MaxPixels,
                    "Linked preview reported the wrong dimensions or exceeded the decoded pixel budget.");
                Check(name=="square" ? zoom.Width==2896 && zoom.Height==2896 : zoom.Width==width && zoom.Height==height,
                    "Linked preview lost aspect-aware detail or exceeded the square limit.");
            }
            Console.WriteLine("Wide and tall WebP retained 6,000 pixels, and square WebP stayed within the pixel cap.");
        }
        static void CheckMotionDraft(string directory,string fixtures){
            string[] names={"draft-timeline.mp4","draft-timeline.gif"};
            if(names.Any(name=>!File.Exists(Path.Combine(fixtures,name))))return;
            string root=Directory.CreateDirectory(Path.Combine(directory,"motion-draft")).FullName;
            foreach(string name in names)File.Copy(Path.Combine(fixtures,name),Path.Combine(root,name));
            using(var engine=ReviewerEngine.Open(Path.Combine(directory,"motion-draft.sqlite"),true)){
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1});
                Wait(()=>!engine.Status.RunningScan);
                var classifier=new MotionFixtureClassifier();
                string offline=Path.Combine(root,names[0]);
                File.Move(offline,offline+".offline");
                try{
                    Result(engine,engine.RequestDraftReview(()=>classifier));
                    var suggestions=Result(engine,engine.RequestPage(new PageRequest {Bucket=DraftReview.Bucket})).Page;
                    Check(suggestions.Rows.Count==1 && suggestions.Rows[0].Extension=="gif" && classifier.Calls==3,
                        "A later GIF frame must produce a suggestion while the unavailable video stays unscored.");
                }finally{File.Move(offline+".offline",offline);}
                Wait(()=>!engine.DraftReviewActive);
                Result(engine,engine.RequestDraftReview(()=>classifier));
                var page=Result(engine,engine.RequestPage(new PageRequest {Bucket=DraftReview.Bucket})).Page;
                Check(page.Rows.Count==2 && page.Rows.All(row=>row.AiScore==.91) && classifier.Calls==8,
                    "Motion scores must keep the highest frame and reuse the completed GIF score.");
            }
            Console.WriteLine("Video and GIF AI sampling, later-frame scoring, source retry and score reuse passed.");
        }

        sealed class MotionFixtureClassifier:IDraftClassifier {
            public string Device {get{return "";}}
            internal int Calls;
            public double? Score(byte[] bytes,CancellationToken token){
                Calls++;
                var frame=new System.Windows.Media.Imaging.FormatConvertedBitmap(MediaLoader.Decode(bytes,256,token),PixelFormats.Bgr24,null,0);
                var pixel=new byte[3];frame.CopyPixels(new Int32Rect(frame.PixelWidth/2,frame.PixelHeight/2,1,1),pixel,3,0);
                return pixel[2]>pixel[0]+40 ? .91 : .01;
            }
            public void Dispose(){}
        }

        static void CheckPlaybackFormats(string directory,string fixtures) {
            string root=Path.Combine(directory,"playback-formats");
            string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
            var names=new List<string>{"legacy.flv","animated.gif"};
            if(File.Exists(Path.Combine(fixtures,"audio.webm")))names.Add("audio.webm");
            foreach(string name in names)File.Copy(Path.Combine(fixtures,name),Path.Combine(folder,name));
            using(var engine=ReviewerEngine.Open(Path.Combine(directory,"playback-formats.sqlite"),true)){
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1});
                Wait(()=>!engine.Status.RunningScan);
                var rows=Result(engine,engine.RequestPage(new PageRequest {Generation=1})).Page.Rows;
                Check(rows.Count==names.Count && rows.All(row=>row.PreviewBytes!=null),"All playback fixtures must scan and produce saved posters.");
                foreach(var row in rows) {
                    bool gif=row.Extension=="gif";
                    string proxy;
                    using(var entered=new ManualResetEventSlim())
                    using(var release=new ManualResetEventSlim()){
                        string background=engine.EnqueueOperation("SmokeHold",(db,token,progress)=>{
                            entered.Set();release.Wait(token);return "Background task released";
                        });
                        try{
                            Wait(()=>entered.IsSet);
                            proxy=Result(engine,engine.RequestVideoPlayback(row)).Value;
                        }finally{release.Set();}
                        Result(engine,background);
                    }
                    Check(File.Exists(proxy),"The isolated worker did not create a playback copy.");
                    var viewport=new Grid();
                    var player=new VideoPlaybackControls(viewport,done=>{done(proxy,null);return null;}) {TemporarySource=true,Loop=gif};
                    var dock=new DockPanel();
                    DockPanel.SetDock(player,Dock.Bottom);dock.Children.Add(player);dock.Children.Add(viewport);
                    var window=new Window {Title="Playback format smoke check",Width=680,Height=440,Content=dock};
                    try {
                        window.Show();
                        var commands=(WrapPanel)((StackPanel)player.Child).Children[2];
                        var play=(Button)commands.Children[0];
                        ((Button)commands.Children[4]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        player.Prepare();
                        var media=viewport.Children.OfType<MediaElement>().Single();
                        WaitForWindow(()=>media.HasVideo && media.NaturalDuration.HasTimeSpan,"The video did not preroll.");
                        Check(media.Opacity==0 && media.Position==TimeSpan.Zero,"Paused preroll must leave the poster visible.");
                        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        bool ended=false;
                        media.MediaEnded+=(s,e)=>ended=true;
                        WaitForWindow(()=>media.Position.TotalMilliseconds>150 && media.Opacity==1,"Windows did not reveal the playing "+row.Extension+" copy.");
                        Check(media.HasVideo && (gif || media.HasAudio),"The playback copy lost the "+row.Extension+" video or audio stream.");
                        ((Button)commands.Children.OfType<Button>().Single(b=>Object.Equals(b.Content,"1×"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(Math.Abs(media.SpeedRatio-1.25)<0.001,"Playback speed must apply to the active media.");
                        if(gif){
                            WaitForWindow(()=>ended,"Animated GIF did not reach the end of its frame sequence.");
                            WaitForWindow(()=>media.Position.TotalMilliseconds>150 && media.Position.TotalSeconds<1,"Animated GIF did not loop.");
                        }
                    }finally{player.Dispose();window.Close();}
                    WaitForWindow(()=>!File.Exists(proxy),"The playback copy was not released.");
                }
            }
            Console.WriteLine("Offline playback passed for "+String.Join(", ",names)+".");
        }
        static void CheckCrossRootCopies(string directory,string image) {
            string first=Path.Combine(directory,"duplicate-source");
            string second=Path.Combine(directory,"duplicate-copy");
            foreach(string root in new[]{first,second}){
                string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
                File.Copy(image,Path.Combine(folder,"test.png"));
            }
            foreach(bool sampled in new[]{false,true})using(var engine=ReviewerEngine.Open(Path.Combine(directory,sampled ? "duplicate-candidates.sqlite" : "duplicates.sqlite"),true)){
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                var policy=sampled ? new FingerprintPolicy {Mode="Sampled",ThresholdBytes=0} : FingerprintPolicy.Full();
                engine.StartScan(new ScanOptions {RootPath=first,Extensions=new[]{"png"},WorkerCount=1,FingerprintPolicy=policy});
                Wait(()=>!engine.Status.RunningScan);
                var original=Result(engine,engine.RequestPage(new PageRequest {Generation=1})).Page.Rows.Single();
                engine.StartScan(new ScanOptions {RootPath=second,Extensions=new[]{"png"},WorkerCount=1,FingerprintPolicy=policy});
                Wait(()=>!engine.Status.RunningScan);
                Check(Result(engine,engine.RequestPage(new PageRequest {Generation=2})).Page.Total==2,
                    "Listing exact copies must preserve separate review cards.");
                var page=Result(engine,engine.RequestLocations(original.GroupId,"",1)).Locations;
                Check(page.HasMore,"A second root with the same fingerprint must appear in Details.");
                var next=Result(engine,engine.RequestLocations(original.GroupId,page.NextAfter,1)).Locations;
                var locations=page.Rows.Concat(next.Rows).ToArray();
                Check(!next.HasMore && locations.Single(x=>!x.OtherCard).RootPath==first &&
                    locations.Single(x=>x.OtherCard).RootPath==second,"Each exact match must show its own source root.");
                Check(locations.Single(x=>x.OtherCard).CardLabel=="Exact match",
                    "Old sampling requests must still produce full-file exact matches.");
            }
        }
        static void CheckMixedFingerprints(string directory,string image) {
            string root=Path.Combine(directory,"mixed-fingerprints");
            string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
            string original=Path.Combine(folder,"original.png"),copy=Path.Combine(folder,"copy.png");
            byte[] bytes=File.ReadAllBytes(image);
            Array.Resize(ref bytes,bytes.Length+1);
            File.WriteAllBytes(original,bytes);
            using(var engine=ReviewerEngine.Open(Path.Combine(directory,"mixed-fingerprints.sqlite"),true)){
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                var options=new ScanOptions {RootPath=root,Extensions=new[]{"png"},WorkerCount=1,FingerprintPolicy=FingerprintPolicy.Full()};
                engine.StartScan(options);Wait(()=>!engine.Status.RunningScan);
                var first=Result(engine,engine.RequestPage(new PageRequest {Generation=1})).Page.Rows.Single();
                File.Copy(original,copy);
                bytes[bytes.Length-1]=1;
                File.WriteAllBytes(Path.Combine(folder,"different.png"),bytes);
                options.FingerprintPolicy=new FingerprintPolicy {Mode="Sampled",ThresholdBytes=0};
                engine.StartScan(options);Wait(()=>!engine.Status.RunningScan);
                var rows=Result(engine,engine.RequestPage(new PageRequest {Generation=2})).Page.Rows;
                Check(rows.Count==2 && rows.Single(row=>row.GroupId!=first.GroupId).Algorithm=="sha256",
                    "A same-size file with a different full digest must keep its own identity.");
                var locations=Result(engine,engine.RequestLocations(first.GroupId,"")).Locations.Rows;
                Check(locations.Count==2 && locations.Any(row=>row.FullPath==copy),
                    "A Quick rescan must record a full-verified copy on the existing full fingerprint.");
                long unrelated=rows.Single(r=>r.GroupId!=first.GroupId).GroupId;
                Result(engine,engine.RequestReview(new ReviewRequest {
                    OperationId=Guid.NewGuid().ToString("N"),Status="COMPLIANT",Notes="reviewed full identity",GroupIds=new[]{first.GroupId}
                }));
                string reviewedCopy=Path.Combine(folder,"reviewed-copy.png");
                File.Copy(original,reviewedCopy);
                engine.StartScan(options);Wait(()=>!engine.Status.RunningScan);
                var unreviewed=Result(engine,engine.RequestPage(new PageRequest {Generation=3})).Page.Rows;
                Check(unreviewed.Count==1 && unreviewed[0].GroupId==unrelated && unreviewed[0].Algorithm=="sha256",
                    "A same-size reviewed identity changed an unrelated file's full identity.");
                var reviewed=Result(engine,engine.RequestPage(new PageRequest {Generation=4,Bucket="COMPLIANT"})).Page.Rows.Single();
                Check(reviewed.GroupId==first.GroupId && reviewed.LocationCount==3 &&
                    Result(engine,engine.RequestLocations(first.GroupId,"")).Locations.Rows.Any(row=>row.FullPath==reviewedCopy),
                    "An exact copy must reuse the reviewed full card and add its location.");
            }
        }
        static void CheckRenamed(string directory,string image) {
            string root=Directory.CreateDirectory(Path.Combine(directory,"renamed-root")).FullName;
            string folder=Directory.CreateDirectory(Path.Combine(root,"example.user")).FullName;
            string renamed=Path.Combine(root,"renamed.dat");File.Copy(image,renamed);
            File.WriteAllText(Path.Combine(folder,"document.dat"),"<?xml version=\"1.0\"?><document>ordinary content</document>");
            using(var engine=ReviewerEngine.Open(Path.Combine(directory,"renamed.sqlite"),true)){
                Wait(()=>engine.Ready || engine.Faulted);Check(engine.Ready,engine.Status.Error);
                var options=new ScanOptions {RootPath=root,Extensions=new[]{"png"},WorkerCount=1,SniffRenamed=false};
                engine.StartScan(options);Wait(()=>!engine.Status.RunningScan);
                Check(Result(engine,engine.RequestPage(new PageRequest{Generation=1})).Page.Total==0,"Opting out must skip a root file with an unknown extension.");
                options=new ScanOptions {RootPath=root,Extensions=new[]{"png"},WorkerCount=1,FingerprintPolicy=FingerprintPolicy.Full()};
                engine.StartScan(options);Wait(()=>!engine.Status.RunningScan);
                var row=Result(engine,engine.RequestPage(new PageRequest{Generation=2,Type="png"})).Page.Rows.Single();
                Check(row.Name=="renamed.dat" && row.SourcePath==renamed && row.Extension=="png" && row.Algorithm=="sha256" && row.PreviewBytes!=null,"Renamed media did not retain its true filename, media family, preview and full hash.");
                Check(Result(engine,engine.RequestPage(new PageRequest{Generation=3,Filter="renamed"})).Page.Total==1,"The renamed smart filter did not isolate the mismatched extension item.");
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
            Console.WriteLine("Final HTML database detected and imported into a separate, valid database.");return 0;
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
