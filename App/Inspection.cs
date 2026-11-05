using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        void Details(MediaRow row) {
            var panel=new DockPanel {Margin=new Thickness(16)};var window=Dialog("Item details and notes",panel,825,660);
            var top=new StackPanel();Dock(panel,top,System.Windows.Controls.Dock.Top);
            var summary=Label("Loading details…");top.Children.Add(summary);
            var reviewer=Editor("",128);var notes=Editor("");notes.AcceptsReturn=true;notes.TextWrapping=TextWrapping.Wrap;
            notes.Height=112;notes.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
            top.Children.Add(Label("Reviewer"));top.Children.Add(reviewer);top.Children.Add(Label("Notes"));top.Children.Add(notes);
            var feedback=Label("");var actions=Bar();
            Dock(panel,actions,System.Windows.Controls.Dock.Bottom);Dock(panel,feedback,System.Windows.Controls.Dock.Bottom);
            var locations=new ListView();
            locations.View=new GridView {Columns={new GridViewColumn {Header="Recorded relative path",Width=480,DisplayMemberBinding=new System.Windows.Data.Binding("Path")},
                new GridViewColumn {Header="Bytes",Width=95,DisplayMemberBinding=new System.Windows.Data.Binding("Size")},
                new GridViewColumn {Header="Historical",Width=75,DisplayMemberBinding=new System.Windows.Data.Binding("Historical")}}};
            panel.Children.Add(locations);
            bool live=true;window.Closed+=(s,e)=>live=false;
            InspectorData current=null;string cursor="",nextCursor="";Button save=null,next=null,previous=null;
            var history=new Stack<string>();var generation=new ViewGeneration();
            bool saving=false;
            Action loadLocations=()=>{
                next.IsEnabled=previous.IsEnabled=false;long version=generation.Advance();
                Queue(engine.RequestLocations(row.GroupId,cursor),r=>{
                    if(!live || !generation.Accept(version))return;
                    previous.IsEnabled=history.Count>0;
                    if(!r.Success){feedback.Text=r.Error;return;}
                    locations.ItemsSource=r.Locations.Rows;nextCursor=r.Locations.NextAfter;next.IsEnabled=r.Locations.HasMore;
                });
            };
            Func<bool> dirty=()=>current!=null && (reviewer.Text!=current.Reviewer || notes.Text!=current.Notes);
            var detailsVersion=new ViewGeneration();
            Action<bool> load=keepDraft=>{
                if(saving)return;
                save.IsEnabled=false;long version=detailsVersion.Advance();
                Queue(engine.RequestInspector(row.GroupId,0),r=>{
                    if(!live || !detailsVersion.Accept(version))return;
                    if(!r.Success){feedback.Text=r.Error;save.IsEnabled=current!=null && !current.TextReadOnly;return;}
                    current=r.Inspector;
                    summary.Text="Group "+current.GroupId+" · Publication "+current.Sequence+" · "+current.Status+" · "+current.Size.ToString("N0")+" bytes\n"+
                        "Root: "+current.RootPath+"\n"+current.Algorithm+" v"+current.AlgorithmVersion+": "+current.Digest+"\nPreview: "+current.PreviewState;
                    if(!keepDraft){reviewer.Text=current.Reviewer;notes.Text=current.Notes;}
                    else feedback.Text="Saved review reloaded. Your draft is still in the editor.";
                    reviewer.IsReadOnly=notes.IsReadOnly=current.TextReadOnly;
                    save.IsEnabled=!current.TextReadOnly;cursor="";history.Clear();loadLocations();
                });
            };
            save=ActionButton("Save notes",()=>Guard(()=>{
                if(current==null || saving)return;
                var request=new NotesRequest {GroupId=current.GroupId,ContentId=current.ContentId,ExpectedRevision=current.ReviewRevision,Reviewer=reviewer.Text,Notes=notes.Text,OperationId=Id()};
                Queue(engine.RequestNotes(request),r=>{if(!live)return;saving=false;reviewer.IsReadOnly=notes.IsReadOnly=current.TextReadOnly;feedback.Text=r.Success ? "Notes saved." : r.Error;save.IsEnabled=true;if(r.Success)load(false);});
                saving=true;save.IsEnabled=false;reviewer.IsReadOnly=notes.IsReadOnly=true;feedback.Text="Saving notes…";
            }));
            previous=ActionButton("Previous locations",()=>Guard(()=>{cursor=history.Pop();loadLocations();}));
            next=ActionButton("Next locations",()=>Guard(()=>{history.Push(cursor);cursor=nextCursor;loadLocations();}));
            previous.IsEnabled=next.IsEnabled=save.IsEnabled=false;
            actions.Children.Add(save);actions.Children.Add(ActionButton("Reload, keep draft",()=>Guard(()=>load(true))));
            actions.Children.Add(ActionButton("Discard draft",()=>Guard(()=>{if(!dirty() || MessageBox.Show(window,"Discard the unsaved edits?","Discard draft",MessageBoxButton.YesNo)==MessageBoxResult.Yes)load(false);}))); 
            actions.Children.Add(previous);actions.Children.Add(next);actions.Children.Add(ActionButton("Close",window.Close));
            var neighbors=new WrapPanel();top.Children.Insert(0,neighbors);
            foreach(int direction in new[]{-1,1}){
                int step=direction;
                neighbors.Children.Add(ActionButton(step<0 ? "Previous item" : "Next item",()=>Guard(()=>{
                    if(saving)return;
                    int at=cards.ToList().FindIndex(c=>c.Record.GroupId==row.GroupId),target=at+step;
                    if(at<0 || target<0 || target>=cards.Count)return;
                    if(dirty() && MessageBox.Show(window,"Leave this item and discard its unsaved edits?","Unsaved notes",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                    MediaRow other=cards[target].Record;window.Close();Details(other);
                })));
            }
            TrackDatabaseWindow(window);window.Show();load(false);
        }

        void Inspect(MediaRow row) {
            if(inspector!=null)inspector.Close();
            ResetQuickPreview();
            if(row.Status=="EVIDENCE" || row.OriginalRetained && row.LocationCount==0){Original(row);return;}
            var panel=new DockPanel {Margin=new Thickness(16)};
            var window=Dialog("Inspect · "+row.Name,panel,960,735);inspector=window;
            var top=new StackPanel();Dock(panel,top,System.Windows.Controls.Dock.Top);
            var actions=new WrapPanel();top.Children.Add(actions);
            var reviewActions=new WrapPanel();top.Children.Add(reviewActions);
            var reviewer=Editor(row.Reviewer=="" ? Box("ReviewerBox").Text : row.Reviewer,128);reviewer.Width=135;
            var notes=Editor(row.Notes);notes.Width=220;
            reviewActions.Children.Add(Label("Reviewer"));reviewActions.Children.Add(reviewer);reviewActions.Children.Add(Label("Notes"));reviewActions.Children.Add(notes);
            var feedback=Label("Loading source preview…");feedback.MinHeight=36;top.Children.Add(feedback);
            var image=new Image {Stretch=Stretch.Uniform};
            var preview=new ScrollViewer {Content=image,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
            preview.SetResourceReference(Control.BackgroundProperty,"InsetBrush");
            var footer=Bar();Dock(panel,footer,System.Windows.Controls.Dock.Bottom);footer.Children.Add(ActionButton("Close",window.Close));
            var viewport=new Grid {Focusable=true};viewport.Children.Add(preview);panel.Children.Add(viewport);
            System.Windows.Automation.AutomationProperties.SetName(viewport,"Media preview. Left and Right browse. Up marks Compliant. Down marks Non-compliant.");
            viewport.PreviewMouseLeftButtonDown+=(s,e)=>viewport.Focus();
            bool live=true;int side=0,sourceWidth=0,sourceHeight=0;
            string layout="Fit";double zoom=1;
            int position=positionOf(row);
            CardItem current=position>=0 ? cards[position] : new CardItem(row);
            Action<int> navigate=direction=>{
                if(!Ready || current.IsPending)return;
                int index=positionOf(row),next=index+direction;
                if(index>=0 && next>=0 && next<cards.Count)Inspect(cards[next].Record);
            };
            Action<string> classify=key=>{
                if(!Ready || current.IsPending)return;
                if(current.Record.ContentId!=row.ContentId || current.Record.ReviewRevision!=row.ReviewRevision){
                    feedback.Text="This item changed. Reopen it to review the latest version.";
                    return;
                }
                long generation=view.Current;
                int index=positionOf(row);
                CardItem next=(key=="COMPLIANT" || key=="NON_COMPLIANT") && index>=0 && index+1<cards.Count ? cards[index+1] : null;
                Review(key,new[]{current},r=>{
                    if(!live)return;
                    if(!r.Success){feedback.Text=r.Error;return;}
                    if(Ready && view.Accept(generation) && next!=null && cards.Contains(next))Inspect(next.Record);
                    else window.Close();
                },reviewer.Text,notes.Text,()=>live && Ready && view.Accept(generation));
            };
            for(int i=0;i<BucketKeys.Length;i++){
                string key=BucketKeys[i];
                reviewActions.Children.Add(ActionButton(BucketNames[i],()=>Guard(()=>classify(key))));
            }

            if(position>=0)image.Source=cards[position].Image;
            bool video=Formats.IsVideo(row.Extension);
            if(video)feedback.Text="Video inspection · preparing playback. Retry loads a fresh source poster.";
            Action<int> request=desired=>{
                long generation=inspection.Advance();side=desired;feedback.Text=image.Source==null ? "Loading source preview…" : "Showing cached preview while loading source detail…";
                if(!loader.QueueInspect(row,generation,desired))feedback.Text="Source preview is unavailable during shutdown.";
            };
            Action apply=()=>{
                image.Width=image.Height=Double.NaN;
                preview.HorizontalScrollBarVisibility=preview.VerticalScrollBarVisibility=layout=="Zoom" ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                image.Stretch=layout=="Fill" ? Stretch.UniformToFill : Stretch.Uniform;
                if(layout!="Zoom" || sourceWidth<1)return;
                image.Width=Math.Max(1,sourceWidth*zoom);image.Height=Math.Max(1,sourceHeight*zoom);
            };
            var refine=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(180)};
            refine.Tick+=(s,e)=>{refine.Stop();Guard(()=>{
                if(live && !video && sourceWidth>0){var plan=InspectionPlan.Create(sourceWidth,sourceHeight,Math.Max(1,preview.ViewportWidth),Math.Max(1,preview.ViewportHeight),layout,zoom);if(plan.DecodeSide>side)request(plan.DecodeSide);}
            });};
            Action schedule=()=>{refine.Stop();refine.Start();};
            Action<string,double> choose=(mode,scale)=>{
                layout=mode;zoom=Math.Min(8,Math.Max(0.05,scale));apply();schedule();
            };
            preview.SizeChanged+=(s,e)=>schedule();
            preview.PreviewMouseWheel+=(s,e)=>{
                if(sourceWidth==0 || sourceHeight==0)return;
                Point point=e.GetPosition(preview);
                var plan=InspectionPlan.Create(sourceWidth,sourceHeight,Math.Max(1,preview.ViewportWidth),Math.Max(1,preview.ViewportHeight),layout,zoom);
                double x=(preview.HorizontalOffset+point.X-Math.Max(0,(preview.ViewportWidth-plan.DisplayWidth)/2))/plan.Scale;
                double y=(preview.VerticalOffset+point.Y-Math.Max(0,(preview.ViewportHeight-plan.DisplayHeight)/2))/plan.Scale;
                choose("Zoom",plan.Scale*(e.Delta>0 ? 1.25 : 0.8));
                preview.UpdateLayout();preview.ScrollToHorizontalOffset(x*zoom-point.X);preview.ScrollToVerticalOffset(y*zoom-point.Y);e.Handled=true;
            };
            Point? drag=null;double dragX=0,dragY=0;
            image.MouseLeftButtonDown+=(s,e)=>{if(layout!="Zoom")return;drag=e.GetPosition(preview);dragX=preview.HorizontalOffset;dragY=preview.VerticalOffset;image.CaptureMouse();image.Cursor=Cursors.Hand;e.Handled=true;};
            image.MouseMove+=(s,e)=>{if(!drag.HasValue || e.LeftButton!=MouseButtonState.Pressed)return;Vector move=e.GetPosition(preview)-drag.Value;preview.ScrollToHorizontalOffset(dragX-move.X);preview.ScrollToVerticalOffset(dragY-move.Y);};
            image.MouseLeftButtonUp+=(s,e)=>image.ReleaseMouseCapture();
            image.LostMouseCapture+=(s,e)=>{drag=null;image.Cursor=null;};
            actions.Children.Add(ActionButton("Fit",()=>Guard(()=>choose("Fit",1))));
            actions.Children.Add(ActionButton("Fill",()=>Guard(()=>choose("Fill",1))));
            actions.Children.Add(ActionButton("−",()=>Guard(()=>choose("Zoom",zoom/1.25))));
            actions.Children.Add(ActionButton("+",()=>Guard(()=>choose("Zoom",zoom*1.25))));
            actions.Children.Add(ActionButton("100%",()=>Guard(()=>choose("Zoom",1))));
            actions.Children.Add(ActionButton("Retry",()=>Guard(()=>request(Math.Max(256,side)))));
            actions.Children.Add(ActionButton("Details / notes",()=>Guard(()=>Details(row))));
            VideoPlaybackControls player=null;
            if(video) {
                player=CreateVideoPlayer(viewport,row);
                DockPanel.SetDock(player,System.Windows.Controls.Dock.Bottom);panel.Children.Insert(panel.Children.IndexOf(viewport),player);
                window.Closed+=(s,e)=>player.Dispose();
                window.Loaded+=(s,e)=>player.Prepare();
            }
            if(position>0)actions.Children.Add(ActionButton("← Previous item",()=>Guard(()=>navigate(-1))));
            if(position>=0 && position+1<cards.Count)actions.Children.Add(ActionButton("Next item →",()=>Guard(()=>navigate(1))));
            top.Children.Insert(2,Label("Arrow keys: ← Previous · → Next · ↑ Compliant · ↓ Non-compliant"));
            window.PreviewKeyDown+=(s,e)=>{
                if(!Ready || Keyboard.Modifiers!=ModifierKeys.None || reviewer.IsKeyboardFocusWithin ||
                    notes.IsKeyboardFocusWithin || player!=null && player.IsKeyboardFocusWithin)return;
                Key key=e.Key;
                string destination=QuickReviewKey(key);
                if(key!=Key.Left && key!=Key.Right && destination==null)return;
                e.Handled=true;
                if(e.IsRepeat)return;
                if(destination!=null)Guard(()=>classify(destination));
                else Guard(()=>navigate(key==Key.Left ? -1 : 1));
            };
            showInspection=result=>{
                if(result.Image!=null){image.Source=result.Image;sourceWidth=result.SourceWidth;sourceHeight=result.SourceHeight;apply();feedback.Text=sourceWidth+" × "+sourceHeight+" · "+row.RelativePath;}
                else feedback.Text=result.Error??"Preview unavailable.";
            };
            window.Closed+=(s,e)=>{live=false;refine.Stop();loader.CancelInspect(inspection.Advance());showInspection=null;inspector=null;image.Source=null;};
            if(!video)window.Loaded+=(s,e)=>Guard(()=>request(Math.Min(InspectionPlan.MaxSide,Math.Max(256,(int)Math.Ceiling(Math.Max(preview.ActualWidth,preview.ActualHeight))))));
            window.Loaded+=(s,e)=>viewport.Focus();
            window.Show();
        }

        VideoPlaybackControls CreateVideoPlayer(Grid viewport,MediaRow row) {
            return new VideoPlaybackControls(viewport,complete=>{
                var sourceEngine=engine;
                if(!Ready)throw new InvalidOperationException("The catalog is unavailable.");
                string request=sourceEngine.RequestVideoPlayback(row);
                Queue(request,result=>complete(result.Value,result.Success ? null : result.Error));
                return ()=>sourceEngine.CancelOperation(request);
            });
        }
        int positionOf(MediaRow row){return cards.ToList().FindIndex(c=>c.Record.GroupId==row.GroupId && c.Record.ContentId==row.ContentId);}

    }
}
