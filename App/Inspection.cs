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
            var viewport=new Grid();viewport.Children.Add(preview);panel.Children.Add(viewport);
            bool live=true,reviewPending=false;int side=0,sourceWidth=0,sourceHeight=0;
            string layout="Fit";double zoom=1;
            var reviewCard=new CardItem(row);CardItem current=reviewCard;
            var available=BucketKeys.Select((key,index)=>ActionButton(BucketNames[index],()=>Guard(()=>{
                if(!live || reviewPending)return;
                Review(key,new[]{current},r=>{
                    if(!live)return;reviewPending=false;
                    feedback.Text=r.Success ? "Review saved · "+BucketNames[index] : r.Error;
                    if(r.Success)window.Close();
                },reviewer.Text,notes.Text);
                reviewPending=current.IsPending;
            }))).ToArray();
            foreach(Button button in available)reviewActions.Children.Add(button);

            int position=positionOf(row);
            if(position>=0)image.Source=cards[position].Image;
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
                if(live && sourceWidth>0){var plan=InspectionPlan.Create(sourceWidth,sourceHeight,Math.Max(1,preview.ViewportWidth),Math.Max(1,preview.ViewportHeight),layout,zoom);if(plan.DecodeSide>side)request(plan.DecodeSide);}
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
            if(Formats.IsVideo(row.Extension))AttachVideoControls(actions,viewport,window,row,feedback);
            if(position>=0){current=cards[position];if(position>0)actions.Children.Add(ActionButton("Previous item",()=>Guard(()=>Inspect(cards[position-1].Record))));
                if(position+1<cards.Count)actions.Children.Add(ActionButton("Next item",()=>Guard(()=>Inspect(cards[position+1].Record))));}
            showInspection=result=>{
                if(result.Image!=null){image.Source=result.Image;sourceWidth=result.SourceWidth;sourceHeight=result.SourceHeight;apply();feedback.Text=sourceWidth+" × "+sourceHeight+" · "+row.RelativePath;}
                else feedback.Text=result.Error??"Preview unavailable.";
            };
            window.Closed+=(s,e)=>{live=false;refine.Stop();loader.CancelInspect(inspection.Advance());showInspection=null;inspector=null;image.Source=null;};
            window.Loaded+=(s,e)=>Guard(()=>request(Math.Min(InspectionPlan.MaxSide,Math.Max(256,(int)Math.Ceiling(Math.Max(preview.ActualWidth,preview.ActualHeight))))));
            window.Show();
        }
        int positionOf(MediaRow row){return cards.ToList().FindIndex(c=>c.Record.GroupId==row.GroupId && c.Record.ContentId==row.ContentId);}

        void AttachVideoControls(WrapPanel actions,Grid viewport,Window window,MediaRow row,TextBlock feedback) {
            var media=new MediaElement {LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed};
            viewport.Children.Add(media);
            bool live=true,paused=false;var generation=new ViewGeneration();Button play=null,stop=null;
            Action close=()=>{generation.Advance();media.Close();media.Source=null;media.Visibility=Visibility.Collapsed;paused=false;play.Content="Play video";play.IsEnabled=live;stop.IsEnabled=false;};
            play=ActionButton("Play video",()=>Guard(()=>{
                if(media.Source!=null){if(paused){media.Play();play.Content="Pause";}else{media.Pause();play.Content="Resume video";}paused=!paused;return;}
                long version=generation.Advance();
                Work(engine.RequestVideoPlayback(row),"Check video source",r=>{
                    if(!live || !generation.Accept(version))return;
                    if(!r.Success){close();feedback.Text=r.Error;return;}
                    media.Source=new Uri(r.Value,UriKind.Absolute);media.Visibility=Visibility.Visible;media.Play();play.Content="Pause";play.IsEnabled=true;
                },true);
                play.IsEnabled=false;stop.IsEnabled=true;feedback.Text="Checking the selected video source…";
            }));
            stop=ActionButton("Stop video",()=>{close();feedback.Text="Showing saved poster.";});stop.IsEnabled=false;
            media.MediaFailed+=(s,e)=>{if(media.Source!=null){close();feedback.Text="Windows could not play this encoding. The preview remains available.";}};
            media.MediaEnded+=(s,e)=>{if(media.Source!=null){close();feedback.Text="Playback ended. Showing preview.";}};
            window.Closed+=(s,e)=>{live=false;close();};
            actions.Children.Add(play);actions.Children.Add(stop);
        }
    }
}
