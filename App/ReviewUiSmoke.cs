using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MediaReviewer {
    static class ReviewUiSmoke {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,Private).GetValue(instance);}
        static object Call(object instance,string name,params object[] arguments){return instance.GetType().GetMethod(name,Private).Invoke(instance,arguments);}
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void Wait(Func<bool> condition){var clock=Stopwatch.StartNew();while(!condition()){if(clock.Elapsed.TotalSeconds>20)throw new TimeoutException("Review UI check timed out");Pump();Thread.Sleep(5);}}
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {
            var pending=new System.Collections.Generic.Stack<DependencyObject>();var seen=new System.Collections.Generic.HashSet<DependencyObject>();pending.Push(root);
            while(pending.Count>0){var current=pending.Pop();if(!seen.Add(current))continue;if(current is T)yield return (T)current;
                if(current is Visual)for(int i=0;i<VisualTreeHelper.GetChildrenCount(current);i++)pending.Push(VisualTreeHelper.GetChild(current,i));
                foreach(var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())pending.Push(child);
            }
        }
        static void Save(Window window,string path,double scale){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*scale),(int)Math.Ceiling(window.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
        internal static int Run(string directory){
            Directory.CreateDirectory(directory);
            var app=Application.Current??new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                Field<DispatcherTimer>(controller,"timer").Stop();
                var owner=controller.Window;owner.Show();
                var row=new MediaRow {GroupId=101,ContentId=101,Name="Generated review fixture.png",Extension="png",Reviewer="Generated reviewer",Notes="",Status="TO_REVIEW",RootPath=directory,RelativePath="unavailable-fixture.png"};
                var cards=Field<ObservableCollection<CardItem>>(controller,"cards");
                var pixels=new byte[320*200*3];for(int y=0;y<200;y++)for(int x=0;x<320;x++){int offset=(y*320+x)*3;pixels[offset]=(byte)(80+x/3);pixels[offset+1]=(byte)(120+y/3);pixels[offset+2]=170;}
                var poster=BitmapSource.Create(320,200,96,96,PixelFormats.Rgb24,null,pixels,320*3);poster.Freeze();var fixtureCard=new CardItem(row);fixtureCard.SetImage(poster,null);cards.Add(fixtureCard);
                Call(controller,"Inspect",row);Pump();
                var inspection=Field<Window>(controller,"inspector");
                var notes=Descendants<TextBox>(inspection).Single(x=>x.MaxLength==16384);
                var reviewer=Descendants<TextBox>(inspection).Single(x=>x.MaxLength==128);
                var decisions=Descendants<Button>(inspection).Where(x=>new[]{"Compliant","Non-compliant","To be reviewed","Evidence"}.Contains(Convert.ToString(x.Content))).ToArray();
                Check(decisions.Length>=2,"Inspect fixture could not find its decision controls");
                double width=inspection.Width,height=inspection.Height;
                inspection.UpdateLayout();double reviewerHeight=reviewer.ActualHeight;var heights=decisions.Select(x=>x.ActualHeight).ToArray();
                string multiline=String.Join("\n",Enumerable.Repeat("Preserved imported note with a long explanatory line",330));multiline=multiline.Substring(0,16384);
                foreach(string value in new[]{"","Short note",multiline}){
                    notes.Text=value;inspection.UpdateLayout();
                    Save(inspection,Path.Combine(directory,"inspect-before-or-after.png"),1);
                    Check(Math.Abs(reviewer.ActualHeight-reviewerHeight)<=1,"Multiline notes stretched the reviewer field");
                    Check(decisions.Select((x,i)=>Math.Abs(x.ActualHeight-heights[i])<=1).All(x=>x),"Multiline notes stretched decision buttons");
                    Check(inspection.Width==width&&inspection.Height==height,"Notes resized the Inspect window");
                }
                var section=Descendants<Expander>(inspection).SingleOrDefault(x=>Convert.ToString(x.Header).StartsWith("Notes"));
                Check(section!=null&&!section.IsExpanded,"Inspect notes must start collapsed in their own expander");
                foreach(double scale in new[]{1.0,1.5,2.0})foreach(double viewport in new[]{520.0,960.0}){
                    inspection.Width=viewport;inspection.Height=scale==1 ? 735 : 540;
                    section.IsExpanded=true;inspection.UpdateLayout();Pump();
                    Check(notes.ActualHeight<=112.01&&notes.Text==multiline,"The bounded editor must preserve imported text");
                    Check(Math.Abs(reviewer.ActualHeight-reviewerHeight)<=1,"Expanded notes stretched the reviewer field");
                    Check(decisions.Select((x,i)=>Math.Abs(x.ActualHeight-heights[i])<=1).All(x=>x),"Expanded notes stretched decision buttons");
                    var preview=Descendants<Grid>(inspection).Single(x=>x.Name=="InspectMediaStage");
                    Check(preview.ActualHeight>=160,"Inspect must preserve at least 160 DIP of preview");
                    Save(inspection,Path.Combine(directory,"inspect-"+(int)viewport+"-render-"+(int)(scale*100)+".png"),scale);
                    section.IsExpanded=false;
                }
                notes.Text="";inspection.Close();
                row.Notes=multiline;Call(controller,"Inspect",row);Pump();inspection=Field<Window>(controller,"inspector");
                var imported=Descendants<TextBox>(inspection).Single(x=>x.Name=="InspectNotesEditor");Check(imported.Text==multiline,"Opening saved imported notes changed their contents");Check(Descendants<Expander>(inspection).Any(x=>Convert.ToString(x.Header)=="Notes (saved)"&&!x.IsExpanded),"Saved notes did not retain their collapsed saved label");inspection.Close();
                cards.Clear();cards.Add(new CardItem(new MediaRow {GroupId=201,ContentId=201,Extension="mp4",Status="TO_REVIEW"}));
                var display=controller.GetType().GetField("display",Private);display.SetValue(controller,Enum.Parse(display.FieldType,"Quick"));
                Call(controller,"RotateQuick",90);Check(Field<int>(controller,"quickRotation")==0,"Video rotation must be blocked in the shared quick action");
                cards[0].Record.Extension="gif";Call(controller,"RotateQuick",90);Check(Field<int>(controller,"quickRotation")==0,"GIF playback must use the same rotation gate as its buttons");
                cards[0].Record.Extension="png";Call(controller,"RotateQuick",90);Check(Field<int>(controller,"quickRotation")==90,"Still rotation must remain available");Call(controller,"ResetQuickTransform");
                display.SetValue(controller,Enum.Parse(display.FieldType,"List"));Call(controller,"ClearWorkspaceNoteDrafts");
                cards.Clear();var first=new CardItem(new MediaRow {GroupId=301,ContentId=301,Extension="png",Status="TO_REVIEW"});var second=new CardItem(new MediaRow {GroupId=302,ContentId=302,Extension="png",Status="TO_REVIEW"});cards.Add(first);cards.Add(second);
                var draft=(TextBox)owner.FindName("NotesBox");first.IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");draft.Text="First item draft";
                first.IsSelected=false;second.IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");Check(draft.Text=="","An item inherited its predecessor's unsaved note");draft.Text="Second item draft";
                second.IsSelected=false;first.IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");Check(draft.Text=="First item draft","Returning to an item lost its unsaved draft");
                second.IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");Check(draft.Text=="","A bulk selection inherited a single-item draft");draft.Text="Explicit bulk note";Call(controller,"ForgetWorkspaceNoteDrafts",new object[]{new long[]{301,302}});Check(draft.Text=="","Saving a bulk decision retained its submitted note");
                second.IsSelected=false;Call(controller,"SyncWorkspaceNoteDraft");Check(draft.Text=="First item draft","Saving a separate bulk draft erased a single-item draft");
                display.SetValue(controller,Enum.Parse(display.FieldType,"Quick"));Call(controller,"SyncWorkspaceNoteDraft");Check(draft.Text=="First item draft","Quick Review failed to use the selected item's draft");
                controller.GetType().GetField("quickIndex",Private).SetValue(controller,1);Call(controller,"SyncWorkspaceNoteDraft");Check(draft.Text=="Second item draft","Automatic next-item review reused its predecessor's draft");
                Call(controller,"ClearWorkspaceNoteDrafts");Check(draft.Text=="","Switching databases retained a private note draft");
                display.SetValue(controller,Enum.Parse(display.FieldType,"List"));cards.Clear();for(int i=0;i<129;i++)cards.Add(new CardItem(new MediaRow {GroupId=600+i,ContentId=600+i,Extension="png",Status="TO_REVIEW"}));
                for(int i=0;i<128;i++){if(i>0)cards[i-1].IsSelected=false;cards[i].IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");Check(!draft.IsReadOnly,"The draft bound was reached too early");draft.Text="Preserved draft "+i;}
                cards[127].IsSelected=false;cards[128].IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");Check(draft.IsReadOnly&&draft.Text=="","A full draft store accepted an unbounded new draft");
                cards[128].IsSelected=false;cards[0].IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");Check(!draft.IsReadOnly&&draft.Text=="Preserved draft 0","The draft bound discarded a previous unsaved note");draft.Clear();cards[0].IsSelected=false;cards[128].IsSelected=true;Call(controller,"SyncWorkspaceNoteDraft");Check(!draft.IsReadOnly,"Clearing an existing draft failed to release capacity");
                draft.Text="Identity-specific draft";cards[128].Record.ContentId=999;Call(controller,"SyncWorkspaceNoteDraft");Check(draft.Text=="","A changed content identity inherited an old note draft");Call(controller,"ClearWorkspaceNoteDrafts");
                CheckPasswordDialogs(controller);CheckThumbnailGeneration(controller,poster);CheckNativeKeyboardAndDrafts(controller,directory);
                owner.Close();
            }
            CheckPackagePasswordOwnership(directory);app.Shutdown();Console.WriteLine("Review UI checks passed. Render DPI checks are offscreen 100/150/200 percent renders, not a change to Windows display scaling.");return 0;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string kind,string title);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr value,IntPtr other);
        static void AnswerDraftPrompt(Window owner,int answer,Action action){
            bool answered=false;IntPtr handle=new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(s,e)=>{IntPtr prompt=FindWindow("#32770","Unsaved notes");if(prompt!=IntPtr.Zero && GetWindow(prompt,4)==handle){answered=true;timer.Stop();SendMessage(prompt,0x111,new IntPtr(answer),IntPtr.Zero);}};
            timer.Start();try{action();Check(answered,"The unsaved-note confirmation did not appear");}finally{timer.Stop();}
        }
        static void CheckNativeKeyboardAndDrafts(ReviewerWindow controller,string directory){
            if(Native.RuntimeDirectory==null){Console.WriteLine("SKIP native-backed R-key/focus/repeat and unsaved-navigation checks: run --verify-review-ui with the linked Windows executable.");return;}
            controller.Open(Path.Combine(directory,"generated-keyboard.sqlite"),true);var engine=Field<ReviewerEngine>(controller,"engine");Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
            var owner=controller.Window;var cards=Field<ObservableCollection<CardItem>>(controller,"cards");
            var first=new MediaRow {GroupId=501,ContentId=501,Extension="png",Status="TO_REVIEW",Name="First generated item",RootPath=directory,RelativePath="missing-first.png",Notes="",Reviewer="fixture"};
            var second=new MediaRow {GroupId=502,ContentId=502,Extension="png",Status="TO_REVIEW",Name="Second generated item",RootPath=directory,RelativePath="missing-second.png",Notes="Second saved note",Reviewer="fixture"};
            cards.Clear();cards.Add(new CardItem(first));cards.Add(new CardItem(second));Call(controller,"Inspect",first);Pump();
            var inspection=Field<Window>(controller,"inspector");var note=Descendants<TextBox>(inspection).Single(x=>x.Name=="InspectNotesEditor");note.Text="Unsaved first-item draft";
            AnswerDraftPrompt(inspection,7,()=>Call(controller,"Inspect",second));Check(inspection.Title.EndsWith(first.Name)&&note.Text=="Unsaved first-item draft","Declining navigation discarded the first-item draft");
            AnswerDraftPrompt(inspection,6,()=>Call(controller,"Inspect",second));Check(inspection.Title.EndsWith(second.Name),"Accepted navigation did not move to the next item");Check(Descendants<TextBox>(inspection).Single(x=>x.Name=="InspectNotesEditor").Text==second.Notes,"Navigating Inspect reused the predecessor's draft");inspection.Close();
            var display=controller.GetType().GetField("display",Private);display.SetValue(controller,Enum.Parse(display.FieldType,"Quick"));controller.GetType().GetField("quickIndex",Private).SetValue(controller,0);Call(controller,"UpdateControls");owner.Activate();
            var host=(FrameworkElement)owner.FindName("QuickImageHost");host.Focus();Pump();
            Action<bool> rotate=repeat=>{var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(owner),Environment.TickCount,Key.R){RoutedEvent=Keyboard.PreviewKeyDownEvent};if(repeat)typeof(KeyEventArgs).GetMethod("SetRepeat",Private).Invoke(key,new object[]{true});Call(controller,"ReviewKey",owner,key);};
            Call(controller,"ResetQuickTransform");rotate(false);Check(Field<int>(controller,"quickRotation")==90,"The R shortcut failed for a still image");rotate(true);Check(Field<int>(controller,"quickRotation")==90,"A repeated R event rotated a second time");
            ((Expander)owner.FindName("ReviewNotesExpander")).IsExpanded=true;Pump();owner.UpdateLayout();var editor=(TextBox)owner.FindName("ReviewerBox");editor.BringIntoView();editor.Focus();Pump();Check(editor.IsKeyboardFocusWithin,"The keyboard fixture did not focus its editor");rotate(false);Check(Field<int>(controller,"quickRotation")==90,"Typing R inside an editor rotated the media");
            host.Focus();Pump();foreach(string extension in new[]{"mp4","gif"}){first.Extension=extension;Call(controller,"ResetQuickTransform");rotate(false);Check(Field<int>(controller,"quickRotation")==0,"The R shortcut rotated playable "+extension+" media");}
            CheckAcceptedUnlockPresentationFailure(controller,engine);
            engine.CloseAsync();Wait(()=>engine.Closed);engine.Dispose();controller.GetType().GetField("engine",Private).SetValue(controller,null);
        }
        static void CheckAcceptedUnlockPresentationFailure(ReviewerWindow controller,ReviewerEngine engine){
            var owner=controller.Window;object message=owner.FindName("MessageText");bool failed=false;byte[] owned=null;
            lock(Field<object>(engine,"operationGate")){
                owner.UnregisterName("MessageText");
                try{
                    owner.Dispatcher.BeginInvoke(new Action(()=>{var dialog=owner.OwnedWindows.OfType<Window>().Single(w=>w.Title=="Set originals password");foreach(var input in Descendants<PasswordBox>(dialog))input.Password="Generated queued password";Descendants<Button>(dialog).Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}));
                    try{Call(controller,"Unlock",new object[]{null,null});}catch(TargetInvocationException error){failed=error.InnerException is NullReferenceException;}
                    var work=Field<System.Collections.Generic.Dictionary<string,OperationWork>>(engine,"operationStates").Values.Single(x=>x.Progress.Kind=="VaultUnlock");
                    owned=work.Cleanup.Target.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(field=>field.FieldType==typeof(byte[])).Select(field=>(byte[])field.GetValue(work.Cleanup.Target)).Single();
                    Check(failed&&owned.Any(b=>b!=0),"A presentation failure cleared the password already owned by queued unlock work");engine.CancelOperation(work.Progress.RequestId);Check(owned.All(b=>b==0),"Cancelling accepted unlock work retained its password");
                }finally{owner.RegisterName("MessageText",message);controller.GetType().GetField("task",Private).SetValue(controller,null);}
            }
        }
        static void CheckPasswordDialogs(ReviewerWindow controller){
            var read=typeof(ReviewerWindow).GetMethod("ReadPassword",BindingFlags.Static|BindingFlags.NonPublic);
            var input=new PasswordBox();var confirmation=new PasswordBox();input.Password="Generated pässword 🔑";confirmation.Password="Mismatch";
            Check(read.Invoke(null,new object[]{input,confirmation})==null,"Mismatched confirmation was accepted");confirmation.Password="Generated pässword 🔑";
            var bytes=(byte[])read.Invoke(null,new object[]{input,confirmation});try{Check(Encoding.UTF8.GetString(bytes)=="Generated pässword 🔑","Secure-password conversion changed Unicode text");}finally{Array.Clear(bytes,0,bytes.Length);input.Clear();confirmation.Clear();}
            Check(read.Invoke(null,new object[]{input,null})==null,"An empty password was accepted");
            foreach(bool cancel in new[]{false,true}){
                PasswordBox[] controls=null;
                controller.Window.Dispatcher.BeginInvoke(new Action(()=>{
                    var dialog=controller.Window.OwnedWindows.OfType<Window>().Single(w=>w.Title=="Generated password lifetime check");
                    controls=Descendants<PasswordBox>(dialog).ToArray();foreach(var box in controls)box.Password="Generated temporary password";
                    if(cancel)dialog.Close();else Descendants<Button>(dialog).Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                var result=(byte[])Call(controller,"Password","Generated password lifetime check","Generated fixture",true);
                try{Check(cancel ? result==null : result!=null,"Password dialog returned the wrong submission state");foreach(var box in controls)using(var secure=box.SecurePassword)Check(secure.Length==0,"A closed password dialog retained its input");}finally{if(result!=null)Array.Clear(result,0,result.Length);}
            }
        }
        static void CheckThumbnailGeneration(ReviewerWindow controller,BitmapSource poster){
            var cards=Field<ObservableCollection<CardItem>>(controller,"cards");cards.Clear();
            var tiny=BitmapSource.Create(1,1,96,96,PixelFormats.Rgb24,null,new byte[]{10,20,30},3);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(tiny));byte[] encoded;using(var stream=new MemoryStream()){encoder.Save(stream);encoded=stream.ToArray();}
            var card=new CardItem(new MediaRow {GroupId=401,ContentId=401,Extension="png",PreviewBytes=encoded,Status="TO_REVIEW"});cards.Add(card);
            var loader=Field<MediaLoader>(controller,"loader");var generation=Field<ViewGeneration>(controller,"view");long old=generation.Current;
            Check(loader.QueueThumbnail(401,401,old,encoded),"Could not queue the initial thumbnail");card.ImageQueued=true;
            Call(controller,"InvalidateThumbnails");Check(!card.ImageQueued&&card.CanLoadImage&&!generation.Accept(old),"Generation invalidation left its pending thumbnail stuck");
            MediaLoadResult late=null;Wait(()=>loader.TryTakeResult(out late));Check(late.Generation==old&&!generation.Accept(late.Generation),"An in-flight old generation was accepted");
            Check(loader.QueueThumbnail(401,401,generation.Current,encoded),"The invalidated thumbnail could not be requeued");card.ImageQueued=true;
            MediaLoadResult fresh=null;Wait(()=>loader.TryTakeResult(out fresh));Check(generation.Accept(fresh.Generation),"A current thumbnail generation was rejected");Check(fresh.Image!=null&&fresh.Error==null,"A requeued valid thumbnail failed to decode: "+fresh.Error);
            card.SetImage(fresh.Image,fresh.Error);Check(!card.ImageQueued,"Completing the requeued thumbnail retained its pending marker");
        }
        static void CheckPackagePasswordOwnership(string directory){
            string path=Path.Combine(directory,"generated-password-package.mcrevidence");const string password="Generated package password";
            var vault=new VaultDescriptor {Salt=EvidenceFormat.Random(16),Iterations=1000,CheckIv=EvidenceFormat.Random(12)};
            var header=new PackageHeader {Version=1,Count=1,PackageId=Guid.NewGuid().ToString("N"),WorkspaceId="generated-fixture",OperationId="generated-password-fixture",PayloadHash=new string('a',64),CreatedUtc=DateTime.UtcNow.ToString("o"),Vault=vault};
            using(var key=Crypto.DeriveKey(Encoding.UTF8.GetBytes(password),vault.Salt,vault.Iterations)){
                vault.CheckCipher=Crypto.Encrypt(key,vault.CheckIv,EvidenceFormat.CheckAad(header.WorkspaceId),Encoding.UTF8.GetBytes("ImageComplianceReviewer evidence vault v1"));
                var record=new PackageRecord {GroupId=1,Key="generated",Hash=new string('b',64),Version=1,Size=1,ChunkCount=1};record.SealIdentity(header,0,key);
                using(var file=File.Create(path))using(var writer=new PackageFormat.Writer(file,CancellationToken.None)){
                    writer.Write(PackageFormat.Magic);writer.Object(header,65536);writer.Object(record,65536);
                    using(var cipher=new MemoryStream(new byte[16]))writer.Frame(3,0,EvidenceFormat.Random(12),cipher,null);writer.Seal(header,key);
                }
            }
            using(var session=EvidencePackageSession.Open(path)){
                Wait(()=>session.Ready&&!session.Busy);using(var opened=session.TakeResult())Check(opened!=null&&opened.Success,"Generated package failed to open");
                foreach(bool wrong in new[]{false,true}){
                    byte[] bytes=Encoding.UTF8.GetBytes(wrong ? "Wrong generated password" : password);object gate=Field<object>(session,"gate");
                    lock(gate){session.RequestUnlock(bytes);Check(bytes.Any(b=>b!=0),"Accepted asynchronous unlock cleared its password before its worker could use it");}
                    Wait(()=>!session.Busy);using(var result=session.TakeResult())Check(result!=null&&result.Success!=wrong,"Package password outcome was wrong");Check(bytes.All(b=>b==0),"Completed package unlock retained its password buffer");
                }
                byte[] cancelled=Encoding.UTF8.GetBytes(password);lock(Field<object>(session,"gate")){session.RequestUnlock(cancelled);session.Cancel();}Wait(()=>!session.Busy);Check(cancelled.All(b=>b==0),"Cancelled queued unlock retained its password");
                byte[] closing=Encoding.UTF8.GetBytes(password);lock(Field<object>(session,"gate")){session.RequestUnlock(closing);session.CloseAsync();}Wait(()=>session.Closed);Check(closing.All(b=>b==0),"Closing the package retained its queued password");
                byte[] rejected=Encoding.UTF8.GetBytes(password);bool failed=false;try{session.RequestUnlock(rejected);}catch(InvalidOperationException){failed=true;}Check(failed&&rejected.All(b=>b==0),"Failed unlock submission retained its password");
            }
        }
    }
}
