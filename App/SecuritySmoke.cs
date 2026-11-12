using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Reflection;
namespace MediaReviewer {
    static class SecuritySmoke {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int KeyCall(IntPtr db,byte[] key,int length);
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static byte[] Bytes(string value){return Encoding.UTF8.GetBytes(value);}
        static void WaitUi(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>35)throw new Exception("Security UI check timed out.");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
        static IEnumerable<T> Controls<T>(DependencyObject node) where T:DependencyObject {var found=node as T;if(found!=null)yield return found;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Controls<T>(VisualTreeHelper.GetChild(node,i)))yield return child;}
        static void Screenshot(Window window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);}
        static void Ui(string directory){
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                controller.Window.Show();var welcome=controller.CreateWelcomeWindow();welcome.Show();
                var buttons=Controls<Button>(welcome).Where(b=>b.Name.StartsWith("Startup")).ToArray();
                Check(buttons.Select(b=>b.Name).SequenceEqual(new[]{"StartupCreateButton","StartupCatalogButton","StartupExitButton"}),"Welcome must require a database or Exit.");
                Screenshot(welcome,Path.Combine(directory,"welcome.png"));welcome.Close();
                DatabaseCredentials access=null;
                var setup=controller.CreateDatabaseSetupWindow(true,value=>access=value);
                setup.Loaded+=(s,e)=>setup.Dispatcher.BeginInvoke(new Action(()=>{
                    var passwords=Controls<PasswordBox>(setup).ToDictionary(b=>b.Name);
                    passwords["SetupDatabasePassword"].Password="generated database password";passwords["SetupDatabaseConfirm"].Password="mismatched password";
                    passwords["SetupSupervisorPassword"].Password=passwords["SetupSupervisorConfirm"].Password="generated database password";
                    var submit=Controls<Button>(setup).Single(b=>b.Name=="SetupContinueButton");
                    submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(access==null && Controls<TextBlock>(setup).Single(b=>b.Name=="SetupFeedback").Text.Contains("do not match"),"Password mismatch must not finish setup.");
                    passwords["SetupDatabaseConfirm"].Password="generated database password";submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(access==null && Controls<TextBlock>(setup).Single(b=>b.Name=="SetupFeedback").Text.Contains("different"),"A shared database/Supervisor password must be rejected.");
                    passwords["SetupSupervisorPassword"].Password=passwords["SetupSupervisorConfirm"].Password="different supervisor password";
                    Screenshot(setup,Path.Combine(directory,"database-setup.png"));submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Check(setup.ShowDialog()==true && access!=null,"Confirmed independent passwords must finish setup.");
                typeof(ReviewerWindow).GetMethod("OpenPrepared",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{Path.Combine(directory,"welcome-protected.sqlite"),true,access});access=null;
                var engine=(ReviewerEngine)typeof(ReviewerWindow).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
                WaitUi(()=>engine.Ready&&((TextBlock)controller.Window.FindName("PageLabel")).Text.StartsWith("Page 1"));
                Action<string> unlock=password=>{
                    controller.Window.Dispatcher.BeginInvoke(new Action(()=>{
                        var dialog=controller.Window.OwnedWindows.OfType<Window>().Single(w=>w.Title=="Unlock Supervisor access");
                        if(password==null)dialog.Close();else{Controls<PasswordBox>(dialog).Single().Password=password;Controls<Button>(dialog).Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                    }));
                    ((Button)controller.Window.FindName("Queue3")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                unlock(null);Check(!engine.VaultUnlocked&&((ComboBox)controller.Window.FindName("ViewBox")).SelectedIndex==0,"Cancelling Supervisor unlock must keep Evidence closed.");
                unlock("generated database password");WaitUi(()=>((TextBlock)controller.Window.FindName("MessageText")).Text.Contains("failed"));
                Check(!engine.VaultUnlocked&&((ComboBox)controller.Window.FindName("ViewBox")).SelectedIndex==0,"The database password must not grant UI access to Evidence.");
                unlock("different supervisor password");WaitUi(()=>((ComboBox)controller.Window.FindName("ViewBox")).SelectedIndex==3);Check(engine.VaultUnlocked,"The Supervisor password must grant access to Evidence.");
                ((Button)controller.Window.FindName("VaultButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUi(()=>!engine.VaultUnlocked&&((ComboBox)controller.Window.FindName("ViewBox")).SelectedIndex==0);
                var cancel=controller.CreateDatabaseSetupWindow(true,value=>{value.Dispose();throw new Exception("Cancelled setup returned passwords.");});
                cancel.Loaded+=(s,e)=>cancel.Dispatcher.BeginInvoke(new Action(cancel.Close));Check(cancel.ShowDialog()!=true,"Cancelled setup must stay incomplete.");
                controller.Window.Close();
            }
            app.Shutdown();Console.WriteLine("Welcome, password confirmation, Evidence access, Supervisor locking and setup cancellation checks passed.");
        }
        static void Wait(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>35)throw new Exception("Security check timed out.");Thread.Sleep(10);}}
        static UiResult Result(ReviewerEngine engine,string id){UiResult result=null;Wait(()=>{UiResult next;while(engine.TryTakeResult(out next)){if(next.RequestId==id){result=next;return true;}if(next.Evidence!=null)next.Evidence.Dispose();}return false;});return result;}
        static byte[] ReadShared(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var copy=new MemoryStream()){file.CopyTo(copy);return copy.ToArray();}}
        static bool Rejects(string path,SessionPassword password){try{using(var db=new SqliteConnection(path,true,password))db.Scalar("SELECT COUNT(*) FROM sqlite_schema");return false;}catch(DatabaseException){return true;}}
        static void Sessions(string directory){
            const string password="generated database password",supervisor="different supervisor password";
            string path=Path.Combine(directory,"session.sqlite"),backup=Path.Combine(directory,"backup.sqlite"),copy=Path.Combine(directory,"copy.sqlite");
            byte[] supplied=Bytes(password),second=Bytes(supervisor);
            var credentials=new DatabaseCredentials(supplied,second);Check(supplied.All(b=>b==0)&&second.All(b=>b==0),"Submitted password buffers must be cleared.");
            using(var engine=ReviewerEngine.OpenProtected(path,true,credentials)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);Check(engine.DatabaseEncrypted && DatabaseProtection.IsEncrypted(path) && engine.VaultExists&&!engine.VaultUnlocked,"A new encrypted database must have encrypted bytes and a locked Supervisor vault, including through directory aliases.");
                using(var wrong=new SessionPassword(Bytes("wrong generated password")))Check(Rejects(path,wrong),"Wrong database passwords must fail.");
                Check(!Result(engine,engine.RequestVaultUnlock(Bytes(password),false)).Success && !engine.VaultUnlocked,"The database password must not unlock the Supervisor vault.");
                Check(Result(engine,engine.RequestVaultUnlock(Bytes(supervisor),false)).Success && engine.VaultUnlocked,"The separate Supervisor password must unlock its vault.");
                Check(Result(engine,engine.RequestVaultLock()).Success&&!engine.VaultUnlocked,"Supervisor lock must clear access.");
                bool refused=false;byte[] proposed=Bytes(password),current=Bytes(supervisor);try{engine.RequestVaultPasswordChange(current,proposed,Guid.NewGuid().ToString("N"));}catch(ArgumentException){refused=true;}
                Check(refused&&proposed.All(b=>b==0)&&current.All(b=>b==0),"Changing Supervisor credentials to the database password must fail and clear both input buffers.");
                Check(!Result(engine,engine.RequestEncryptedCopy(Path.Combine(directory,"same-password-copy.sqlite"),new DatabaseCredentials(Bytes(supervisor)))).Success,"An encrypted copy must not share its database and Supervisor passwords.");
                engine.WaitWrite(db=>{db.Execute("INSERT INTO settings VALUES('generated-secret','PRIVATE_SESSION_FIXTURE')");return null;});
                Check(Result(engine,engine.RequestBackup(backup)).Success,"Encrypted portable backup must complete.");
                using(var key=new SessionPassword(Bytes(password)))using(var db=new SqliteConnection(backup,true,key))Check((string)db.Scalar("SELECT value FROM settings WHERE key='generated-secret'")=="PRIVATE_SESSION_FIXTURE","Encrypted backup must preserve records and password.");
                Check(!Encoding.ASCII.GetString(ReadShared(path+"-wal")).Contains("PRIVATE_SESSION_FIXTURE"),"Session WAL must be encrypted.");
                using(var plain=new SqliteConnection(Path.Combine(directory,"legacy.sqlite"),false)){Catalog.Initialize(plain,true);plain.Execute("INSERT INTO settings VALUES('generated-secret','PRIVATE_LEGACY_FIXTURE')");plain.Execute("INSERT INTO import_extensions(rowid,kind,id,json) VALUES(113,'generated','streamed','')");using(var writer=new ImportedFields.Writer(plain,"import_extensions",113,"json","utf8",CancellationToken.None)){byte[] value=Bytes("PRIVATE_STREAMED_REFERENCE");writer.Write(value,0,value.Length);writer.Complete();}}
                using(var plain=new SqliteConnection(Path.Combine(directory,"legacy.sqlite"),true))using(var access=new DatabaseCredentials(Bytes("new encrypted copy password"),Bytes("new copy supervisor password")))EncryptedDatabaseCopy.Create(plain,copy,access,CancellationToken.None,(done,total,stage)=>{});
                Check(DatabaseProtection.IsEncrypted(copy),"A legacy database copy must be encrypted.");
                using(var key=new SessionPassword(Bytes("new encrypted copy password")))using(var db=new SqliteConnection(copy,true,key)){
                    Check((string)db.Scalar("SELECT value FROM settings WHERE key='generated-secret'")=="PRIVATE_LEGACY_FIXTURE","Encrypted copy must preserve legacy data.");
                    Check(Convert.ToInt64(db.Scalar("SELECT rowid FROM import_extensions WHERE id='streamed'"))==113,"Encrypted copies must preserve hidden rowids used by imported records.");
                    using(var stream=ImportedFields.Open(db,"import_extensions",113,"json"))using(var text=new StreamReader(stream))Check(text.ReadToEnd()=="PRIVATE_STREAMED_REFERENCE","Encrypted copy must preserve streamed legacy references.");
                }
                using(var plain=new SqliteConnection(Path.Combine(directory,"legacy.sqlite"),true))Check((string)plain.Scalar("SELECT value FROM settings WHERE key='generated-secret'")=="PRIVATE_LEGACY_FIXTURE","Copying must preserve its plaintext source.");
            }
            Check(Rejects(path,null)&&Rejects(backup,null),"Closed databases and backups must reject unkeyed access.");
            Check(DatabaseProtection.Find(path)==null,"Closed sessions must remove registered credentials.");
            using(var engine=ReviewerEngine.OpenProtected(path,false,new DatabaseCredentials(Bytes(password)))){Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready&&!engine.VaultUnlocked,"Reopening must keep Supervisor access locked.");Check(Result(engine,engine.RequestPage(new PageRequest())).Success,"Encrypted reader workers must load pages.");}
            string damaged=Path.Combine(directory,"damaged.sqlite");File.Copy(backup,damaged);using(var file=new FileStream(damaged,FileMode.Open,FileAccess.ReadWrite)){file.Position=150;int old=file.ReadByte();file.Position=150;file.WriteByte((byte)(old^1));}
            using(var key=new SessionPassword(Bytes(password)))Check(Rejects(damaged,key),"Modified encrypted pages must fail authentication.");
            Console.WriteLine("Session passwords, independent Supervisor access, readers, backup, encrypted copies and tamper checks passed.");
        }
        static void Imports(string directory){
            string source=Path.Combine(directory,"html-origin.sqlite"),html=Path.Combine(directory,"html-export.sqlite"),destination=Path.Combine(directory,"html-encrypted.sqlite");
            using(var db=new SqliteConnection(source,false)){
                Catalog.Initialize(db,true);using(var supervisor=new SessionPassword(Bytes("imported supervisor password")))DatabaseProtection.CreateSupervisor(db,supervisor);
                Interchange.Export(db,html);
            }
            Check(Interchange.DetectHtml(html,CancellationToken.None),"Generated legacy database must remain recognizable before encryption prompts.");
            using(var engine=ReviewerEngine.ImportHtmlProtected(html,destination,new DatabaseCredentials(Bytes("imported database password")))){
                Wait(()=>{
                    foreach(string path in Directory.GetFiles(directory,"html-encrypted.sqlite.importing-*"))if(!path.EndsWith("-wal")&&!path.EndsWith("-shm")&&!path.EndsWith("-journal")){
                        byte[] header=null;try{header=ReadShared(path);}catch(IOException){}
                        if(header!=null && header.Length>=16)Check(Encoding.ASCII.GetString(header,0,16)!="SQLite format 3\0","Import staging must be encrypted before publication.");
                    }
                    return engine.Ready||engine.Faulted;
                });
                Check(engine.Ready&&engine.DatabaseEncrypted&&DatabaseProtection.IsEncrypted(destination),"Legacy import must produce a usable encrypted database: "+engine.Status.Error);
                Check(Result(engine,engine.RequestVaultUnlock(Bytes("imported supervisor password"),false)).Success,"Protected import must preserve the existing Supervisor password.");
            }
            string refused=Path.Combine(directory,"html-matching-password.sqlite");
            using(var engine=ReviewerEngine.ImportHtmlProtected(html,refused,new DatabaseCredentials(Bytes("imported supervisor password")))){Wait(()=>engine.Ready||engine.Faulted);Check(engine.Faulted&&!File.Exists(refused),"Import must reject matching database and Supervisor passwords before publication.");}
            Console.WriteLine("Legacy import, encrypted staging, preserved Supervisor envelope and matching-password rejection passed.");
        }
        internal static int Run(string directory){
            Directory.CreateDirectory(directory);
            var key=Native.Export<KeyCall>("sqlite3_key");
            string path=Path.Combine(directory,"encrypted.sqlite");byte[] password=Encoding.UTF8.GetBytes("generated database password");
            using(var db=new SqliteConnection(path,false)){
                Check(key(db.Handle,password,password.Length)==0,"Encryption key must be accepted.");
                db.Script("PRAGMA journal_mode=WAL; CREATE TABLE secret(value TEXT); INSERT INTO secret VALUES('PRIVATE_GENERATED_FIXTURE');");
                Check(!Encoding.ASCII.GetString(ReadShared(path+"-wal")).Contains("PRIVATE_GENERATED_FIXTURE"),"WAL must not contain plaintext records.");
            }
            Check(!Encoding.ASCII.GetString(File.ReadAllBytes(path)).Contains("PRIVATE_GENERATED_FIXTURE"),"Database must not contain plaintext records.");
            bool rejected=false;try{using(var db=new SqliteConnection(path,true))db.Scalar("SELECT value FROM secret");}catch(DatabaseException){rejected=true;}
            Check(rejected,"An encrypted database must reject an unkeyed connection.");
            using(var db=new SqliteConnection(path,true)){Check(key(db.Handle,password,password.Length)==0,"Read key must be accepted.");Check((string)db.Scalar("SELECT value FROM secret")=="PRIVATE_GENERATED_FIXTURE","Correct password must recover the record.");}
            Array.Clear(password,0,password.Length);Console.WriteLine("Native encrypted database and WAL checks passed.");Sessions(directory);Imports(directory);Ui(directory);return 0;
        }
    }
}
