using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaReviewer {
    // One player belongs to one inspection window. Source resolution stays on the engine worker.
    sealed class VideoPlaybackControls:Border,IDisposable {
        readonly Grid viewport;
        readonly Func<Action<string,string>,Action> resolve;
        readonly Button play,stop,back,forward,mute;
        readonly Slider seek,volume;
        readonly TextBlock time,status;
        readonly DispatcherTimer timer;
        MediaElement media;
        Action cancelResolution;
        bool disposed,opened,paused,dragging,updating,ended,preparing,playRequested;
        long generation;
        double duration;

        internal VideoPlaybackControls(Grid viewport,Func<Action<string,string>,Action> resolve) {
            this.viewport=viewport;this.resolve=resolve;
            Margin=new Thickness(0,6,0,0);Padding=new Thickness(6);CornerRadius=new CornerRadius(7);
            SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(BorderBrushProperty,"BorderBrush");BorderThickness=new Thickness(1);
            var panel=new StackPanel();Child=panel;
            status=new TextBlock {Text="Video · press Play to open the source. Windows codec support is required.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,0,3,3)};
            status.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");panel.Children.Add(status);
            var timeline=new DockPanel();panel.Children.Add(timeline);
            time=new TextBlock {Text="0:00 / --:--",MinWidth=110,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,3,0)};
            DockPanel.SetDock(time,Dock.Right);timeline.Children.Add(time);
            seek=new Slider {Minimum=0,Maximum=1,IsEnabled=false,SmallChange=1,LargeChange=10,Margin=new Thickness(6),VerticalAlignment=VerticalAlignment.Center,ToolTip="Seek through video; use Left/Right for one second, Page Up/Down for ten seconds"};
            AutomationProperties.SetName(seek,"Video position in seconds");timeline.Children.Add(seek);
            var buttons=new WrapPanel();panel.Children.Add(buttons);
            play=MakeButton("Play",Toggle,"Play or pause video (Space when the player is focused)");
            stop=MakeButton("Stop",()=>{Reset();status.Text="Stopped · showing saved poster.";},"Stop playback and release the source file");
            back=MakeButton("−10 s",()=>SeekTo(seek.Value-10),"Skip back ten seconds");
            forward=MakeButton("+10 s",()=>SeekTo(seek.Value+10),"Skip forward ten seconds");
            mute=MakeButton("Mute",()=>{if(media!=null)media.IsMuted=!media.IsMuted;else muted=!muted;UpdateMute();},"Mute or restore video audio");
            foreach(var button in new[]{play,stop,back,forward,mute})buttons.Children.Add(button);
            buttons.Children.Add(new TextBlock {Text="Volume",Margin=new Thickness(8,0,3,0),VerticalAlignment=VerticalAlignment.Center});
            volume=new Slider {Minimum=0,Maximum=1,Value=0.5,SmallChange=0.05,LargeChange=0.1,Width=100,Margin=new Thickness(5),VerticalAlignment=VerticalAlignment.Center,ToolTip="Playback volume"};
            AutomationProperties.SetName(volume,"Video volume");buttons.Children.Add(volume);
            volume.ValueChanged+=(s,e)=>{if(media!=null)media.Volume=volume.Value;};
            seek.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((s,e)=>dragging=true));
            seek.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((s,e)=>{dragging=false;SeekTo(seek.Value);}));
            seek.ValueChanged+=(s,e)=>{if(!updating){if(!dragging)SeekTo(seek.Value);else UpdateTime(seek.Value);}};
            // Space on a slider belongs to the player; arrows retain their normal slider behavior.
            PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Space && !e.IsRepeat && !(e.OriginalSource is Button) && Keyboard.Modifiers==ModifierKeys.None){e.Handled=true;Toggle();}};
            timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
            timer.Tick+=Tick;
            SetEnabled(false);
        }

        bool muted;
        Button MakeButton(string label,Action action,string hint) {
            var button=new Button {Content=label,ToolTip=hint,MinWidth=50};
            button.Click+=(s,e)=>Safe(action);return button;
        }
        void Safe(Action action) {if(disposed)return;try{action();}catch(Exception error){Fail("Playback unavailable: "+error.GetBaseException().Message);}}
        void Toggle() {
            if(disposed)return;
            if(opened){Safe(()=>{if(paused || ended){if(ended){media.Position=TimeSpan.Zero;ended=false;}media.Play();paused=false;}else{media.Pause();paused=true;}play.Content=paused ? "Play" : "Pause";status.Text=paused ? "Paused" : "Playing";});return;}
            if(preparing){playRequested=true;play.IsEnabled=false;status.Text="Preparing video for playback…";return;}
            BeginPrepare(true);
        }
        internal void Prepare() {if(!disposed && !opened && !preparing)BeginPrepare(false);}
        void BeginPrepare(bool startPlaying) {
            playRequested=startPlaying;preparing=true;
            long request=++generation;play.IsEnabled=!startPlaying;stop.IsEnabled=true;status.Text="Checking video source…";
            Safe(()=>{
                bool resolved=false;
                Action cancel=resolve((path,error)=>{
                    resolved=true;
                    if(disposed || request!=generation)return;
                    cancelResolution=null;
                    if(error!=null){Fail(error);return;}
                    Safe(()=>Open(path));
                });
                if(!resolved && request==generation)cancelResolution=cancel;
            });
        }
        void Open(string path) {
            var player=new MediaElement {LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Uniform,Volume=volume.Value,IsMuted=muted,Opacity=0};
            media=player;viewport.Children.Add(player);status.Text="Opening video…";
            player.MediaOpened+=(s,e)=>{
                if(disposed || !ReferenceEquals(media,player))return;
                Safe(()=>{
                    preparing=false;opened=true;player.Opacity=1;duration=player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0;
                    if(Double.IsNaN(duration) || Double.IsInfinity(duration) || duration<0)duration=0;
                    seek.Maximum=Math.Max(1,duration);SetEnabled(true);paused=!playRequested;
                    if(playRequested)player.Play();else player.Pause();
                    play.Content=paused ? "Play" : "Pause";status.Text=paused ? "Ready · press Play" : "Playing";timer.Start();UpdateTime(0);
                });
            };
            player.MediaFailed+=(s,e)=>{if(ReferenceEquals(media,player))Fail("Windows could not play this file. The saved poster is still available. "+(e.ErrorException==null ? "" : e.ErrorException.GetBaseException().Message));};
            player.MediaEnded+=(s,e)=>{if(!ReferenceEquals(media,player))return;ended=true;paused=true;play.Content="Replay";status.Text="Playback ended";UpdateTime(duration);};
            // Pause prerolls the Windows media pipeline without starting audio or advancing the clip.
            player.Source=new Uri(path,UriKind.Absolute);player.Pause();
        }
        void SetEnabled(bool ready) {play.IsEnabled=!disposed;stop.IsEnabled=ready;back.IsEnabled=forward.IsEnabled=seek.IsEnabled=ready && duration>0;}
        void Tick(object sender,EventArgs args) {if(opened && !dragging && !ended)Safe(()=>{updating=true;try{seek.Value=Math.Min(duration,media.Position.TotalSeconds);}finally{updating=false;}UpdateTime(media.Position.TotalSeconds);});}
        void SeekTo(double seconds) {
            if(!opened || duration<=0 || updating)return;
            Safe(()=>{seconds=Math.Max(0,Math.Min(duration,seconds));media.Position=TimeSpan.FromSeconds(seconds);ended=false;updating=true;try{seek.Value=seconds;}finally{updating=false;}UpdateTime(seconds);});
        }
        static string Clock(double seconds) {var span=TimeSpan.FromSeconds(Math.Max(0,seconds));return span.TotalHours>=1 ? ((int)span.TotalHours)+":"+span.Minutes.ToString("00")+":"+span.Seconds.ToString("00") : ((int)span.TotalMinutes)+":"+span.Seconds.ToString("00");}
        void UpdateTime(double seconds) {time.Text=Clock(seconds)+" / "+(duration>0 ? Clock(duration) : "--:--");}
        void UpdateMute() {if(media!=null)muted=media.IsMuted;mute.Content=muted ? "Unmute" : "Mute";AutomationProperties.SetName(mute,muted ? "Unmute video" : "Mute video");}
        void Fail(string message) {Reset();status.Text=message;}
        void Reset() {
            ++generation;timer.Stop();opened=paused=ended=dragging=preparing=playRequested=false;duration=0;
            var cancel=cancelResolution;cancelResolution=null;
            if(cancel!=null)cancel();
            var old=media;media=null;
            if(old!=null){muted=old.IsMuted;try{old.Close();old.Source=null;}finally{viewport.Children.Remove(old);}}
            updating=true;try{seek.Value=0;}finally{updating=false;}
            time.Text="0:00 / --:--";play.Content="Play";SetEnabled(false);UpdateMute();
        }
        public void Dispose() {if(disposed)return;disposed=true;Reset();timer.Tick-=Tick;}
    }
}
