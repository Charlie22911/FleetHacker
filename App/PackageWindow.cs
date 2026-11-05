using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        internal void OpenPackage(string path) {
            var body=new DockPanel {Margin=new Thickness(16)};
            var window=Dialog("Protected Evidence package",body,865,650);
            var statusLine=Label("Opening protected package…");Dock(body,statusLine,System.Windows.Controls.Dock.Top);
            var footer=Bar();Dock(body,footer,System.Windows.Controls.Dock.Bottom);
            var list=new ListBox {Width=210,Margin=new Thickness(0,0,10,0)};Dock(body,list,System.Windows.Controls.Dock.Left);
            var display=new DockPanel();body.Children.Add(display);
            var image=new Image {Height=220,Stretch=Stretch.Uniform};Dock(display,image,System.Windows.Controls.Dock.Top);
            var details=Editor("");details.IsReadOnly=true;details.TextWrapping=TextWrapping.Wrap;details.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;display.Children.Add(details);
            using(var session=EvidencePackageSession.Open(path)) {
                bool closingPackage=false,warningShown=false;
                Action clear=()=>{details.Clear();image.Source=null;};
                Action<Action> submit=action=>{try{action();}catch(Exception error){statusLine.Text=error.Message;}};
                var unlock=ActionButton("Unlock",()=>submit(()=>{byte[] password=Password("Unlock protected package","Enter the originals password used to protect this package.",false);if(password!=null){list.ItemsSource=null;clear();session.RequestUnlock(password);}}));
                var relock=ActionButton("Lock",()=>submit(()=>{clear();list.ItemsSource=null;session.RequestLock();}));
                var cancel=ActionButton("Cancel task",()=>{clear();session.Cancel();});
                var export=ActionButton("Export original…",()=>submit(()=>{
                    var chosen=list.SelectedItem as PackageItem;if(chosen==null)return;
                    string destination=SaveFile("All files (*.*)|*.*","Evidence-original-"+chosen.Number);
                    if(destination!=null)session.RequestExport(chosen.Number-1,destination);
                }));
                list.SelectionChanged+=(s,e)=>submit(()=>{var chosen=list.SelectedItem as PackageItem;clear();if(chosen!=null && session.Unlocked)session.RequestInspect(chosen.Number-1);});
                foreach(Button b in new[]{unlock,relock,cancel,export,ActionButton("Close",window.Close)})footer.Children.Add(b);
                unlock.IsEnabled=relock.IsEnabled=cancel.IsEnabled=export.IsEnabled=false;
                window.Closing+=(object s,CancelEventArgs e)=>{if(session.Closed)return;e.Cancel=true;closingPackage=true;clear();list.ItemsSource=null;session.CloseAsync();};
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
                timer.Tick+=(s,e)=>{
                    if(closingPackage && session.Closed){
                        timer.Stop();
                        if(!warningShown && !String.IsNullOrEmpty(session.OutputWarning)){warningShown=true;MessageBox.Show(window,session.OutputWarning,"Check incomplete original output",MessageBoxButton.OK,MessageBoxImage.Warning);}
                        window.Close();return;
                    }
                    var state=session.Status;
                    statusLine.Text=state.Stage;
                    using(var reply=session.TakeResult()) {
                        if(reply!=null && reply.Success && !closingPackage){
                            if(reply.Items!=null)list.ItemsSource=reply.Items;
                            if(reply.Inspection!=null){details.Text=reply.Details;image.Source=reply.Inspection.PreviewImage;}
                        }
                        if(reply!=null && !reply.Success && !closingPackage)statusLine.Text=reply.Error;
                    }
                    bool ready=session.Ready && !closingPackage;
                    unlock.IsEnabled=ready && !session.Unlocked && !session.Busy;
                    relock.IsEnabled=ready && session.Unlocked;
                    export.IsEnabled=ready && session.Unlocked && !session.Busy && list.SelectedItem!=null;
                    cancel.IsEnabled=ready && session.Busy;
                };
                timer.Start();try{window.ShowDialog();}finally{timer.Stop();clear();list.ItemsSource=null;}
            }
        }
    }
}
